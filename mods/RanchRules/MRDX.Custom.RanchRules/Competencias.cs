using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MRDX.Custom.RanchRules;

/// <summary>
///     Grabaciones de competencia: las ranuras 8 a 14 (archivos 07..13) se arman con los monstruos de
///     Competencias\competenciaNN.json (NN = número de ranura del juego, 08..14), puestos en el congelador.
///     Sirven para el modo VS: al elegir esa grabación, el juego registra los monstruos congelados.
///
///     Formato de la grabación (16 KB, sin comprimir): el congelador son 20 registros de 0x20C bytes desde 0x20C
///     (8 bytes de cabecera + el monstruo, igual que en memoria en exe+0x3768BC). Un lugar vacío tiene razas 0x2E/0x2E
///     y nombre 0xFF. El monstruo activo está en 0x3188 y no se toca.
///
///     Se arman después del primer guardado de la sesión, partiendo de una copia del autoguardado, y el propio juego
///     registra la huella (igual que la copia anual), así la ranura es válida. Solo se regeneran si cambió el JSON o
///     la ranura.
/// </summary>
public sealed partial class SaveManager
{
    public const int FirstCompetitionSlot = 8, LastCompetitionSlot = 14; // ranuras del juego (1..15)
    private const int RecordSize = 0x20C, FreezerStart = 0x20C, FreezerSlots = 20, SaveSize = 0x4000;

