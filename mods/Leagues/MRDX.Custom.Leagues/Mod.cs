using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using MRDX.Base.Mod.Interfaces;
using MRDX.Custom.Leagues.Configuration;
using MRDX.Custom.Leagues.Template;
using Reloaded.Mod.Interfaces;
using Reloaded.Universal.Redirector.Interfaces;

namespace MRDX.Custom.Leagues;

/// <summary>
///     Tournament leagues: each week, the rivals of the league tournaments (E to S) are drawn from the list of monsters
///     for each league. The game reads them from mf2\data\taikai\taikai_en.flk; a modified copy is generated and the
///     Reloaded redirector serves it in place of the original.
/// </summary>
public class Mod : ModBase
{
    private const string FileName = "taikai_en.flk";
    private const int RecordsOffset = 0xA8C; // record 0 (dummy); record i is at RecordsOffset + i * 60
    private const int RecordSize = 60;
    private const int OriginalSize = 78778;
    private const int MonsterOffset = 0x37667C; // current monster (from the start of the .exe), as in More Monsters
    private const int LearnedTechsOffset = 0x192; // 24 positions x 2 bytes, relative to MonsterOffset
    private const string MoreMonstersId = "MRDX.Game.MoreMonsters";

    // League tournament slots (R_01..R_50 in the original game table). The other slots (4 majors, FIMBA,
    // Hero/Heel/Silver/Phoenix cups, invitations and final battles) are not touched.
    private static readonly Dictionary<string, (int First, int Count)> LeagueSlots = new()
    {
        ["S"] = (1, 8), ["A"] = (9, 8), ["B"] = (17, 10), ["C"] = (27, 10), ["D"] = (37, 8), ["E"] = (45, 6)
    };

    private readonly ILogger _logger = null!;
    private readonly IModConfig _modConfig = null!;
    private Config _config = null!;
    private readonly IGame? _game;
    private readonly IRedirectorController? _redirector;
    private readonly nuint _exeBase;

    private readonly string _poolDir = "";     // league list shipped with the mod (Mods\...\Liga)
    private readonly string _userPoolDir = ""; // personal list (User\Mods\...\Liga)
    private readonly string _exportDir = "";
    private readonly string _generated = "";
    private readonly byte[]? _original;
    private readonly HashSet<string> _sources = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<(int, int)>? _mmBreeds;
    private readonly IModLoader? _loader;

    private long _drawnWeek = long.MinValue;

    public Mod(ModContext context)
    {
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        _config = context.Configuration;
        _loader = context.ModLoader;
        _exeBase = (nuint)Process.GetCurrentProcess().MainModule!.BaseAddress;

        var configDir = _loader.GetModConfigDirectory(_modConfig.ModId);
        _poolDir = Path.Combine(_loader.GetDirectoryForModId(_modConfig.ModId), "Liga");
        _userPoolDir = Path.Combine(configDir, "Liga");
        _exportDir = Path.Combine(configDir, "Exportados");
        _generated = Path.Combine(configDir, "generado", FileName);

        _loader.GetController<IGame>()?.TryGetTarget(out _game);
        ISaveFile? save = null;
        _loader.GetController<ISaveFile>()?.TryGetTarget(out save);
        _loader.GetController<IRedirectorController>()?.TryGetTarget(out _redirector);

        if (_game == null || save == null)
        {
            Log("No se encontró MRDX.Base.Mod. ¿Está activado y por encima de este mod?", Color.Red);
            return;
        }

        try
        {
            var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!;
            var source = Path.GetFullPath(Path.Combine(exeDir, "Resources", "data", "mf2", "data", "taikai", FileName));
            var data = File.ReadAllBytes(source);
            if (data.Length != OriginalSize)
                throw new InvalidDataException($"tamaño {data.Length}, se esperaba {OriginalSize}");
            _original = data;
            _sources.Add(source);
            // The game tries Resources\mf2\... first and, if it does not exist, Resources\data\mf2\...
            _sources.Add(Path.GetFullPath(Path.Combine(exeDir, "Resources", "mf2", "data", "taikai", FileName)));
            CheckRoundTrip();
            ExportOriginals();
        }
        catch (Exception e)
        {
            _original = null;
            Log($"No se pudo leer el archivo de torneos original: {e.Message}. Las ligas quedan desactivadas.", Color.Red);
        }

        if (_redirector == null)
            Log("Sin el redirector de Reloaded no se pueden cambiar los rivales.", Color.Red);
        else
            _redirector.Loading += OnLoading;

        _game.OnWeekChange += c =>
        {
            if (c.NewWeek == uint.MaxValue) return; // no date yet (screen transitions)
            Draw(c.NewWeek);
        };
        save.OnSave += OnSave;
        save.OnLoad += OnLoad;

        Log($"Cargado. Semilla {_config.Seed}.", Color.LightGreen);
    }

