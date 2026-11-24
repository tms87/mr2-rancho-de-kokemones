using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;
using MRDX.Base.Mod.Interfaces;
using MRDX.Custom.RanchRules.Configuration;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using CallingConventions = Reloaded.Hooks.Definitions.X86.CallingConventions;

namespace MRDX.Custom.RanchRules;

// Funciones del juego (MF2.exe, versión US de Steam), encontradas desensamblando el ejecutable.

/// <summary>Abre un archivo de partida. ecx = ruta ("bu00/BISLPS-01906NN"), edx = modo (1 = escribir).</summary>
[HookDef(BaseGame.Mr2, Region.Us,
    "55 8B EC 6A FF 68 ?? ?? ?? ?? 64 A1 00 00 00 00 50 81 EC B0 00 00 00 A1 ?? ?? ?? ?? 33 C5 89 45 F0 53 56 57 50 8D 45 F4 64 A3 00 00 00 00 8B DA 8B F1 80 3E 00")]
[Function(new[] { FunctionAttribute.Register.ecx, FunctionAttribute.Register.edx }, FunctionAttribute.Register.eax,
    FunctionAttribute.StackCleanup.Caller)]
public delegate int H_SaveFileOpen(nint path, int mode, int extra);

/// <summary>
///     Registra la huella de una ranura (0..14) en el índice de partidas leyendo el archivo que ya está en disco.
///     El juego lo llama después de escribir una partida. Devuelve al != 0 si salió bien.
/// </summary>
[HookDef(BaseGame.Mr2, Region.Us,
    "55 8B EC 6A FF 68 ?? ?? ?? ?? 64 A1 00 00 00 00 50 83 EC 38 A1 ?? ?? ?? ?? 33 C5 89 45 EC 53 56 57 50 8D 45 F4 64 A3 00 00 00 00 8B F1 8B 7D 08 C7 45 D0 0F 00 00 00")]
[Function(CallingConventions.MicrosoftThiscall)]
public delegate int H_RegisterSlot(nint self, int slot);

/// <summary>Escribe psdata001.bin (el índice con las huellas de todas las ranuras).</summary>
[HookDef(BaseGame.Mr2, Region.Us,
    "55 8B EC 6A FF 68 ?? ?? ?? ?? 64 A1 00 00 00 00 50 83 EC 34 A1 ?? ?? ?? ?? 33 C5 89 45 F0 53 56 57 50 8D 45 F4 64 A3 00 00 00 00 8B F1 8D 9E 5C 09 00 00")]
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void H_WriteSaveIndex(nint self);

/// <summary>
///     Ranuras: archivos "BISLPS-01906NN" (NN = 00..14) en Documents\KoeiTecmo\mfdx_en\bu00. Ranura del juego = NN + 1.
///     La 15 (archivo 14) es el autoguardado.
///     - Guardado manual: cuando el juego va a escribir la ranura elegida, se cambia la ruta por la del autoguardado,
///       y el registro de la huella también se hace para el autoguardado. Escribe el juego, así que la huella es válida.
///     - Copia anual: tras el primer guardado de un año nuevo se copia el autoguardado a otra ranura y se le pide al
///       juego que registre la huella de esa ranura y reescriba psdata001.bin.
/// </summary>
public sealed partial class SaveManager
{
    private const int AutosaveIndex = 14;
    private const int SlotCount = 15;
    private const string SlotPrefix = "BISLPS-01906";

    private readonly Func<Config> _config;
    private readonly Action<string, Color?> _log;
    private readonly string _stateDir;
    private readonly RotationState _state;
    private readonly List<Func<int, string>> _companions = new();

    private IHook<H_SaveFileOpen>? _hookOpen;
    private IHook<H_RegisterSlot>? _hookRegister;
    private IHook<H_WriteSaveIndex>? _hookWriteIndex;

    private string? _folder;
    private nint _indexObject;

    // Guardado manual redirigido en curso.
    private int _redirectedFrom = -1;
    private int _redirectTo = AutosaveIndex;
    private readonly Stopwatch _redirectClock = new();

    // Índice de archivo (7..13) de la grabación de competencia cargada, o -1.
    private int _loadedCompetition = -1;
    private readonly Stopwatch _lastWrite = Stopwatch.StartNew();