    private static readonly byte[] EmptyRecord = Convert.FromBase64String(
        "AAAAAAAAAAAuAAAALgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA//8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP8AAAD/////AAAAAAAAAAABAQEBAAAAAAD///8AAAAAAAAAAAAAAAAA/wAAAAAAAAAAAAAAALMAAAAAAAAAswCzALMAswC2ALYAAAAAAAAAAAAAAAAAAACDAwAAgwMAAJsDAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAHAAAAAAAAAAAACQAACQ3///////////////////////////////////8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAYGBgYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

    private string? _competitionDir;
    private bool _competitionsDone;
    private readonly System.Diagnostics.Stopwatch _sinceStart = new();

    /// <summary>Carpeta con los competenciaNN.json (la del mod). Sin ella no se arman competencias.</summary>
    public string? CompetitionDir
    {
        get => _competitionDir;
        init => _competitionDir = value;
    }

    private string CompetitionStatePath => Path.Combine(_stateDir, "competencias.json");

    /// <summary>Hay una grabación de competencia cargada y no se permite guardarla: no se guarda nada.</summary>
    public bool CompetitionBlocked => _loadedCompetition >= 0 && !_config().SaveCompetitions;

    /// <summary>Partida nueva o vuelta al título: ya no hay una grabación de competencia cargada.</summary>
    public void OnNewGame() => _loadedCompetition = -1;

    /// <summary>Archivo de competencia de una ranura del juego (8..14), o null si no hay.</summary>
    public string? CompetitionFile(int gameSlot)
    {
        if (_competitionDir == null) return null;
        var path = Path.Combine(_competitionDir, $"competencia{gameSlot:00}.json");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    ///     Desde OnFrame: una vez por sesión, después del primer guardado. Antes de eso (p. ej. en la pantalla de
    ///     título) el índice de partidas no está cargado, y pedirle al juego que lo reescriba lo deja casi vacío y
    ///     ninguna ranura carga (probado el 21/11/2026).
    /// </summary>
    private void CompetitionsOnFrame()
    {
        if (_competitionsDone || _indexObject == 0) return;
        if (!_sinceStart.IsRunning) _sinceStart.Restart();
        if (_sinceStart.ElapsedMilliseconds < 3000 || _lastWrite.ElapsedMilliseconds < 2000 || _pendingYear != 0) return;
        _competitionsDone = true;

        try
        {
            BuildCompetitions();
        }
        catch (Exception e)
        {
            _log($"Error armando las grabaciones de competencia: {e.Message}", Color.Red);
        }
    }

    /// <summary>
    ///     Guardado permitido en una grabación de competencia: la ranura nueva pasa a ser la "armada" (se anota su huella),
    ///     así en la sesión siguiente no se regenera desde el autoguardado. Se rearma solo si cambia su JSON.
    /// </summary>
    private void AdoptCompetitionSave(int fileIndex)
    {
        var json = CompetitionFile(fileIndex + 1);
        var dest = SlotFile(fileIndex);
        if (json == null || !File.Exists(dest)) return;
        var state = CompetitionState.Load(CompetitionStatePath);
        state.Slots[(fileIndex + 1).ToString("00")] = new CompetitionDone
        {
            Json = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(json))),
            Save = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dest)))
        };
        state.Save(CompetitionStatePath);
        _log($"Ranura {fileIndex + 1}: grabación de competencia guardada; queda así (no se rearma salvo que cambie su JSON).",
            Color.LightGreen);
    }

    private void BuildCompetitions()
    {
        if (_competitionDir == null || _hookRegister == null || _hookWriteIndex == null) return;
        var state = CompetitionState.Load(CompetitionStatePath);
        var index = _indexObject; // el que usó el juego en su último guardado
        var changed = false;

        for (var gameSlot = FirstCompetitionSlot; gameSlot <= LastCompetitionSlot; gameSlot++)
        {
            var json = CompetitionFile(gameSlot);
            if (json == null) continue;

            var fileIndex = gameSlot - 1;
            // La ranura que se está jugando no se toca: se regenera en una sesión siguiente (ya cambió su huella).
            if (fileIndex == _loadedCompetition) continue;
            var dest = SlotFile(fileIndex);
            var jsonHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(json)));
            var key = gameSlot.ToString("00");
            if (state.Slots.TryGetValue(key, out var done) && done.Json == jsonHash && File.Exists(dest) &&
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dest))) == done.Save)
                continue;

            var comp = JsonSerializer.Deserialize<Competition>(File.ReadAllText(json), JsonOptions)
                       ?? throw new InvalidDataException($"{Path.GetFileName(json)} está vacío");
            var data = BuildCompetitionSave(comp, Path.GetFileName(json));
            if (data == null) continue;

            ToTrash(dest);
            File.WriteAllBytes(dest, data);
            if ((_hookRegister.OriginalFunction(index, fileIndex) & 0xFF) == 0)
            {
                _log($"El juego no aceptó la grabación de competencia de la ranura {gameSlot}.", Color.Red);
                continue;
            }

            changed = true;
            state.Slots[key] = new CompetitionDone { Json = jsonHash, Save = Convert.ToHexString(SHA256.HashData(data)) };
            _log($"Ranura {gameSlot}: competencia \"{comp.Nombre}\" armada con {Math.Min(comp.Monstruos.Count, FreezerSlots)} monstruos.",
                Color.LightGreen);
        }

        if (!changed) return;
        _hookWriteIndex.OriginalFunction(index);
        state.Save(CompetitionStatePath);
    }

    /// <summary>Copia del autoguardado con el congelador reemplazado por los monstruos de la competencia.</summary>
    private byte[]? BuildCompetitionSave(Competition comp, string name)
    {
        var baseFile = ReadOrNull(SlotFile(AutosaveIndex));
        if (baseFile == null || baseFile.Length != SaveSize)
        {
            _log($"{name}: no hay autoguardado para usar de base (hace falta haber guardado una partida).", Color.Orange);
            return null;
        }

        if (comp.Monstruos.Count > FreezerSlots)
            _log($"{name}: tiene {comp.Monstruos.Count} monstruos; el congelador tiene lugar para {FreezerSlots}.", Color.Orange);

        var data = (byte[])baseFile.Clone();
        for (var k = 0; k < FreezerSlots; k++)
        {
            var record = k < comp.Monstruos.Count ? Record(comp.Monstruos[k], name) : EmptyRecord;
            Buffer.BlockCopy(record, 0, data, FreezerStart + k * RecordSize, RecordSize);
        }

        // En el archivo, los primeros 4 bytes del primer lugar (0x20C..0x20F) no son del monstruo sino del final del
        // ícono de la grabación (en memoria valen 0): se dejan como estaban en la base.
        Buffer.BlockCopy(baseFile, FreezerStart, data, FreezerStart, 4);

        WriteTrainer(data, string.IsNullOrWhiteSpace(comp.Entrenador) ? DefaultTrainer : comp.Entrenador);
        WriteChecksum(data);
        return data;
    }

    public const string DefaultTrainer = "Kokemon";
    private const int TrainerOffset = 0x2B06, TrainerBytes = 26, TrainerMax = 8;

    /// <summary>
    ///     Nombre del entrenador: en los datos (0x2B06, como los nombres de monstruo: B0 + código; dígitos en un byte
    ///     A2..AB; fin 0xFF) y en el título de la grabación (Shift-JIS de ancho completo, después de "週"), para que
    ///     las grabaciones de competencia se distingan de las del jugador.
    /// </summary>
    private static void WriteTrainer(byte[] data, string name)
    {
        var chars = name.Where(char.IsAsciiLetterOrDigit).Take(TrainerMax).ToArray();
        if (chars.Length == 0) return;

        var field = Enumerable.Repeat((byte)0xFF, TrainerBytes).ToArray();
        var at = 0;
        foreach (var c in chars)
        {
            if (char.IsAsciiDigit(c)) { field[at++] = (byte)(0xA2 + c - '0'); continue; }
            field[at++] = 0xB0;
            field[at++] = (byte)(char.IsAsciiLetterUpper(c) ? c - 'A' : 0x1A + c - 'a');
        }

        field.CopyTo(data, TrainerOffset);

        // Título: después de "週" (8F 54) y hasta el final del título (0x44), en ancho completo.
        var start = -1;
        for (var i = 4; i < 0x43; i++)
            if (data[i] == 0x8F && data[i + 1] == 0x54) { start = i + 2; break; }
        if (start < 0) return;
        var pos = start;
        foreach (var c in chars)
        {
            if (pos + 1 >= 0x44) break;
            var code = char.IsAsciiDigit(c) ? 0x824F + (c - '0') : char.IsAsciiLetterUpper(c) ? 0x8260 + (c - 'A') : 0x8281 + (c - 'a');
            data[pos++] = (byte)(code >> 8);
            data[pos++] = (byte)code;
        }

        for (; pos + 1 < 0x44; pos += 2)
        {
            data[pos] = 0x81;
            data[pos + 1] = 0x40;
        }
    }

    /// <summary>
    ///     Suma de control de la grabación: suma de todos los bytes (con signo) de 0 a 0x3FFC, guardada como int32 en
    ///     0x3FFC. Comprobado con las 14 grabaciones del jugador (21/11/2026); sin esto el juego no carga la ranura.
    /// </summary>
    private const int ChecksumOffset = 0x3FFC;

    public static void WriteChecksum(byte[] data)
    {
        var sum = 0;
        for (var i = 0; i < ChecksumOffset; i++) sum += (sbyte)data[i];
        BitConverter.GetBytes(sum).CopyTo(data, ChecksumOffset);
    }

    public static bool ChecksumOk(byte[] data)
    {
        var sum = 0;
        for (var i = 0; i < ChecksumOffset; i++) sum += (sbyte)data[i];
        return BitConverter.ToInt32(data, ChecksumOffset) == sum;
    }

    /// <summary>El monstruo en el formato del congelador (0x20C bytes; si viene sin cabecera, 0x204, se le agrega).</summary>
    private static byte[] Record(CompetitionMonster m, string file)
    {
        var raw = Convert.FromBase64String(m.Crudo ?? "");
        if (raw.Length == RecordSize) return raw;
        if (raw.Length == RecordSize - 8)
        {
            var rec = new byte[RecordSize];
            Buffer.BlockCopy(raw, 0, rec, 8, raw.Length);
            return rec;
        }

        throw new InvalidDataException($"{file}: \"{m.Nombre}\" tiene datos de {raw.Length} bytes (se esperaban {RecordSize}).");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed class Competition
    {
        public string Nombre { get; set; } = "";
        public string? Entrenador { get; set; }
        public List<CompetitionMonster> Monstruos { get; set; } = new();
    }

    private sealed class CompetitionMonster
    {
        public string Nombre { get; set; } = "";
        [JsonPropertyName("crudo")] public string? Crudo { get; set; }
    }

    private sealed class CompetitionDone
    {
        public string Json { get; set; } = "";
        public string Save { get; set; } = "";
    }

    private sealed class CompetitionState
    {
        public Dictionary<string, CompetitionDone> Slots { get; set; } = new();

        public static CompetitionState Load(string path)
        {
            try
            {
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<CompetitionState>(File.ReadAllText(path)) ?? new CompetitionState()
                    : new CompetitionState();
            }
            catch
            {
                return new CompetitionState();
            }
        }

        public void Save(string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
