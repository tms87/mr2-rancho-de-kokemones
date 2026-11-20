using System.Diagnostics;
using System.Drawing;
using MRDX.Base.Mod.Interfaces;
using MRDX.Custom.ItemTolerance.Configuration;
using MRDX.Custom.ItemTolerance.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using Reloaded.Mod.Interfaces;
using CallingConventions = Reloaded.Hooks.Definitions.X86.CallingConventions;

namespace MRDX.Custom.ItemTolerance;

// Función del juego que se ejecuta al dar un objeto al monstruo.
// Firma tomada del mod de la comunidad "MRDX.Qol.MagicBananaStatic" (ranchercommunity/mrdx_reloaded).
// El ID del objeto está en p1 + 76.
[HookDef(BaseGame.Mr2, Region.Us, "55 8B EC B9 01 00 00 00 83 EC 18")]
[Function(CallingConventions.Cdecl)]
public delegate void H_ItemUsed(int p1, uint p2, uint p3);

public class Mod : ModBase
{
    private const int SharedKey = -1;

    // Tras dar el objeto, durante cuánto tiempo atribuimos los cambios del monstruo al objeto.
    // (Medido en el juego: el efecto llega ~30 ms después de dar el objeto.)
    private const int EffectWindowMs = 3000;

    // Si el efecto llegó justo antes de la llamada al hook, lo aceptamos si tiene menos de esto.
    private const int RecentChangeMs = 3000;

    private readonly ILogger _logger = null!;
    private readonly IModConfig _modConfig = null!;
    private readonly IModLoader _modLoader = null!;
    private Config _config = null!;

    private readonly IGame? _game;
    private IHook<H_ItemUsed>? _hookItemUsed;
    private IHook<FrameStart>? _hookFrameStart;

    private readonly Random _rng = new();
    private readonly string _dataDir = "";
    private string _slot = "sin-ranura";
    private SlotData _data = new();

    private Pending? _pending;
    private Change? _recent;

    private sealed class Pending
    {
        public int ItemId;
        public string ItemName = "";
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public int ToleranceBefore;
        public double Mult;
        public bool Counted;
        public bool FatigueDone;
        public bool StressDone;
    }

    private sealed class Change
    {
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public byte PrevFatigue, CurFatigue;
        public sbyte PrevStress, CurStress;
        public bool OtherChanged;
    }

    public Mod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        _config = context.Configuration;

        _dataDir = Path.Combine(_modLoader.GetModConfigDirectory(_modConfig.ModId), "partidas");
        Directory.CreateDirectory(_dataDir);

        IHooks? hooks = null;
        _modLoader.GetController<IHooks>()?.TryGetTarget(out hooks);
        _modLoader.GetController<IGame>()?.TryGetTarget(out _game);
        var saveCtl = _modLoader.GetController<ISaveFile>();

        if (hooks == null || _game == null)
        {
            Log("No se encontró MRDX.Base.Mod (hooks/juego). ¿Está activado y por encima de este mod?", Color.Red);
            return;
        }

        hooks.AddHook<H_ItemUsed>(OnItemUsed).ContinueWith(r => _hookItemUsed = r.Result);
        hooks.AddHook<FrameStart>(OnFrameStart).ContinueWith(r => _hookFrameStart = r.Result);
        _game.OnWeekChange += OnWeekChange;
        _game.OnMonsterChanged += OnMonsterChanged;

        if (saveCtl != null && saveCtl.TryGetTarget(out var save))
        {
            save.OnLoad += OnLoad;
            save.OnSave += OnSave;
        }
        else
        {
            Log("No se pudo enganchar el guardado: la tolerancia no se conservará entre sesiones.", Color.Orange);
        }

