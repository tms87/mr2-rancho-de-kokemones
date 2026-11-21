using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using MRDX.Base.Mod.Interfaces;
using MRDX.Custom.RanchRules.Configuration;
using MRDX.Custom.RanchRules.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using Reloaded.Mod.Interfaces;
using Reloaded.Universal.Redirector.Interfaces;
using CallingConventions = Reloaded.Hooks.Definitions.X86.CallingConventions;

namespace MRDX.Custom.RanchRules;

// Firmas del santuario tomadas del mod de la comunidad "MRDX.Game.MoreMonsters" (ranchercommunity/mrdx_reloaded).
[HookDef(BaseGame.Mr2, Region.Us,
    "55 8B EC 6A FF 68 ?? ?? ?? ?? 64 A1 ?? ?? ?? ?? 50 83 EC 50 A1 ?? ?? ?? ?? 33 C5 89 45 ?? 53 56 57 50 8D 45 ?? 64 A3 ?? ?? ?? ?? 8B F9")]
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void H_EarlyShrine(nuint self, nuint p2);

[HookDef(BaseGame.Mr2, Region.Us, "55 8B EC 53 8B 5D ?? 8A C3")]
[Function(CallingConventions.MicrosoftThiscall)]
public delegate nuint H_ShrineMonsterUnlockedChecker(nuint self, int unk1, int unk2);

[HookDef(BaseGame.Mr2, Region.Us, "56 8B F1 57 8A 86 ?? ?? ?? ??")]
[Function(CallingConventions.Fastcall)]
public delegate int H_MysteryStatUpdate(nuint self);

public class Mod : ModBase
{
    private const int WeeksPerYear = 48; // 12 meses x 4 semanas
    private const int FirstYear = 1000;

    private readonly ILogger _logger = null!;
    private readonly IModConfig _modConfig = null!;
    private Config _config = null!;
    private readonly string _stateDir = "";

    private IHook<H_EarlyShrine>? _hookEarlyShrine;
    private IHook<H_ShrineMonsterUnlockedChecker>? _hookUnlocked;
    private IHook<H_MysteryStatUpdate>? _hookStatUpdate;

    private readonly ShrineMessage? _message;
    private SaveManager? _saves;
    private IHook<FrameStart>? _hookFrame;
    private readonly IGame? _game;

    private long _week = -1;
    private string _slot = "00";
    private ShrineState _shrine = new();
    private readonly Calendar _calendar = new();

    private bool _blocked;
    private readonly Stopwatch _pendingClock = new();
    private TournamentAccess? _tournamentAccess;
    private bool _pending;

    // Partida nueva: la fecha retrocede sin que se haya cargado una partida. Se confirma unos segundos después
    // (si en ese tiempo se carga una partida, era una carga y no una partida nueva).
    private readonly Stopwatch _dateWentBack = new();
    private const int NewGameConfirmMs = 5000;
    private const long StartWeek = 0; // contador de semanas al empezar una partida (medido en el juego)

    public Mod(ModContext context)
    {
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        _config = context.Configuration;
        var loader = context.ModLoader;

        _stateDir = loader.GetModConfigDirectory(_modConfig.ModId);
        Directory.CreateDirectory(Path.Combine(_stateDir, "santuario"));
        _calendar = Calendar.Load(CalendarPath);

        IHooks? hooks = null;
        IGame? game = null;
        loader.GetController<IHooks>()?.TryGetTarget(out hooks);
        loader.GetController<IGame>()?.TryGetTarget(out game);
        ISaveFile? save = null;
        loader.GetController<ISaveFile>()?.TryGetTarget(out save);
        IRedirectorController? redirector = null;
        loader.GetController<IRedirectorController>()?.TryGetTarget(out redirector);

        _message = new ShrineMessage(redirector, _stateDir, m => Log(m));

        if (hooks == null || game == null || save == null)
        {
            Log("No se encontró MRDX.Base.Mod (hooks/juego/guardado). ¿Está activado y por encima de este mod?", Color.Red);
            return;
        }

        _game = game;
        game.OnWeekChange += c =>
        {
            // 0xFFFFFFFF = el juego todavía no tiene fecha (transiciones de pantalla): se ignora.
            if (c.NewWeek == uint.MaxValue) return;
            var anterior = _week;
            _week = c.NewWeek;
            // Partida nueva: toda partida empieza en la semana 0 (año 1000, mes 4, semana 1) y ninguna partida guardada
            // puede estar en esa fecha. Los retrocesos chicos al cargar (p. ej. 899 -> 898) no llegan a 0.
            if (_week == StartWeek && anterior > StartWeek) _dateWentBack.Restart();
            UpdateMessage();
        };
        save.OnLoad += OnLoad;
        save.OnSave += OnSave;

        _saves = new SaveManager(() => _config, Log, _stateDir, hooks) { AutosaveDate = Calibrate };
        // Datos por ranura que viajan con la copia anual.
        _saves.AddCompanion(i => ShrinePath(i.ToString("00")));
        var toleranceDir = Path.Combine(loader.GetModConfigDirectory("MRDX.Custom.ItemTolerance"), "partidas");
        _saves.AddCompanion(i => Path.Combine(toleranceDir, $"tolerancia_{i:00}.json"));
        hooks.AddHook<FrameStart>(OnFrame).ContinueWith(r => _hookFrame = r.Result);

        hooks.AddHook<H_EarlyShrine>(OnEarlyShrine).ContinueWith(r => _hookEarlyShrine = r.Result);
        hooks.AddHook<H_ShrineMonsterUnlockedChecker>(OnUnlockedCheck).ContinueWith(r => _hookUnlocked = r.Result);
        hooks.AddHook<H_MysteryStatUpdate>(OnStatUpdate).ContinueWith(r => _hookStatUpdate = r.Result);

        if (context.Hooks != null)
            _tournamentAccess = new TournamentAccess(context.Hooks,
                () => (_config.BlockLowerRankTournaments, _config.LowerRanksAllowed), m => Log(m));

        Log("Cargado.", Color.LightGreen);
    }