    // Guardado bloqueado en curso (grabación de competencia cargada): ranura que el juego iba a escribir.
    private int _blockedSlot = -1;
    private readonly Stopwatch _blockedClock = new();

    // Copia anual pendiente (se hace unos segundos después, cuando todos los mods ya guardaron sus datos).
    private int _pendingYear;
    private readonly Stopwatch _pendingClock = new();

    public SaveManager(Func<Config> config, Action<string, Color?> log, string stateDir, IHooks hooks)
    {
        _config = config;
        _log = log;
        _stateDir = stateDir;
        Directory.CreateDirectory(Path.Combine(_stateDir, "papelera"));
        _state = RotationState.Load(StatePath);

        hooks.AddHook<H_SaveFileOpen>(OnOpen).ContinueWith(r => _hookOpen = r.Result);
        hooks.AddHook<H_RegisterSlot>(OnRegister).ContinueWith(r => _hookRegister = r.Result);
        hooks.AddHook<H_WriteSaveIndex>(OnWriteIndex).ContinueWith(r => _hookWriteIndex = r.Result);
    }

    /// <summary>Datos de un mod que se guardan por ranura (ruta en función del índice 0..14); viajan con la copia anual.</summary>
    public void AddCompanion(Func<int, string> pathForSlot) => _companions.Add(pathForSlot);

    private string StatePath => Path.Combine(_stateDir, "rotacion.json");

    private string Folder => _folder ??= Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KoeiTecmo", "mfdx_en", "bu00");

    private string SlotFile(int index) => Path.Combine(Folder, SlotPrefix + index.ToString("00"));

    // ------------------------------------------------------------------
    // Hooks del juego
    // ------------------------------------------------------------------

    private unsafe int OnOpen(nint path, int mode, int extra)
    {
        try
        {
            if (mode == 1) _lastWrite.Restart();
            if (mode == 1 && path != 0 && (_config().IronmanMode || _loadedCompetition >= 0))
            {
                var p = (byte*)path;
                var len = 0;
                while (len < 260 && p[len] != 0) len++;
                var text = Encoding.ASCII.GetString(p, len);
                var idx = text.LastIndexOf(SlotPrefix, StringComparison.Ordinal);

                if (idx >= 0 && len - idx == SlotPrefix.Length + 2 && int.TryParse(text.AsSpan(len - 2, 2), out var slot))
                {
                    // Con una grabación de competencia cargada no se guarda nada (tampoco el autoguardado): el juego
                    // escribe en NUL (se descarta) y no se registra la huella, así ninguna ranura cambia y la grabación
                    // queda como la armó el mod.
                    if (CompetitionBlocked)
                    {
                        p[0] = (byte)'N';
                        p[1] = (byte)'U';
                        p[2] = (byte)'L';
                        p[3] = 0;
                        if (_blockedSlot != slot)
                            _log($"Grabación de competencia cargada: guardado bloqueado (ranura {slot + 1}); no se escribe nada.",
                                Color.Orange);
                        _blockedSlot = slot;
                        _blockedClock.Restart();
                        return _hookOpen!.OriginalFunction(path, mode, extra);
                    }

                    // Grabación de competencia con guardado permitido: todo va a esa misma ranura (nunca al
                    // autoguardado del jugador). Si no, ironman: todo va al autoguardado.
                    var target = _loadedCompetition >= 0 ? _loadedCompetition : AutosaveIndex;
                    if (slot != target)
                    {
                        // Misma longitud: se cambian los dos últimos dígitos en el propio buffer del juego.
                        var digits = target.ToString("00");
                        p[len - 2] = (byte)digits[0];
                        p[len - 1] = (byte)digits[1];
                        if (_redirectedFrom != slot)
                            _log(_loadedCompetition >= 0
                                ? $"Grabación de competencia cargada: el guardado en la ranura {slot + 1} se escribe en la {target + 1}."
                                : $"Guardado manual en la ranura {slot + 1} → se escribe en la 15 (autoguardado).", null);
                        _redirectedFrom = slot;
                        _redirectTo = target;
                        _redirectClock.Restart();
                    }
                }
            }
        }
        catch (Exception e)
        {
            _log($"Error al redirigir el guardado: {e.Message}", Color.Red);
        }

        return _hookOpen!.OriginalFunction(path, mode, extra);
    }

