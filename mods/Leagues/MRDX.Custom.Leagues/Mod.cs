using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
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
        if (week == _drawnWeek) return;
        _drawnWeek = week;

        if (!_config.Enabled)
        {
            SetRedirect(false);
            return;
        }

        try
        {
            var pool = LoadPool();
            var data = (byte[])_original.Clone();
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
                    byPickStrength[i].ToRecord().CopyTo(data, RecordsOffset + bySlotStrength[i] * RecordSize);

                summary.Add($"{league}: {string.Join(", ", byPickStrength.Select(p => p.Name))}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_generated)!);
            var tmp = _generated + ".tmp";
            File.WriteAllBytes(tmp, data);
            File.Copy(tmp, _generated, true);
            File.Delete(tmp);
            SetRedirect(true);

            Log($"Rivales de la semana {week} ({pool.Count} monstruos en las ligas).");
            if (_config.VerboseLog)
                foreach (var line in summary) Log("  " + line);
        }
        catch (Exception e)
        {
            Log($"No se pudieron sortear los rivales: {e.Message}", Color.Red);
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

    private void OnSave(ISaveFileEntry file)
    {
        if (!_config.ExportOnSave) return;
        var exported = new List<string>();
        Export(_exeBase + MonsterOffset, "rancho", exported);
        for (var i = 0; i < FreezerSlots; i++)
            Export(_exeBase + FreezerOffset + (nuint)(i * FreezerSlotSize + FreezerMonsterStart), $"congelador {i + 1}", exported);
        if (exported.Count > 0)
            Log($"Exportados {exported.Count} monstruos a {_exportDir}: {string.Join(", ", exported)}");
    }

    private unsafe void Export(nuint s, string where, List<string> exported)
    {
        try
        {
            var p = (byte*)s;
            if (p[NameOffset] == 0xFF) return; // empty slot
            var main = *(int*)(p + OffGenusMain);
            var sub = *(int*)(p + OffGenusSub);
            var life = *(ushort*)(p + OffLife);
            if (main is < 0 or > 45 || sub is < 0 or > 45 || life == 0) return;

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
                BattleSpecials = *(ushort*)(p + OffBattleSpecials)
            };

            Directory.CreateDirectory(_exportDir);
            var path = Path.Combine(_exportDir, $"{FileSafe(m.Name)}_{m.GenusMain}-{m.GenusSub}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(m, LeagueMonster.Json));
            exported.Add($"{m.Name} ({where}, liga {m.League})");
            if (_config.VerboseLog)
                Log($"  {m.Label}, rango {rank} → liga {m.League}, naturaleza {m.Nature}, técnicas [{string.Join(",", techs)}]");
        }
        catch (Exception e)
        {
            Log($"No se pudo exportar el monstruo ({where}): {e.Message}", Color.Orange);
        }
    }

    /// <summary>
    ///     A monster's name. In DX (English) each character is stored with prefix 0xB0, whereas the tournament
    ///     file and the base mod use 0xB5 (same code), so the base mod decodes it wrong (表表表...).
    /// </summary>
    private static unsafe string? ReadName(byte* p)
    {
        var raw = new byte[26];
        raw[24] = 0xFF;
        for (var i = 0; i < 12; i++)
        {
            var hi = p[i * 2];
            if (hi == 0xFF) { raw[i * 2] = 0xFF; break; }
            raw[i * 2] = hi == 0xB0 ? (byte)0xB5 : hi;
            raw[i * 2 + 1] = p[i * 2 + 1];
        }

        var name = raw.AsShorts().AsString().Trim();
        return name.Length > 0 && !name.Contains('表') ? name : null;
    }

    private static string FileSafe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string((name ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return s.Length > 0 ? s : "monstruo";
    }

    private void Log(string msg, Color? color = null) =>
        _logger.WriteLine($"[{_modConfig.ModId}] {msg}", color ?? _logger.TextColor);
}