    public Mod()
    {
    }

    public override void ConfigurationUpdated(Config configuration)
    {
        _config = configuration;
        UpdateMessage();
        Log("Configuración actualizada.");
    }

    // ------------------------------------------------------------------
    // Calendario: el contador de semanas del juego no empieza en enero; el desfase se mide en cada autoguardado
    // comparando el contador con la fecha real escrita en la partida.
    // ------------------------------------------------------------------

    private string CalendarPath => Path.Combine(_stateDir, "calendario.json");

    private long RealWeek => _week < 0 ? -1 : _week + _calendar.Offset;
    private int YearIndex => RealWeek < 0 ? -1 : (int)(RealWeek / WeeksPerYear);

    private string DateText => RealWeek < 0
        ? "fecha desconocida"
        : $"año {FirstYear + RealWeek / WeeksPerYear}, mes {RealWeek % WeeksPerYear / 4 + 1}, semana {RealWeek % 4 + 1}";

    // Contador de semanas del juego (misma dirección que usa MRDX.Base.Mod: exe + 0x379444).
    private static readonly nint WeekAddress = Process.GetCurrentProcess().MainModule!.BaseAddress + 0x379444;

    private static unsafe uint ReadWeek() => *(uint*)WeekAddress;

    private void Calibrate(int year, int month, int week)
    {
        try
        {
            var counter = ReadWeek();
            if (counter == uint.MaxValue || year < FirstYear) return;
            _week = counter;

            var real = (year - FirstYear) * WeeksPerYear + (month - 1) * 4 + (week - 1);
            var offset = real - (long)counter;
            if (offset < -WeeksPerYear || offset > WeeksPerYear || offset == _calendar.Offset) return;

            _calendar.Offset = (int)offset;
            _calendar.Save(CalendarPath);
            Log($"Calendario ajustado: el contador de semanas va {offset} semanas detrás de la fecha real.");
            UpdateMessage();
        }
        catch (Exception e)
        {
            Log($"Error ajustando el calendario: {e.Message}", Color.Red);
        }
    }

    // ------------------------------------------------------------------
    // Santuario
    // ------------------------------------------------------------------

    private int UsedThisYear => _shrine.YearIndex == YearIndex ? _shrine.Count : 0;

    private bool Exhausted => _config.ShrineLimitEnabled && _week >= 0 && UsedThisYear >= _config.ShrinePerYear;

    private void UpdateMessage() => _message?.SetRemaining(
        _config.ShrineLimitEnabled && _week >= 0 ? Math.Max(0, _config.ShrinePerYear - UsedThisYear) : null);

    private void OnEarlyShrine(nuint self, nuint p2)
    {
        _hookEarlyShrine!.OriginalFunction(self, p2);

        _blocked = false;
        _pending = false;
        if (!_config.ShrineLimitEnabled) return;

        try
        {
            var used = UsedThisYear;
            if (Exhausted)
            {
                _blocked = true;
                Log($"Sin invocaciones: ya se usaron {used}/{_config.ShrinePerYear} este año ({DateText}).",
                    Color.Orange);
            }
            else
            {
                _pending = true;
                _pendingClock.Restart();
                if (_config.VerboseLog)
                    Log($"Santuario: {used}/{_config.ShrinePerYear} invocaciones usadas este año ({DateText}).");
            }
        }
        catch (Exception e)
        {
            Log($"Error en el límite del santuario: {e.Message}", Color.Red);
        }
    }