    private int OnRegister(nint self, int slot)
    {
        _indexObject = self;

        // Guardado bloqueado (grabación de competencia): no se registra la huella; la ranura sigue como estaba.
        if (_blockedSlot >= 0 && slot == _blockedSlot && _blockedClock.ElapsedMilliseconds < 30_000)
        {
            _blockedSlot = -1;
            if (_config().VerboseLog) _log($"Ranura {slot + 1}: huella sin registrar (guardado bloqueado).", null);
            return 1;
        }

        if (_redirectedFrom >= 0 && slot == _redirectedFrom && _redirectClock.ElapsedMilliseconds < 30_000)
            slot = _redirectTo;
        _redirectedFrom = -1;

        var ret = _hookRegister!.OriginalFunction(self, slot);

        try
        {
            if ((ret & 0xFF) != 0 && _loadedCompetition >= 0 && slot == _loadedCompetition) AdoptCompetitionSave(slot);
            else if ((ret & 0xFF) != 0 && slot == AutosaveIndex) CheckNewYear();
            else if ((ret & 0xFF) != 0 && _config().VerboseLog) _log($"Ranura {slot + 1} guardada.", null);
        }
        catch (Exception e)
        {
            _log($"Error comprobando la copia anual: {e.Message}", Color.Red);
        }

        return ret;
    }

    private void OnWriteIndex(nint self)
    {
        _hookWriteIndex!.OriginalFunction(self);
    }

    /// <summary>Llamar en cada frame (hilo del juego).</summary>
    public void OnFrame()
    {
        CompetitionsOnFrame();
        if (_pendingYear == 0) return;
        if (_pendingClock.ElapsedMilliseconds < 2500 || _lastWrite.ElapsedMilliseconds < 1500) return;

        var year = _pendingYear;
        _pendingYear = 0;
        try
        {
            MakeYearlyCopy(year);
        }
        catch (Exception e)
        {
            _log($"Error haciendo la copia anual: {e.Message}", Color.Red);
        }
    }

    // ------------------------------------------------------------------

    public void OnLoad(ISaveFileEntry entry)
    {
        _pendingYear = 0;
        _redirectedFrom = -1;
        var dir = Path.GetDirectoryName(entry.Filename);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) _folder = dir;

        var name = Path.GetFileName(entry.Filename) ?? "";
        _loadedCompetition = name.StartsWith(SlotPrefix) && int.TryParse(name.AsSpan(name.Length - 2), out var loaded) &&
                             loaded + 1 is >= FirstCompetitionSlot and <= LastCompetitionSlot &&
                             CompetitionFile(loaded + 1) != null
            ? loaded
            : -1;
        if (_loadedCompetition >= 0)
            _log(CompetitionBlocked
                ? $"Grabación de competencia (ranura {_loadedCompetition + 1}): no se guarda nada (tampoco el autoguardado); " +
                  "al volver a cargarla está igual."
                : $"Grabación de competencia (ranura {_loadedCompetition + 1}): guardado permitido; lo que guardes queda en esta ranura.",
                Color.Orange);

        // El año de referencia es el de la partida cargada (también si es una copia de un año anterior).
        var data = ReadOrNull(entry.Filename);
        var date = data == null ? null : ParseDate(data);
        if (date == null || _state.LastYear == date.Value.Year) return;