    public Mod()
    {
    }

    public override void ConfigurationUpdated(Config configuration)
    {
        _config = configuration;
        var week = _drawnWeek;
        _drawnWeek = long.MinValue;
        if (week != long.MinValue) Draw(week);
        Log("Configuración actualizada.");
    }

    // ------------------------------------------------------------------
    // Weekly draw
    // ------------------------------------------------------------------

    private void Draw(long week)
    {
        if (_original == null || _redirector == null) return;
        // Same week: nothing to do, unless "Liga Kokemon" changed its tables (a new month can change the tournaments).
        var ligaStamp = LigaTablesStamp();
        if (week == _drawnWeek && ligaStamp == _drawnLigaStamp) return;
        _drawnWeek = week;
        _drawnLigaStamp = ligaStamp;

        try
        {
            // Tournament tables from "Liga Kokemon" (new tournaments), if it is active.
            var (data, ligaTables) = WithLigaKokemonTables(_original);

            if (!_config.Enabled)
            {
                if (!ligaTables) SetRedirect(false);
                else ServeGenerated(data);
                return;
            }

            var pool = LoadPool();
            var summary = new List<string>();

            foreach (var (league, (first, count)) in LeagueSlots)
            {
                var slots = Enumerable.Range(first, count).ToList();
                var originals = slots.ToDictionary(s => s, s => LeagueMonster.FromRecord(Record(_original, s)));

                var candidates = pool.Where(m => m.League == league)
                    .OrderBy(m => m.File, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Shared draw: depends only on the seed, the week and the league.
                var rng = new Random(Hash(_config.Seed, week, league));
                for (var i = candidates.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                }

                var picked = candidates.Take(count).ToList();
                // If the league has fewer monsters than slots, the remaining slots keep their original rival.
                foreach (var s in slots)
                {
                    if (picked.Count >= count) break;
                    if (!picked.Any(p => SameMonster(p, originals[s]))) picked.Add(originals[s]);
                }

                // The strongest goes in the slot that originally had the strongest rival: tournaments that use
                // the weak slots of a league stay easier, as in the original.
                var bySlotStrength = slots.OrderByDescending(s => originals[s].StatTotal).ToList();
                var byPickStrength = picked.OrderByDescending(p => p.StatTotal).ToList();
                for (var i = 0; i < count; i++)
                    byPickStrength[i].ToRecord().CopyTo(data, RecordOffset(data, bySlotStrength[i]));

                summary.Add($"{league}: {string.Join(", ", byPickStrength.Select(p => p.Name))}");
            }

            if (ligaTables) ApplyFixedParticipants(data, pool, summary);

            ServeGenerated(data);

            Log($"Rivales de la semana {week} ({pool.Count} monstruos en las ligas).");
            if (_config.VerboseLog)
                foreach (var line in summary) Log("  " + line);
        }
        catch (Exception e)
        {
            Log($"No se pudieron sortear los rivales: {e.Message}", Color.Red);
        }
    }

    private void ServeGenerated(byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_generated)!);
        var tmp = _generated + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Copy(tmp, _generated, true);
        File.Delete(tmp);
        SetRedirect(true);
    }

    // taikai_en.flk is a container: u32 count (6), u32 0, then 6 section offsets (u32). Sections:
    // 0 = tournaments (btl_form.obj), 1 = prizes (btl_gold.obj), 2 = brackets, 3 = random draw,
    // 4 = rivals (table of offsets u32, relative to the section, + 60-byte records), 5 = names.
    private const int SectionCount = 6, SectionTable = 8, RivalsSection = 4;
    private const string LigaKokemonId = "MRDX.Custom.LigaKokemon";
    private const int MsgBlock = 0x4000, NextBlock = 0x8000; // fixed blocks of taikai_en.flk after the sections

    private static int SectionOffset(byte[] data, int s) => BitConverter.ToInt32(data, SectionTable + s * 4);

    /// <summary>Position of rival record "slot" (as in the original, the dummy at slot 0 starts at 0xA8C).</summary>
    private static int RecordOffset(byte[] data, int slot)
    {
        var sec = SectionOffset(data, RivalsSection);
        return sec + BitConverter.ToInt32(data, sec + slot * 4);
    }

    /// <summary>
    ///     If "Liga Kokemon" is active and generated its tables, rebuilds the container with them (the tournament
    ///     section can change size, so the offsets of the following sections are recomputed).
    /// </summary>
    private (byte[] Data, bool Changed) WithLigaKokemonTables(byte[] original)
    {
        try
        {
            if (_loader == null || !_loader.GetActiveMods().Any(m => m.Generic.ModId == LigaKokemonId)) return ((byte[])original.Clone(), false);
            var dir = Path.Combine(_loader.GetModConfigDirectory(LigaKokemonId), "generado");
            var replace = new Dictionary<int, string> { [0] = "btl_form.obj", [1] = "btl_gold.obj" };
            var parts = new byte[SectionCount][];
            var changed = false;
            // After the 6 sections, the file has blocks at fixed positions: the battle messages (msg_btl, with the
            // tournament names the announcer says) at 0x4000 and more data at 0x8000. The sections can change size,
            // so they are rebuilt and then the blocks are put back at their positions.
            var itemsEnd = MsgBlock;
            while (itemsEnd > 0 && original[itemsEnd - 1] == 0) itemsEnd--;
            for (var s = 0; s < SectionCount; s++)
            {
                var start = SectionOffset(original, s);
                var end = s + 1 < SectionCount ? SectionOffset(original, s + 1) : itemsEnd;
                parts[s] = original[start..end];
                if (replace.TryGetValue(s, out var name) && File.Exists(Path.Combine(dir, name)))
                {
                    parts[s] = File.ReadAllBytes(Path.Combine(dir, name));
                    changed = true;
                }
            }

            // Battle messages of Liga Kokemon (names of the new tournaments for the announcer).
            var msgPath = Path.Combine(dir, "msg_btl_en.obj");
            var msg = original[MsgBlock..NextBlock];
            if (File.Exists(msgPath))
            {
                var m = File.ReadAllBytes(msgPath);
                if (m.Length <= NextBlock - MsgBlock)
                {
                    msg = m.Concat(new byte[NextBlock - MsgBlock - m.Length]).ToArray();
                    changed = true;
                }
            }

            if (!changed) return ((byte[])original.Clone(), false);

            var o = new List<byte>(original[..SectionOffset(original, 0)]);
            for (var s = 0; s < SectionCount; s++)
            {
                while (o.Count % 4 != 0 && s > 0) o.Add(0);
                var at = o.Count;
                var b = BitConverter.GetBytes(at);
                for (var k = 0; k < 4; k++) o[SectionTable + s * 4 + k] = b[k];
                o.AddRange(parts[s]);
            }

            if (o.Count > MsgBlock) throw new InvalidDataException("las tablas de torneos no entran antes de los mensajes");
            o.AddRange(new byte[MsgBlock - o.Count]);
            o.AddRange(msg);
            o.AddRange(original[NextBlock..]);

            if (_config.VerboseLog && _drawnWeekLoggedLiga++ == 0)
                Log("Torneos de Liga Kokemon incluidos en el archivo de torneos.");
            return (o.ToArray(), true);
        }
        catch (Exception e)
        {
            Log($"No se pudieron incluir los torneos de Liga Kokemon: {e.Message}", Color.Orange);
            return ((byte[])original.Clone(), false);
        }
    }

    private int _drawnWeekLoggedLiga;
    private DateTime _drawnLigaStamp;

    private DateTime LigaTablesStamp()
    {
        try
        {
            if (_loader == null) return default;
            var dir = Path.Combine(_loader.GetModConfigDirectory(LigaKokemonId), "generado");
            return new[] { "btl_form.obj", "btl_gold.obj", "participantes.json", "msg_btl_en.obj" }
                .Select(n => Path.Combine(dir, n)).Where(File.Exists).Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty().Max();
        }
        catch
        {
            return default;
        }
    }

    private const int WeekCounterOffset = 0x379444; // week counter (from the start of the .exe)

    private unsafe long? CurrentWeek()
    {
        try
        {
            var w = *(uint*)(_exeBase + WeekCounterOffset);
            return w == uint.MaxValue ? null : w;
        }
        catch
        {
            return null;
        }
    }

    private static bool SameMonster(LeagueMonster a, LeagueMonster b) =>
        a.Name == b.Name && a.GenusMain == b.GenusMain && a.GenusSub == b.GenusSub && a.StatTotal == b.StatTotal;

    private static int Hash(int seed, long week, string league)
    {
        unchecked
        {
            ulong x = (ulong)(uint)seed << 32 ^ (ulong)week * 0x9E3779B97F4A7C15UL ^ (ulong)league[0] * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return (int)((x ^ (x >> 31)) & 0x7FFFFFFF);
        }
    }

    private static byte[] Record(byte[] data, int slot) =>
        data[(RecordsOffset + slot * RecordSize)..(RecordsOffset + (slot + 1) * RecordSize)];

    private void SetRedirect(bool on)
    {
        foreach (var source in _sources) Apply(source, on);
    }

    private void Apply(string source, bool on)
    {
        _redirector!.RemoveRedirect(source);
        if (on) _redirector.AddRedirect(source, _generated);
    }

    // In case the game opens the file through another form of the path; also used to see when it reads it.
    private void OnLoading(string path)
    {
        if (!path.EndsWith(FileName, StringComparison.OrdinalIgnoreCase)) return;
        if (path.StartsWith(Path.GetDirectoryName(_generated)!, StringComparison.OrdinalIgnoreCase)) return; // the mod's own writes
        if (_config.VerboseLog) Log($"El juego lee {path}");
        // The game reads the file in the same frame the week changes, before the base mod reports it:
        // the week is read directly from memory so the game gets the draw of the week it is entering.
        var week = CurrentWeek();
        if (week != null) Draw(week.Value);
        if (path.StartsWith(Path.GetDirectoryName(_generated)!, StringComparison.OrdinalIgnoreCase)) return;
        if (_sources.Add(path) && _drawnWeek != long.MinValue && _config.Enabled) Apply(path, true);
    }

    // ------------------------------------------------------------------
    // League list
    // ------------------------------------------------------------------

    private sealed class FixedParticipant
    {
        [JsonPropertyName("hueco")] public int Slot { get; set; }
        [JsonPropertyName("liga")] public string League { get; set; } = "";
        [JsonPropertyName("puesto")] public int Place { get; set; }
        [JsonPropertyName("nombre")] public string Name { get; set; } = "";
    }

    /// <summary>
    ///     Fixed participants of the "Liga Kokemon" tournaments (generado\participantes.json): monsters of the league
    ///     list, by name or file name, or the Nth strongest of a league. They go in rival slots no original
    ///     tournament uses.
    /// </summary>
    private void ApplyFixedParticipants(byte[] data, List<LeagueMonster> pool, List<string> summary)
    {
        if (_loader == null) return;
        var file = Path.Combine(_loader.GetModConfigDirectory(LigaKokemonId), "generado", "participantes.json");
        if (!File.Exists(file)) return;
        var entries = JsonSerializer.Deserialize<List<FixedParticipant>>(File.ReadAllText(file)) ?? [];
        static string Key(string s) => Path.GetFileNameWithoutExtension(s.Trim()).ToLowerInvariant();
        var placed = new List<string>();
        foreach (var e in entries)
        {
            if (e.Slot is < 116 or > 135) continue; // only the free slots
            LeagueMonster? m;
            // Place > 0: the Nth strongest of the league; < 0: the Nth weakest (recomputed every week from the list).
            if (e.Place != 0)
            {
                var league = pool.Where(p => p.League == e.League.ToUpperInvariant());
                var ordered = e.Place > 0
                    ? league.OrderByDescending(p => p.StatTotal).ThenBy(p => p.File, StringComparer.OrdinalIgnoreCase)
                    : league.OrderBy(p => p.StatTotal).ThenBy(p => p.File, StringComparer.OrdinalIgnoreCase);
                m = ordered.Skip(Math.Abs(e.Place) - 1).FirstOrDefault();
            }
            else
                m = pool.FirstOrDefault(p => Key(p.File) == Key(e.Name) || Key(Path.GetFileName(p.File)) == Key(e.Name))
                    ?? pool.FirstOrDefault(p => p.Name.Equals(e.Name.Trim(), StringComparison.OrdinalIgnoreCase));

            var what = e.Place > 0 ? $"mejor {e.Place} de {e.League}" : e.Place < 0 ? $"peor {-e.Place} de {e.League}" : $"\"{e.Name}\"";
            if (m == null)
            {
                Log($"Participante fijo {what}: no está en la lista de rivales.", Color.Orange);
                continue;
            }

            m.ToRecord().CopyTo(data, RecordOffset(data, e.Slot));
            placed.Add($"{what} = {m.Name}");
        }

        if (placed.Count > 0) summary.Add("Fijos: " + string.Join(", ", placed));
    }

    private List<LeagueMonster> LoadPool()
    {
        var pool = new List<LeagueMonster>();
        foreach (var dir in new[] { _poolDir, _userPoolDir })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var m = JsonSerializer.Deserialize<LeagueMonster>(File.ReadAllText(file), LeagueMonster.Json);
                    if (m == null || !m.Active) continue;
                    m.File = Path.GetRelativePath(dir, file);
                    m.League = (m.League ?? "").Trim().ToUpperInvariant();
                    // No league: it is placed by stat total (for monsters created by hand).
                    if (m.League.Length == 0) m.League = LeagueMonster.LeagueForTotal(m.StatTotal);
                    if (!LeagueSlots.ContainsKey(m.League))
                    {
                        Log($"{m.File}: liga \"{m.League}\" desconocida (tiene que ser S, A, B, C, D o E). Se saltea.", Color.Orange);
                        continue;
                    }

                    if (!_config.IncludeMoreMonsters && IsMoreMonsters(m))
                    {
                        if (_config.VerboseLog) Log($"{m.File}: especie de More Monsters, se saltea (opción desactivada).");
                        continue;
                    }

                    m.ToRecord(); // validates it can be written
                    pool.Add(m);
                }
                catch (Exception e)
                {
                    Log($"No se pudo leer {Path.GetFileName(file)}: {e.Message}", Color.Orange);
                }
            }
        }

        return pool;
    }

    private bool IsMoreMonsters(LeagueMonster m)
    {
        // Only the species in the More Monsters tables: values 38-43 are also the base game's unique variants
        // (the original tournaments use them, e.g. Dragon/38), and those are valid rivals.
        _mmBreeds ??= ReadMoreMonstersBreeds();
        return _mmBreeds.Contains(((int)m.GenusMain, (int)m.GenusSub));
    }

    private HashSet<(int, int)> ReadMoreMonstersBreeds()
    {
        var set = new HashSet<(int, int)>();
        try
        {
            var dir = Path.Combine(_loader!.GetDirectoryForModId(MoreMonstersId), "NewMonsterData");
            foreach (var file in Directory.Exists(dir) ? Directory.GetFiles(dir, "*.csv") : [])
            foreach (var line in File.ReadAllLines(file))
            {
                var c = line.Split('\t');
                if (c.Length >= 5 && int.TryParse(c[3], out var main) && int.TryParse(c[4], out var sub))
                    set.Add((main, sub));
            }
        }
        catch
        {
            // without More Monsters there are no extra species
        }

        return set;
    }

    /// <summary>The first time, the 50 original league rivals are written to Liga\originales.</summary>
    private void ExportOriginals()
    {
        var dir = Path.Combine(_poolDir, "originales");
        if (Directory.Exists(dir)) return;
        Directory.CreateDirectory(dir);
        foreach (var (league, (first, count)) in LeagueSlots)
        foreach (var slot in Enumerable.Range(first, count))
        {
            var m = LeagueMonster.FromRecord(Record(_original!, slot));
            m.League = league;
            m.Origin = "original";
            File.WriteAllText(Path.Combine(dir, $"{league}_{slot:00}_{FileSafe(m.Name)}.json"),
                JsonSerializer.Serialize(m, LeagueMonster.Json));
        }

        Log($"Se crearon los 50 rivales originales en {dir}.");
    }

    /// <summary>Check that reading and rewriting the original records yields the same bytes.</summary>
    private void CheckRoundTrip()
    {
        var bad = new List<int>();
        foreach (var (_, (first, count)) in LeagueSlots)
        foreach (var slot in Enumerable.Range(first, count))
        {
            var r = Record(_original!, slot);
            var back = LeagueMonster.FromRecord(r).ToRecord();
            // The name may carry leftover bytes after its end: only the data after the name is compared.
            if (!r.AsSpan(26).SequenceEqual(back.AsSpan(26))) bad.Add(slot);
        }

        if (bad.Count > 0)
            Log($"Aviso: {bad.Count} rivales originales no se reescriben idénticos (huecos {string.Join(",", bad)}).", Color.Orange);
        else if (_config.VerboseLog)
            Log("Formato de rivales verificado con los 50 originales.");
    }

    // ------------------------------------------------------------------
    // Export the player's monster
    // ------------------------------------------------------------------

    // Monster layout (offsets relative to the start of the struct, as at exe+0x37667C):
    private const int OffGenusMain = 0x00, OffGenusSub = 0x04;
    private const int OffLife = 0x08, OffPower = 0x0A, OffDefense = 0x0C, OffSkill = 0x0E, OffSpeed = 0x10, OffIntelligence = 0x12;
    private const int OffNatureRaw = 0x1C, OffSpoil = 0x24, OffFear = 0x25, OffNatureBase = 0x41;
    private const int NameOffset = 0x168; // 12 characters x 2 bytes
    private const int OffRank = 0x18A;    // 0 = E ... 4 = A, 5 = S
    private const int OffArenaSpeed = 0x1CE, OffGutsRate = 0x1CF, OffBattleSpecials = 0x1D0;
    // Freezer: 20 slots of 524 bytes; the monster starts 8 bytes in (its name at +0x170). Empty: name = 0xFF.
    private const int FreezerOffset = 0x3768BC, FreezerSlots = 20, FreezerSlotSize = 524, FreezerMonsterStart = 8;

    // Año de referencia para exportar: el de la partida cargada. Se exporta solo en el primer guardado de un año nuevo
    // (enero), igual que la copia anual de "Reglas del rancho": un exportado por año, la foto de enero.
    private int _exportYear;

    private void OnLoad(ISaveFileEntry file) => _exportYear = YearOf(SaveDate(file.Filename));

    private static int YearOf(string date) => int.TryParse(date.Split('-')[0], out var y) ? y : 0;

    private void OnSave(ISaveFileEntry file)
    {
        if (!_config.ExportOnSave) return;
        var date = SaveDate(file.Filename);
        var year = YearOf(date);
        if (_exportYear == 0 || year == 0 || year <= _exportYear)
        {
            // Primer guardado sin una partida cargada (partida nueva) o mismo año: no se exporta.
            if (_exportYear == 0) _exportYear = year;
            if (_config.VerboseLog) Log($"Exportación: se hace al empezar cada año (próxima: enero de {_exportYear + 1}).");
            return;
        }

        _exportYear = year;
        var exported = new List<LeagueMonster>();
        var trainer = SaveTrainer(file.Filename);
        Export(_exeBase + MonsterOffset, "rancho", exported);
        for (var i = 0; i < FreezerSlots; i++)
            Export(_exeBase + FreezerOffset + (nuint)(i * FreezerSlotSize + FreezerMonsterStart), $"congelador {i + 1}", exported);
        try
        {
            var path = ExportFile(new ExportedSave { Trainer = trainer, Date = date, Monsters = exported });
            if (path != null)
                Log($"Exportados {exported.Count} monstruos a {path}: {string.Join(", ", exported.Select(m => m.Name))}");
            else if (_config.VerboseLog)
                Log("Exportación: los monstruos no cambiaron desde el último archivo, no se repite.");
        }
        catch (Exception e)
        {
            Log($"No se pudo exportar: {e.Message}", Color.Orange);
        }

        if (UnknownChars.Count > 0)
            Log($"Nombres con caracteres desconocidos (quedaron como ?): códigos {string.Join(" ", UnknownChars)}. Se puede corregir el nombre en el archivo exportado.", Color.Orange);
    }

    private unsafe void Export(nuint s, string where, List<LeagueMonster> exported)
    {
        try
        {
            var p = (byte*)s;
            if (p[NameOffset] == 0xFF) return; // empty slot
            var main = *(int*)(p + OffGenusMain);
            var sub = *(int*)(p + OffGenusSub);
            var life = *(ushort*)(p + OffLife);
            if (main is < 0 or > 45 || sub is < 0 or > 45 || life == 0) return;

            // El monstruo completo: el registro de 0x20C bytes empieza 8 bytes antes (igual que en el congelador).
            var raw = new ReadOnlySpan<byte>(p - FreezerMonsterStart, FreezerSlotSize).ToArray();

            var techs = new List<int>();
            for (var i = 0; i < 24; i++)
                if (p[LearnedTechsOffset + i * 2] != 0) techs.Add(i);

            var rank = p[OffRank];
            var m = new LeagueMonster
            {
                Name = ReadName(p + NameOffset) ?? "Rival",
                Origin = $"exportado ({where})",
                // Rank reached in the game; if unknown, the league is decided by stat total when loading it.
                League = rank < LeagueMonster.Leagues.Length ? LeagueMonster.Leagues[^(rank + 1)] : (rank >= 5 ? "S" : ""),
                GenusMain = (MonsterGenus)main, GenusSub = (MonsterGenus)sub,
                Life = life,
                Power = *(ushort*)(p + OffPower), Intelligence = *(ushort*)(p + OffIntelligence),
                Skill = *(ushort*)(p + OffSkill), Speed = *(ushort*)(p + OffSpeed), Defense = *(ushort*)(p + OffDefense),
                Nature = (sbyte)Math.Clamp(*(sbyte*)(p + OffNatureBase) + MonsterHelper.NatureRawToMod(*(short*)(p + OffNatureRaw)), -100, 100),
                Fear = p[OffFear], Spoil = p[OffSpoil],
                Techniques = techs,
                ArenaSpeed = p[OffArenaSpeed], GutsRate = p[OffGutsRate],
                BattleSpecials = *(ushort*)(p + OffBattleSpecials),
                Place = where,
                Raw = Convert.ToBase64String(raw)
            };

            exported.Add(m);
            if (_config.VerboseLog)
                Log($"  {m.Label}, rango {rank} → liga {m.League}, naturaleza {m.Nature}, técnicas [{string.Join(",", techs)}]");
        }
        catch (Exception e)
        {
            Log($"No se pudo exportar el monstruo ({where}): {e.Message}", Color.Orange);
        }
    }

    // ------------------------------------------------------------------
    // Exportación: un archivo por grabación, Exportados\"año-mes-semana Entrenador.json", con todos sus monstruos
    // (rancho y congelador): datos de batalla legibles y el monstruo completo ("crudo"). Si el conjunto de monstruos
    // es igual al de un archivo anterior, no se repite.
    // ------------------------------------------------------------------

    private HashSet<string>? _exportedSets; // huella de los monstruos de cada archivo ya exportado

    private static string SetKey(IEnumerable<LeagueMonster> monsters) =>
        string.Join("|", monsters.Select(m => m.Raw).Order(StringComparer.Ordinal));

    /// <summary>Escribe el archivo de la grabación. Devuelve su ruta, o null si ya había uno igual.</summary>
    private string? ExportFile(ExportedSave save)
    {
        if (save.Monsters.Count == 0) return null;
        Directory.CreateDirectory(_exportDir);
        if (_exportedSets == null)
        {
            _exportedSets = [];
            foreach (var f in Directory.GetFiles(_exportDir, "*.json"))
            {
                try
                {
                    var old = JsonSerializer.Deserialize<ExportedSave>(File.ReadAllText(f), LeagueMonster.Json);
                    if (old?.Monsters is { Count: > 0 }) _exportedSets.Add(SetKey(old.Monsters));
                }
                catch
                {
                    // exportados del formato anterior (un monstruo por archivo) u otros: se ignoran
                }
            }
        }

        var key = SetKey(save.Monsters);
        if (!_exportedSets.Add(key)) return null;

        var baseName = $"{save.Date} {FileSafe(save.Trainer ?? "Entrenador")}";
        var path = Path.Combine(_exportDir, baseName + ".json");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(_exportDir, $"{baseName} ({n}).json");
        File.WriteAllText(path, JsonSerializer.Serialize(save, LeagueMonster.Json));
        return path;
    }

    /// <summary>Nombre del entrenador de la grabación (0x2B06: como los nombres de monstruo, B0 + código, fin 0xFF).</summary>
    private static string? SaveTrainer(string? saveFile)
    {
        try
        {
            if (saveFile == null || !File.Exists(saveFile)) return null;
            var d = File.ReadAllBytes(saveFile);
            if (d.Length < 0x2B06 + 26) return null;
            var sb = new System.Text.StringBuilder();
            for (var i = 0x2B06; i < 0x2B06 + 24 && d[i] != 0xFF;)
            {
                if (d[i] is >= 0xA2 and <= 0xAB) { sb.Append((char)('0' + d[i] - 0xA2)); i++; continue; }
                var c = d[i + 1];
                sb.Append(c <= 0x19 ? (char)('A' + c) : c <= 0x33 ? (char)('a' + c - 0x1A) : c is >= 0x57 and <= 0x60 ? (char)('0' + c - 0x57) : '?');
                i += 2;
            }

            var name = sb.ToString().Trim();
            return name.Length > 0 ? name : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Fecha de la partida recién guardada ("1020-08-1"), de la cabecera de la grabación o del contador.</summary>
    private unsafe string SaveDate(string? saveFile)
    {
        try
        {
            if (saveFile != null && File.Exists(saveFile))
            {
                var d = File.ReadAllBytes(saveFile);
                int? year = null, month = null, week = null;
                var number = 0;
                var digits = 0;
                for (var i = 0; i < Math.Min(d.Length - 1, 0x80); i++)
                {
                    if (d[i] == 0x82 && d[i + 1] is >= 0x4F and <= 0x58) { number = number * 10 + d[i + 1] - 0x4F; digits++; i++; continue; }
                    if (digits > 0)
                    {
                        if (d[i] == 0x94 && d[i + 1] == 0x4E) year = number;
                        else if (d[i] == 0x8C && d[i + 1] == 0x8E) month = number;
                        else if (d[i] == 0x8F && d[i + 1] == 0x54) week = number;
                    }

                    number = 0;
                    digits = 0;
                }

                if (year != null && month != null && week != null) return $"{year}-{month:00}-{week}";
            }
        }
        catch (IOException)
        {
            // se usa el contador
        }

        // El contador de semanas va 12 semanas atrás de la fecha real (semana 0 = año 1000, mes 4, semana 1).
        var w = (long)*(uint*)(_exeBase + WeekCounterOffset) + 12;
        return $"{1000 + w / 48}-{w % 48 / 4 + 1:00}-{w % 4 + 1}";
    }

    /// <summary>
    ///     A monster's name. In DX (English) each character is stored with prefix 0xB0, whereas the tournament
    ///     file and the base mod use 0xB5 (same code), so the base mod decodes it wrong (表表表...).
    /// </summary>
    private static unsafe string? ReadName(byte* p)
    {
        // Letters take 2 bytes (prefix + code); digits typed in the name screen take 1 byte: 0xA2 = '0' ... 0xAB = '9'
        // (a monster called "99" is AB AB). Everything is converted to the 2-byte form of the decoder (B5 57 = '0').
        var raw = Enumerable.Repeat((byte)0xFF, 26).ToArray();
        var at = 0;
        for (var i = 0; i < 24 && at < 24; )
        {
            var hi = p[i];
            if (hi == 0xFF) break;
            if (hi is >= 0xA2 and <= 0xAB)
            {
                raw[at++] = 0xB5;
                raw[at++] = (byte)(0x57 + hi - 0xA2);
                i++;
                continue;
            }

            raw[at++] = hi == 0xB0 ? (byte)0xB5 : hi;
            raw[at++] = p[i + 1];
            i += 2;
        }

        string name;
        try
        {
            name = raw.AsShorts().AsString().Trim();
        }
        catch
        {
            // Some character the decoder does not know (symbols, accents): decoded one by one, unknown ones as "?".
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < 12 && raw[i * 2] != 0xFF; i++)
            {
                try
                {
                    sb.Append(new byte[] { raw[i * 2], raw[i * 2 + 1], 0xFF, 0xFF }.AsShorts().AsString());
                }
                catch
                {
                    sb.Append('?');
                    UnknownChars.Add($"{raw[i * 2]:X2}{raw[i * 2 + 1]:X2}");
                }
            }

            name = sb.ToString().Trim();
        }

        return name.Length > 0 && !name.Contains('表') ? name : null;
    }

    /// <summary>Character codes of names the decoder did not know (reported in the log after exporting).</summary>
    private static readonly HashSet<string> UnknownChars = [];

    private static string FileSafe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string((name ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return s.Length > 0 ? s : "monstruo";
    }

    private void Log(string msg, Color? color = null) =>
        _logger.WriteLine($"[{_modConfig.ModId}] {msg}", color ?? _logger.TextColor);
}