    private nuint OnUnlockedCheck(nuint self, int unk1, int unk2)
    {
        var ret = _hookUnlocked!.OriginalFunction(self, unk1, unk2);
        if (!_blocked) return ret;

        _blocked = false;
        return 0; // el santuario rechaza: el jugador ve el mensaje de rechazo
    }

    private int OnStatUpdate(nuint self)
    {
        var ret = _hookStatUpdate!.OriginalFunction(self);

        // Solo cuenta si viene de un santuario aprobado hace poco (no de otras operaciones del juego).
        if (!_pending || _pendingClock.ElapsedMilliseconds > 60_000) return ret;
        _pending = false;

        try
        {
            if (_shrine.YearIndex != YearIndex)
            {
                _shrine.YearIndex = YearIndex;
                _shrine.Count = 0;
            }

            _shrine.Count++;
            var left = Math.Max(0, _config.ShrinePerYear - _shrine.Count);
            Log($"Monstruo creado en el santuario. Quedan {left} invocaciones este año.", Color.Yellow);
            UpdateMessage();
        }
        catch (Exception e)
        {
            Log($"Error contando la invocación: {e.Message}", Color.Red);
        }

        return ret;
    }

    // ------------------------------------------------------------------
    // Guardado: los datos del santuario viajan con cada ranura
    // ------------------------------------------------------------------

    private string ShrinePath(string slot) => Path.Combine(_stateDir, "santuario", $"ranura_{slot}.json");

    private void OnLoad(ISaveFileEntry file)
    {
        _dateWentBack.Reset(); // era una carga, no una partida nueva
        _slot = file.Slot;
        _shrine = ShrineState.Load(ShrinePath(_slot));
        _blocked = false;
        _pending = false;
        // Al cargar, el contador del juego todavía puede valer 0: la fecha sale de la cabecera de la partida.
        var date = LoadedDate(file.Filename);
        if (date != null)
            _week = (date.Value.Year - FirstYear) * WeeksPerYear + (date.Value.Month - 1) * 4 + (date.Value.Week - 1) -
                    _calendar.Offset;
        else if (ReadWeek() is var current and > 0 and not uint.MaxValue) _week = current;
        Log($"Ranura {SlotLabel(_slot)} cargada. Santuario: {UsedThisYear} invocaciones usadas este año ({DateText}).");
        UpdateMessage();
        _saves?.OnLoad(file);
    }

    private void OnSave(ISaveFileEntry file)
    {
        // Con el modo ironman, el juego informa la ranura 15 (la que realmente escribió).
        _slot = file.Slot;
        try
        {
            _shrine.Save(ShrinePath(_slot));
        }
        catch (Exception e)
        {
            Log($"No se pudieron guardar los datos del santuario: {e.Message}", Color.Red);
        }
    }

    private static (int Year, int Month, int Week)? LoadedDate(string path)
    {
        try
        {
            return File.Exists(path) ? SaveManager.ParseDate(File.ReadAllBytes(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private int OnFrame()
    {
        var ret = _hookFrame!.OriginalFunction();
        _saves?.OnFrame();

        // La fecha retrocedió hace unos segundos y no se cargó ninguna partida: es una partida nueva.
        if (_dateWentBack.IsRunning && _dateWentBack.ElapsedMilliseconds > NewGameConfirmMs)
        {
            _dateWentBack.Reset();
            _shrine = new ShrineState();
            _blocked = false;
            _pending = false;
            Log($"Partida nueva: el santuario vuelve a tener {_config.ShrinePerYear} invocaciones ({DateText}).",
                Color.LightGreen);
            UpdateMessage();
        }

        return ret;
    }

    private static string SlotLabel(string slot) => int.TryParse(slot, out var n) ? (n + 1).ToString() : slot;

    private void Log(string msg, Color? color = null) =>
        _logger.WriteLine($"[{_modConfig.ModId}] {msg}", color ?? Color.White);
}

public class Calendar
{
    /// <summary>Semanas que hay que sumar al contador del juego para obtener la fecha real (medido: 12).</summary>
    public int Offset { get; set; } = 12;

    public static Calendar Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Calendar>(File.ReadAllText(path)) ?? new Calendar()
                : new Calendar();
        }
        catch
        {
            return new Calendar();
        }
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this));
}

public class ShrineState
{
    public int YearIndex { get; set; } = -1;
    public int Count { get; set; }

    public static ShrineState Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ShrineState>(File.ReadAllText(path)) ?? new ShrineState()
                : new ShrineState();
        }
        catch
        {
            return new ShrineState();
        }
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this));
}