        Log("Cargado.", Color.LightGreen);
    }

    // Constructor vacío requerido por la plantilla de Reloaded.
    public Mod()
    {
    }

    public override void ConfigurationUpdated(Config configuration)
    {
        _config = configuration;
        Log("Configuración actualizada.");
    }

    // ------------------------------------------------------------------
    // Detección del objeto
    // ------------------------------------------------------------------

    private unsafe void OnItemUsed(int p1, uint p2, uint p3)
    {
        try
        {
            var itemId = *(byte*)(p1 + 76);
            var m = _game!.Monster;

            if (_config.VerboseLog)
                Log($"Objeto dado: ID {itemId} (ya comió esta semana: {m.ItemUsed}). " +
                    $"Cansancio {m.Fatigue}, Estrés {m.Stress}, Mimo {m.LoyalSpoil}, Miedo {m.LoyalFear}");

            var name = NameOf(itemId);
            if (name != null && !m.ItemUsed)
            {
                var rec = _data.For(m);
                rec.Tolerance.TryGetValue(KeyFor(itemId), out var tolerance);
                var mult = Math.Max(_config.MinimumEffectPercent, 100 - _config.ToleranceStepPercent * tolerance);

                _pending = new Pending
                {
                    ItemId = itemId,
                    ItemName = name,
                    ToleranceBefore = tolerance,
                    Mult = Math.Clamp(mult, 0, 100) / 100.0
                };

                // Por si el juego aplicó el efecto justo antes de llamar a esta función.
                if (_recent != null && _recent.Clock.ElapsedMilliseconds <= RecentChangeMs)
                {
                    if (_config.VerboseLog) Log("El efecto llegó antes del aviso del objeto; se aplica igualmente.");
                    Process(_pending, _recent);
                }

                _recent = null;
            }
        }
        catch (Exception e)
        {
            Log($"Error al leer el objeto: {e.Message}", Color.Red);
        }

        _hookItemUsed!.OriginalFunction(p1, p2, p3);
    }

    private void OnMonsterChanged(IMonsterChange change)
    {
        try
        {
            var prev = change.Previous;
            var cur = change.Current;
            var c = new Change
            {
                PrevFatigue = prev.Fatigue, CurFatigue = cur.Fatigue,
                PrevStress = prev.Stress, CurStress = cur.Stress,
                OtherChanged = prev.LoyalSpoil != cur.LoyalSpoil || prev.LoyalFear != cur.LoyalFear
            };

            var relevant = c.CurFatigue != c.PrevFatigue || c.CurStress != c.PrevStress || c.OtherChanged;
            if (!relevant) return;

            var p = _pending;
            if (p == null)
            {
                _recent = c;
                return;
            }

            if (_config.VerboseLog)
                Log($"  +{p.Clock.ElapsedMilliseconds}ms cambio: Cansancio {c.PrevFatigue}→{c.CurFatigue}, " +
                    $"Estrés {c.PrevStress}→{c.CurStress}, Mimo {prev.LoyalSpoil}→{cur.LoyalSpoil}, " +
                    $"Miedo {prev.LoyalFear}→{cur.LoyalFear}");

            Process(p, c);
        }
        catch (Exception e)
        {
            Log($"Error procesando cambio del monstruo: {e.Message}", Color.Red);
        }
    }

    private int OnFrameStart()
    {
        var result = _hookFrameStart!.OriginalFunction();

        var p = _pending;
        if (p != null && p.Clock.ElapsedMilliseconds > EffectWindowMs)
        {
            _pending = null;
            if (!p.Counted && _config.VerboseLog)
                Log($"{p.ItemName}: no se detectó ningún efecto (¿lo rechazó o ya estaba a 0?). No cuenta.");
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Lógica principal
    // ------------------------------------------------------------------

    private void Process(Pending p, Change c)
    {
        var m = _game!.Monster;
        var any = c.OtherChanged;

        // 1) Efecto decreciente: solo se recorta la parte beneficiosa (bajada de cansancio y estrés).
        if (!p.FatigueDone && c.CurFatigue < c.PrevFatigue)
        {
            p.FatigueDone = true;
            any = true;
            var reduction = c.PrevFatigue - c.CurFatigue;
            var target = (byte)(c.PrevFatigue - (int)Math.Round(reduction * p.Mult));
            if (target != c.CurFatigue) m.Fatigue = target;
            Log($"{p.ItemName}: cansancio {c.PrevFatigue}→{target} (sin mod: {c.CurFatigue}), " +
                $"efecto al {p.Mult * 100:0}% (tolerancia {p.ToleranceBefore}).", Color.Yellow);
        }

        if (!p.StressDone && c.CurStress < c.PrevStress)
        {
            p.StressDone = true;
            any = true;
            var reduction = c.PrevStress - c.CurStress;
            var target = (sbyte)(c.PrevStress - (int)Math.Round(reduction * p.Mult));
            if (target != c.CurStress) m.Stress = target;
            Log($"{p.ItemName}: estrés {c.PrevStress}→{target} (sin mod: {c.CurStress}), " +
                $"efecto al {p.Mult * 100:0}% (tolerancia {p.ToleranceBefore}).", Color.Yellow);
        }

        if (any && !p.Counted)
        {
            p.Counted = true;
            Count(p, m);
        }
    }

    private void Count(Pending p, IMonster m)
    {
        var rec = _data.For(m);
        var key = KeyFor(p.ItemId);

        // 2) Subir tolerancia y marcar uso esta semana.
        rec.Tolerance[key] = Math.Min(_config.MaxTolerance, p.ToleranceBefore + 1);
        rec.UsedThisWeek.Add(key);

        // 3) Riesgo de acortar la vida, creciente con el total ingerido en la vida del monstruo.
        rec.LifetimeCount++;
        var chance = Math.Min(_config.LifespanMaxChancePercent,
            _config.LifespanBaseChancePercent + _config.LifespanChancePerItemPercent * rec.LifetimeCount);
        var roll = _rng.Next(100);

        if (roll < chance && _config.LifespanWeeksLost > 0)
        {
            var before = m.Lifespan;
            m.Lifespan = (ushort)Math.Max(1, before - _config.LifespanWeeksLost);
            Log($"¡{p.ItemName} le ha sentado mal! Vida {before}→{m.Lifespan} " +
                $"(probabilidad {chance}%, objetos en su vida: {rec.LifetimeCount}).", Color.OrangeRed);
        }
        else
        {
            Log($"Riesgo de vida superado (probabilidad {chance}%, objetos en su vida: {rec.LifetimeCount}). " +
                $"Tolerancia ahora: {rec.Tolerance[key]}.");
        }
    }

    private int KeyFor(int itemId) => _config.SharedTolerance ? SharedKey : itemId;

    private void OnWeekChange(IWeekChange change)
    {
        // Lo que ocurra después de pasar la semana ya no es efecto del objeto.
        _pending = null;
        _recent = null;

        // Ignora saltos raros (carga de partida, inicio): solo avances de 1 a 12 semanas.
        if (change.OldWeek == uint.MaxValue || change.NewWeek <= change.OldWeek) return;
        var weeks = (int)(change.NewWeek - change.OldWeek);
        if (weeks > 12) return;

        var m = _game?.Monster;
        if (m == null) return;

        var rec = _data.For(m);
        foreach (var key in rec.Tolerance.Keys.ToList())
        {
            // La semana en que se usó el objeto no cuenta como recuperación.
            var recover = weeks - (rec.UsedThisWeek.Contains(key) ? 1 : 0);
            rec.Tolerance[key] = Math.Max(0, rec.Tolerance[key] - recover);
        }

        rec.UsedThisWeek.Clear();

        if (_config.VerboseLog && rec.Tolerance.Count > 0)
            Log("Nueva semana. Tolerancia: " +
                string.Join(", ", rec.Tolerance.Select(kv => $"{KeyName(kv.Key)}={kv.Value}")));
    }

    // ------------------------------------------------------------------
    // Guardado por ranura
    // ------------------------------------------------------------------

    private string FileFor(string slot) => Path.Combine(_dataDir, $"tolerancia_{slot}.json");

    private void OnLoad(ISaveFileEntry file)
    {
        try
        {
            _slot = file.Slot;
            _data = SlotData.Load(FileFor(_slot));
            _pending = null;
            Log($"Datos cargados para la ranura {_slot}.");
        }
        catch (Exception e)
        {
            _data = new SlotData();
            Log($"No se pudieron leer los datos de la ranura {file.Slot}: {e.Message}", Color.Red);
        }
    }

    private void OnSave(ISaveFileEntry file)
    {
        try
        {
            _slot = file.Slot;
            _data.Save(FileFor(_slot));
            if (_config.VerboseLog) Log($"Datos guardados en la ranura {_slot}.");
        }
        catch (Exception e)
        {
            Log($"No se pudieron guardar los datos: {e.Message}", Color.Red);
        }
    }

    // ------------------------------------------------------------------

    private string? NameOf(int itemId)
    {
        if (itemId == _config.NutsOilId) return "Nuts Oil";
        if (itemId == _config.MagicBananaId) return "Magic Banana";
        return null;
    }

    private string KeyName(int key) => key == SharedKey ? "compartida" : NameOf(key) ?? key.ToString();

    private void Log(string msg, Color? color = null) =>
        _logger.WriteLine($"[{_modConfig.ModId}] {msg}", color ?? Color.White);
}