        _state.LastYear = date.Value.Year;
        _state.Save(StatePath);
        if (_config().VerboseLog) _log($"Partida cargada del año {date.Value.Year}.", null);
    }

    /// <summary>Fecha real del autoguardado recién escrito (año, mes, semana), para calibrar el calendario.</summary>
    public Action<int, int, int>? AutosaveDate { get; init; }

    private void CheckNewYear()
    {
        var cfg = _config();
        var data = ReadOrNull(SlotFile(AutosaveIndex));
        var date = data == null ? null : ParseDate(data);
        if (date == null)
        {
            _log("No se pudo leer la fecha del autoguardado.", Color.Orange);
            return;
        }

        var (year, month, week) = date.Value;
        if (cfg.VerboseLog) _log($"Autoguardado: año {year}, mes {month}, semana {week}.", null);
        AutosaveDate?.Invoke(year, month, week);
        if (!cfg.YearlyBackup) return;

        if (_state.LastYear == 0 || year < _state.LastYear)
        {
            _state.LastYear = year;
            _state.Save(StatePath);
            return;
        }

        if (year == _state.LastYear) return;

        _pendingYear = year;
        _pendingClock.Restart();
    }

    private void MakeYearlyCopy(int year)
    {
        var cfg = _config();
        if (_hookRegister == null || _hookWriteIndex == null || _indexObject == 0)
        {
            _log("Copia anual no disponible: faltan funciones del juego.", Color.Red);
            return;
        }

        // Ranuras del juego (1..15) → índices de archivo (0..14).
        // Ironman: ranuras 1 a 7 (de la 8 a la 14 son para competencias). Normal: ranuras 8 a 13 (las demás quedan
        // libres para el jugador). En los dos casos se saltean las ranuras que tienen un competenciaNN.json.
        var (first, last) = cfg.IronmanMode ? (0, 6) : (7, 12);
        var target = _state.NextIndex;
        if (target < first || target > last) target = first;
        for (var tries = 0; CompetitionFile(target + 1) != null; tries++)
        {
            if (tries > last - first)
            {
                _log("Copia anual: todas las ranuras de copias tienen una competencia asignada.", Color.Orange);
                return;
            }

            target = target + 1 > last ? first : target + 1;
        }

        var source = SlotFile(AutosaveIndex);
        var dest = SlotFile(target);
        ToTrash(dest);
        File.Copy(source, dest, true);

        // El juego calcula la huella del archivo copiado y reescribe psdata001.bin.
        var ok = (_hookRegister.OriginalFunction(_indexObject, target) & 0xFF) != 0;
        if (!ok)
        {
            _log($"El juego no aceptó la copia en la ranura {target + 1}.", Color.Red);
            return;
        }

        _hookWriteIndex.OriginalFunction(_indexObject);

        foreach (var companion in _companions)
        {
            var from = companion(AutosaveIndex);
            var to = companion(target);
            if (File.Exists(from)) File.Copy(from, to, true);
            else if (File.Exists(to)) File.Delete(to);
        }

        _log($"Año nuevo ({year}): copia del autoguardado guardada en la ranura {target + 1}.", Color.LightGreen);
        _state.LastYear = year;
        _state.NextIndex = target + 1 > last ? first : target + 1;
        _state.Save(StatePath);
    }

    private void ToTrash(string path)
    {
        if (!File.Exists(path)) return;
        var trash = Path.Combine(_stateDir, "papelera");
        File.Copy(path, Path.Combine(trash, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Path.GetFileName(path)}"));
        foreach (var old in Directory.GetFiles(trash).OrderByDescending(f => f).Skip(60))
            File.Delete(old);
    }

    private static byte[]? ReadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    ///     La cabecera guarda la fecha en Shift-JIS de ancho completo, p. ej. "１０１８年１２月４週".
    ///     Dígitos: 0x82 0x4F..0x58. 年 = 94 4E, 月 = 8C 8E, 週 = 8F 54.
    /// </summary>
    public static (int Year, int Month, int Week)? ParseDate(byte[] data)
    {
        var limit = Math.Min(data.Length - 1, 0x80);
        int? year = null, month = null, week = null;
        var number = 0;
        var digits = 0;

        for (var i = 0; i < limit; i++)
        {
            var hi = data[i];
            var lo = data[i + 1];
            if (hi == 0x82 && lo is >= 0x4F and <= 0x58)
            {
                number = number * 10 + (lo - 0x4F);
                digits++;
                i++;
                continue;
            }

            if (digits > 0)
            {
                if (hi == 0x94 && lo == 0x4E) year = number;
                else if (hi == 0x8C && lo == 0x8E) month = number;
                else if (hi == 0x8F && lo == 0x54) week = number;
            }

            number = 0;
            digits = 0;
        }

        return year != null && month != null && week != null ? (year.Value, month.Value, week.Value) : null;
    }

    private sealed class RotationState
    {
        public int LastYear { get; set; }
        public int NextIndex { get; set; }

        public static RotationState Load(string path)
        {
            try
            {
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<RotationState>(File.ReadAllText(path)) ?? new RotationState()
                    : new RotationState();
            }
            catch
            {
                return new RotationState();
            }
        }

        public void Save(string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
