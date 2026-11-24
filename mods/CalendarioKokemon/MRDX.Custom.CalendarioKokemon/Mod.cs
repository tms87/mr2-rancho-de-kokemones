using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using MRDX.Base.Mod.Interfaces;
using MRDX.Custom.CalendarioKokemon.Configuration;
using MRDX.Custom.CalendarioKokemon.Template;
using Reloaded.Mod.Interfaces;
using Reloaded.Universal.Redirector.Interfaces;

namespace MRDX.Custom.CalendarioKokemon;

/// <summary>
///     Calendario Kokemon: new tournaments and tournament calendar changes. Generates modified copies of the game's
///     tables (mf2\data\obj\btl_*.obj and the calendar copy in farm_etc*.obj) and the Reloaded redirector serves
///     them. The same tables also live inside taikai_en.flk: "Rivales Kokemon" picks them up from generado\ when it
///     generates that file.
/// </summary>
public class Mod : ModBase
{
    private const int TournamentCount = 80;  // BTL_00..BTL_4F
    private const int GoldEntrySize = 10;    // prizes per tournament
    private const int CalendarSize = 288;    // 12 months x 6 ranks x 4 weeks
    private static readonly string[] Ranks = ["S", "A", "B", "C", "D", "E"];

    private readonly ILogger _logger = null!;
    private readonly IModConfig _modConfig = null!;
    private Config _config = null!;
    private readonly IRedirectorController? _redirector;
    private readonly string _modDir = "";
    private readonly string _outDir = "";
    private readonly string _objDir = "";
    private readonly string _exeDir = "";

    public Mod(ModContext context)
    {
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        _config = context.Configuration;
        var loader = context.ModLoader;
        _modDir = loader.GetDirectoryForModId(_modConfig.ModId);
        _outDir = Path.Combine(loader.GetModConfigDirectory(_modConfig.ModId), "generado");
        _exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!;
        _objDir = Path.Combine(_exeDir, "Resources", "data", "mf2", "data", "obj");
        loader.GetController<IRedirectorController>()?.TryGetTarget(out _redirector);

        if (_redirector == null)
        {
            Log("Sin el redirector de Reloaded no se pueden cambiar los torneos.", Color.Red);
            return;
        }

        _redirector.Loading += OnLoading;
        IGame? game = null;
        loader.GetController<IGame>()?.TryGetTarget(out game);
        if (game != null) game.OnWeekChange += OnWeekChange;
        else Log("Sin MRDX.Base.Mod el calendario solo cambia de año al cargar la partida.", Color.Orange);
        if (context.Hooks != null)
            _buildMonthHook = context.Hooks.CreateHook<BuildMonth>(OnBuildMonth, (nint)(_exeBase + BuildMonthFunction)).Activate();
        else Log("Sin Reloaded Hooks el calendario muestra un solo nombre para los números compartidos.", Color.Orange);

        if (!_config.Enabled)
        {
            Log("Desactivado: torneos y calendario originales.");
            DeleteGenerated();
            return;
        }

        try
        {
            Generate();
        }
        catch (Exception e)
        {
            Log($"No se pudieron preparar los torneos: {e.Message}", Color.Red);
            DeleteGenerated();
        }
    }

    public Mod()
    {
    }

    public override void ConfigurationUpdated(Config configuration)
    {
        _config = configuration;
        Log("Configuración actualizada.");
        if (_def == null) return;
        try
        {
            if (CurrentPeriod() is { } p) ServeCalendar(p);
        }
        catch (Exception e)
        {
            Log($"No se pudo armar el calendario del año: {e.Message}", Color.Red);
        }
    }

    // ------------------------------------------------------------------
    // Definition (torneos.json)
    // ------------------------------------------------------------------

    private sealed class Definition
    {
        [JsonPropertyName("torneos")] public List<Tournament> Tournaments { get; set; } = [];
        [JsonPropertyName("calendario")] public List<CalendarEntry> Calendar { get; set; } = [];

        /// <summary>Small tournaments per year and rank: [minimum, maximum].</summary>
        [JsonPropertyName("cantidades")] public Dictionary<string, int[]> Counts { get; set; } = [];

        /// <summary>Original small tournaments taken out of the yearly draw (replaced by new ones).</summary>
        [JsonPropertyName("excluir")] public List<int> Exclude { get; set; } = [];
    }

    private sealed class Tournament
    {
        /// <summary>Tournament number. Without it, the tournament enters the yearly draw of its rank and the mod gives it a
        /// free number in the month it is played.</summary>
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("nombre")] public string Name { get; set; } = "";
        [JsonPropertyName("plantilla")] public int Template { get; set; }

        /// <summary>
        ///     Rival slots (numbers, random: the weekly draw of "Rivales Kokemon"), monster names (fixed),
        ///     or the text "mejores" (the strongest of its league).
        /// </summary>
        [JsonPropertyName("participantes")] public JsonElement Participants { get; set; }

        /// <summary>Enters the yearly draw of its rank (if it has no fixed date in "calendario").</summary>
        [JsonPropertyName("sorteo")] public bool Draw { get; set; } = true;

        /// <summary>
        ///     Months (1-12) in which this tournament uses its number. Several tournaments can share a number if their
        ///     months do not overlap. Empty: every month.
        /// </summary>
        [JsonPropertyName("meses")] public List<int> Months { get; set; } = [];

        /// <summary>Prize money by place (1st, 2nd, 3rd, 4th). Empty: the template's.</summary>
        [JsonPropertyName("premios")] public List<int> Prizes { get; set; } = [];

        /// <summary>Bonus item: a name, or a list of names (one is drawn each year). Missing: the template's.</summary>
        [JsonPropertyName("bonus")] public JsonElement Bonus { get; set; }

        /// <summary>Fixed date (every year) for a tournament without "id": it gets a free number that month.</summary>
        [JsonPropertyName("fecha")] public DateEntry? Date { get; set; }
        /// <summary>PNG 480x192 (relative to the mod folder). Empty: the template's.</summary>
        [JsonPropertyName("banner")] public string Banner { get; set; } = "";
        /// <summary>PNG 240x64 with the name, transparent background. Empty: the template's.</summary>
        [JsonPropertyName("imagenNombre")] public string NameImage { get; set; } = "";
    }

    private sealed class DateEntry
    {
        [JsonPropertyName("mes")] public int Month { get; set; }
        [JsonPropertyName("semana")] public int Week { get; set; }
    }

    private sealed class CalendarEntry
    {
        [JsonPropertyName("mes")] public int Month { get; set; }
        [JsonPropertyName("semana")] public int Week { get; set; }
        [JsonPropertyName("rango")] public string Rank { get; set; } = "";
        [JsonPropertyName("torneo")] public int Tournament { get; set; }
    }

    // ------------------------------------------------------------------
    // Generation
    // ------------------------------------------------------------------

    private void Generate()
    {
        var def = JsonSerializer.Deserialize<Definition>(File.ReadAllText(Path.Combine(_modDir, "torneos.json")),
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;

        var form = File.ReadAllBytes(Path.Combine(_objDir, "btl_form.obj"));
        var gold = File.ReadAllBytes(Path.Combine(_objDir, "btl_gold.obj"));
        var year = File.ReadAllBytes(Path.Combine(_objDir, "btl_year.obj"));
        var stage = File.ReadAllBytes(Path.Combine(_objDir, "btl_stag.obj"));
        if (gold.Length != TournamentCount * GoldEntrySize || year.Length != CalendarSize || stage.Length != CalendarSize)
            throw new InvalidDataException("las tablas de torneos no tienen el tamaño esperado");

        LoadItemNames();
        var records = ReadRecords(form);
        _originalRecords = records.Select(r => (byte[])r.Clone()).ToArray();
        _originalGold = (byte[])gold.Clone();
        _variants.Clear();
        _fixedKeys.Clear();
        _dated.Clear();
        _counts.Clear();
        Directory.CreateDirectory(_outDir);

        // New tournaments
        _autoPool.Clear();
        foreach (var t in def.Tournaments)
        {
            if (t.Id is < 0 or >= TournamentCount || t.Template is < 0 or >= TournamentCount)
                throw new InvalidDataException($"torneo \"{t.Name}\": número o plantilla fuera de rango");
            var template = records[t.Template];
            if (template.Length < 11) throw new InvalidDataException($"la plantilla {t.Template} está vacía");
            var rankIndex = 0x85 - template[3]; // grade 0x85 = S ... 0x80 = E
            var slots = ParticipantSlots(t, template, rankIndex);
            if (slots.Count is < 1 or > 15 || slots.Any(p => p is 0 or > 255))
                throw new InvalidDataException($"torneo \"{t.Name}\": participantes inválidos");

            // Record with the template's first slot in place of the fixed participants (replaced each month).
            var rec = new List<byte>(template[..11]) { [10] = (byte)slots.Count };
            rec.AddRange(slots.Select(p => p > 0 ? (byte)p : template[11]));
            if (rec.Count % 2 == 1) rec.Add(0);
            // Prizes (btl_gold, 10 bytes per tournament): u16 bonus item (0xFFFF = none), then u16 money for places 1 to 4.
            var goldEntry = gold[(t.Template * GoldEntrySize)..((t.Template + 1) * GoldEntrySize)];
            if (t.Prizes.Count > 4 || t.Prizes.Any(x => x is < 0 or > 65535))
                throw new InvalidDataException($"torneo \"{t.Name}\": \"premios\" son hasta 4 montos de 0 a 65535");
            for (var k = 0; k < t.Prizes.Count; k++) BitConverter.GetBytes((ushort)t.Prizes[k]).CopyTo(goldEntry, 2 + k * 2);
            foreach (var item in BonusNames(t))
                if (!(int.TryParse(item, out var n) ? _itemNames.ContainsKey(n) : _itemIds.ContainsKey(ItemKey(item))))
                    throw new InvalidDataException($"torneo \"{t.Name}\": el ítem \"{item}\" no existe");
            var where = t.Id is { } shownId ? $"{shownId:X2}" : "sin número";
            var shownSlots = slots.Select(x => x > 0 ? x.ToString() : FixedLabel(_fixedKeys[-x - 1]));
            var when = t.Id != null ? "" : t.Date != null ? $", fecha fija {t.Date.Month}/{t.Date.Week}" : ", entra al sorteo";
            Log($"Torneo {where} \"{t.Name}\": plantilla {t.Template:X2}, rango {(rankIndex is >= 0 and < 6 ? Ranks[rankIndex] : "?")}, " +
                $"{slots.Count} rivales ({string.Join(", ", shownSlots)}){when}");
            if (t.Id == null)
            {
                var entry = new Entry(t, rec.ToArray(), goldEntry, slots);
                if (t.Date != null)
                {
                    if (t.Date.Month is < 1 or > 12 || t.Date.Week is < 1 or > 4 || rankIndex is < 0 or > 5)
                        throw new InvalidDataException($"torneo \"{t.Name}\": fecha inválida");
                    _dated.Add((((t.Date.Month - 1) * Ranks.Length + rankIndex) * 4 + (t.Date.Week - 1), entry));
                    continue;
                }

                if (rankIndex is < 1 or > 5)
                    throw new InvalidDataException($"torneo \"{t.Name}\": sin \"id\" ni \"fecha\" tiene que ser de rango A a E (entra al sorteo)");
                _autoPool.Add((rankIndex, entry));
                continue;
            }

            var id = t.Id.Value;
            if (t.Months.Any(m => m is < 1 or > 12))
                throw new InvalidDataException($"torneo {id}: \"meses\" tiene que tener números de 1 a 12");
            var clash = _variants.FirstOrDefault(v => v.T.Id == id &&
                (v.T.Months.Count == 0 || t.Months.Count == 0 || v.T.Months.Intersect(t.Months).Any()));
            if (clash != null)
                throw new InvalidDataException($"torneo {id}: \"{t.Name}\" y \"{clash.T.Name}\" usan el mismo número en el mismo mes");
            _variants.Add(new Entry(t, rec.ToArray(), goldEntry, slots));
            if (records[id].Length < 11 || _variants.Count(v => v.T.Id == id) == 1) records[id] = rec.ToArray();
        }

        // Free numbers for the tournaments without "id": unused by the game (hidden or empty) and by torneos.json.
        _freeIds = FreeIdCandidates.Where(i => _variants.All(v => v.T.Id != i)).ToList();
        if (_autoPool.Count > 0)
            Log($"{_autoPool.Count} torneos sin número entran al sorteo; números libres: {string.Join(" ", _freeIds.Select(i => i.ToString("X2")))}");

        foreach (var (rank, range) in def.Counts)
        {
            var r = Array.IndexOf(Ranks, rank.Trim().ToUpperInvariant());
            if (r is < 1 or > 5 || range.Length != 2 || range[0] < 0 || range[1] < range[0])
                throw new InvalidDataException($"cantidades: rango \"{rank}\" inválido (se espera A a E con [mínimo, máximo])");
            _counts[r] = (range[0], range[1]);
        }

        // Calendar entries of torneos.json (validated once; applied every year on top of the draw)
        foreach (var c in def.Calendar)
        {
            var rank = Array.IndexOf(Ranks, c.Rank.Trim().ToUpperInvariant());
            if (c.Month is < 1 or > 12 || c.Week is < 1 or > 4 || rank < 0)
                throw new InvalidDataException($"calendario: fecha inválida (mes {c.Month}, semana {c.Week}, rango {c.Rank})");
            if (c.Tournament >= 0 && (c.Tournament >= TournamentCount || records[c.Tournament].Length < 11))
                throw new InvalidDataException($"calendario: el torneo {c.Tournament} no existe");
            _fixedCells[((c.Month - 1) * Ranks.Length + rank) * 4 + (c.Week - 1)] = c.Tournament;
        }

        _records = records;
        _originalYear = year;
        _originalStage = stage;
        var farmEn = Path.Combine(_objDir, "farm_etc_en.obj");
        if (File.Exists(farmEn)) _farmEn = File.ReadAllBytes(farmEn);
        var farmJp = Path.Combine(_objDir, "farm_etc.obj");
        if (File.Exists(farmJp)) _farmJp = File.ReadAllBytes(farmJp);
        var msgBtl = Path.Combine(_objDir, "msg_btl_en.obj");
        if (File.Exists(msgBtl)) _msgBtl = File.ReadAllBytes(msgBtl);
        _def = def; // after reading the originals (those reads also go through OnLoading)

        Directory.CreateDirectory(_outDir);
        ServeCalendar(CurrentPeriod() ?? 0);

        Log("Torneos y calendario listos.", Color.LightGreen);
    }

    /// <summary>Rival slots unused by every original tournament (leftover data): used for fixed participants.</summary>
    private const int FirstFreeSlot = 116, LastFreeSlot = 135;

    /// <summary>
    ///     Rival slots of a new tournament. "participantes" can be: missing (the template's), "mejores" (the strongest of
    ///     its league, as many as the template has), or a list mixing slot numbers (random rivals of the weekly draw),
    ///     monster names and "mejor N" (the Nth strongest of its league).
    /// </summary>
    private List<int> ParticipantSlots(Tournament t, byte[] template, int rankIndex)
    {
        var league = rankIndex is >= 0 and < 6 ? Ranks[rankIndex] : "";
        // Fixed participant: -(index in _fixedKeys + 1). The rival slot is chosen each month (ApplyMonth), only for
        // the tournaments of that month, so the 20 free slots are enough.
        int Fixed((string League, int Place, string Name) key)
        {
            var i = _fixedKeys.IndexOf(key);
            if (i < 0)
            {
                _fixedKeys.Add(key);
                i = _fixedKeys.Count - 1;
            }

            return -(i + 1);
        }

        // place > 0: the Nth strongest; < 0: the Nth weakest.
        int Best(int place) => place != 0 && league != ""
            ? Fixed((league, place, ""))
            : throw new InvalidDataException($"torneo {t.Name}: \"mejor/peor {Math.Abs(place)}\" inválido");

        var p = t.Participants;
        switch (p.ValueKind)
        {
            case JsonValueKind.Undefined or JsonValueKind.Null:
                return template[11..(11 + template[10])].Select(b => (int)b).ToList();
            case JsonValueKind.String when p.GetString()!.Trim().Equals("mejores", StringComparison.OrdinalIgnoreCase):
                return Enumerable.Range(1, template[10]).Select(Best).ToList();
            case JsonValueKind.Array:
                return p.EnumerateArray().Select(e =>
                {
                    if (e.ValueKind == JsonValueKind.Number) return e.GetInt32();
                    var s = e.GetString()?.Trim() ?? "";
                    if (s.StartsWith("mejor ", StringComparison.OrdinalIgnoreCase) && int.TryParse(s[6..], out var n))
                        return Best(n);
                    if (s.StartsWith("peor ", StringComparison.OrdinalIgnoreCase) && int.TryParse(s[5..], out var w))
                        return Best(-w);
                    if (s == "") throw new InvalidDataException($"torneo \"{t.Name}\": participante vacío");
                    return Fixed(("", 0, s));
                }).ToList();
            default:
                throw new InvalidDataException($"torneo \"{t.Name}\": \"participantes\" debe ser una lista o \"mejores\"");
        }
    }

    // ------------------------------------------------------------------
    // Calendar of each year
    // ------------------------------------------------------------------

    /// <summary>A new tournament ready to use: its definition, its TAIKAI_FORM record and its prizes.</summary>
    // Item names of the tournament file (taikai_en.flk, section 5): lower-case name → first number with that name.
    private readonly Dictionary<string, int> _itemIds = new();
    private readonly Dictionary<int, string> _itemNames = new();

    private void LoadItemNames()
    {
        _itemIds.Clear();
        _itemNames.Clear();
        var flk = Path.Combine(_exeDir, "Resources", "data", "mf2", "data", "taikai", "taikai_en.flk");
        if (!File.Exists(flk)) return;
        var b = File.ReadAllBytes(flk);
        var sec = BitConverter.ToInt32(b, 8 + 5 * 4);
        var count = BitConverter.ToInt32(b, sec) / 4;
        for (var i = 0; i < count; i++)
        {
            var at = sec + BitConverter.ToInt32(b, sec + i * 4);
            var end = at;
            while (end + 1 < b.Length && b[end] != 0xFF) end += 2;
            string name;
            try
            {
                name = b[at..(end + 1)].AsShorts().AsString().Trim();
            }
            catch
            {
                continue;
            }

            if (name.Length == 0) continue;
            _itemNames[i] = name;
            _itemIds.TryAdd(ItemKey(name), i);
        }
    }

    /// <summary>Item name to look up: letters and digits only, lower case (the game writes "Double‐edged" with its own dash).</summary>
    private static string ItemKey(string name) => new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    ///     Bonus groups: "bonus" can be a name/number, a list (one drawn), or a list of lists (first a group is drawn,
    ///     all with the same chance, then an item of it).
    /// </summary>
    private static List<List<string>> BonusGroups(Tournament t)
    {
        static string Item(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetInt32().ToString() : e.GetString()?.Trim() ?? "";
        var b = t.Bonus;
        return b.ValueKind switch
        {
            JsonValueKind.String or JsonValueKind.Number => [[Item(b)]],
            JsonValueKind.Array when b.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Array) =>
                b.EnumerateArray().Select(g => g.ValueKind == JsonValueKind.Array
                    ? g.EnumerateArray().Select(Item).Where(x => x != "").ToList()
                    : [Item(g)]).Where(g => g.Count > 0).ToList(),
            JsonValueKind.Array => [b.EnumerateArray().Select(Item).Where(x => x != "").ToList()],
            _ => []
        };
    }

    /// <summary>All the bonus items of a tournament (names, or numbers for items that share a name).</summary>
    private static List<string> BonusNames(Tournament t) => BonusGroups(t).SelectMany(g => g).ToList();

    /// <summary>Bonus item of a tournament in a year: its own (or one of its list, drawn by year), or null = template's.</summary>
    private int? BonusOf(Tournament t, int year)
    {
        var groups = BonusGroups(t).Where(g => g.Count > 0).ToList();
        if (groups.Count == 0) return null;
        var rng = new Random(Hash(_config.Seed, year * 7919 + t.Name.Length));
        var group = groups[rng.Next(groups.Count)];
        var pick = group[rng.Next(group.Count)];
        return int.TryParse(pick, out var number) ? number : _itemIds[ItemKey(pick)];
    }

    private int[] _shownYears = new int[12]; // year (index) of each month of the calendar served

    private byte[]? _msgBtl; // battle messages (msg_btl_en.obj; the copy inside taikai_en.flk is the one the game uses)

    /// <summary>
    ///     Battle messages with the names of the month's new tournaments: the announcer says
    ///     "Under this condition, NAME is about to start!" with entry 96 + tournament number (a table of u32 offsets
    ///     relative to the start, texts B5xx ending in FF). The names are added at the end.
    /// </summary>
    private static byte[] BattleMessages(byte[] original, Dictionary<int, Entry> map)
    {
        const int NameEntries = 96;
        var o = new List<byte>(original);
        foreach (var (id, e) in map)
        {
            var at = o.Count;
            o.AddRange(e.T.Name.Trim().ToUpperInvariant().AsMr2().AsBytes().TakeWhile(b => b != 0xFF));
            o.Add(0xFF);
            if (o.Count % 2 == 1) o.Add(0xFF);
            var b = BitConverter.GetBytes(at);
            for (var k = 0; k < 4; k++) o[(NameEntries + id) * 4 + k] = b[k];
        }

        if (o.Count > 0x4000) throw new InvalidDataException("los mensajes no entran en su lugar del archivo de torneos");
        return o.ToArray();
    }

    private sealed record Entry(Tournament T, byte[] Record, byte[] Gold, List<int> Slots)
    {
        /// <summary>Fixed participants (keys), as negative numbers.</summary>
        public IEnumerable<int> FixedKeys => Slots.Where(x => x < 0);
    }

    // Fixed participants of all the tournaments: (league, place, name). Place > 0 = Nth strongest, < 0 = Nth weakest.
    private readonly List<(string League, int Place, string Name)> _fixedKeys = [];

    // Tournaments without "id" with a fixed date: calendar cell → tournament.
    private readonly List<(int Cell, Entry E)> _dated = [];

    private static string FixedLabel((string League, int Place, string Name) k) =>
        k.Place > 0 ? $"mejor {k.Place}" : k.Place < 0 ? $"peor {-k.Place}" : k.Name;

    private const int FreeSlotCount = LastFreeSlot - FirstFreeSlot + 1;

    /// <summary>Distinct fixed participants of a set of tournaments (they share a slot when it is the same).</summary>
    private static int FixedCount(IEnumerable<Entry> entries) => entries.SelectMany(e => e.FixedKeys).Distinct().Count();

    private readonly List<(int Rank, Entry E)> _autoPool = []; // tournaments without "id": yearly draw, number per month
    private List<int> _freeIds = [];

    /// <summary>Numbers the game does not use: hidden tournaments first (they already have a rank), then empty ones.</summary>
    /// Not 0x3C: the calendar shows its text when an empty week is selected (0x4ED550 uses 0x3C for 0xFF).
    private static readonly int[] FreeIdCandidates = [0x0F, 0x17, 0x18, 0x24, 0x25, 0x30, 0x31, 0x3B, 0x0E, 0x02];

    /// <summary>For each month (0-11) of the calendar served: number → tournament.</summary>
    private Dictionary<int, Entry>[] _monthIds = Enumerable.Range(0, 12).Select(_ => new Dictionary<int, Entry>()).ToArray();
    private readonly Dictionary<int, (int Min, int Max)> _counts = new(); // per rank (1 = A .. 5 = E)

    private Definition? _def;
    private byte[][] _originalRecords = [];
    private byte[] _originalGold = [];
    private readonly List<Entry> _variants = []; // tournaments with "id"
    private Dictionary<int, Entry> _active = new(); // number → tournament in the current month
    private string _activeKey = "";

    // Calendar texts of the numbers shared by several tournaments: months (1-12) → text (all of the same size).
    private readonly Dictionary<int, List<(HashSet<int> Months, byte[] Text)>> _textVariants = new();

    // The calendar screen (0x4ED2D0) stores the text section pointer here and builds each month with 0x4EDA10,
    // whose object has the month shown (1-12) at +0x44.
    private const int TextSectionPointer = 0x369D44; // static 0x769D44
    private const int BuildMonthFunction = 0x0EDA10; // static 0x4EDA10

    [Reloaded.Hooks.Definitions.X86.Function(Reloaded.Hooks.Definitions.X86.CallingConventions.MicrosoftThiscall)]
    private delegate void BuildMonth(nint self);

    private Reloaded.Hooks.Definitions.IHook<BuildMonth>? _buildMonthHook;

    private unsafe void OnBuildMonth(nint self)
    {
        try
        {
            // +0x4B = number of the tournament the info panel shows; it only redraws when it changes, so after a swap
            // it is reset (0xFF = none, as when the screen opens).
            if (_textVariants.Count > 0 && SwapTexts(*(ushort*)(self + 0x44))) *(byte*)(self + 0x4B) = 0xFF;
        }
        catch (Exception e)
        {
            Log($"Calendario: no se pudo cambiar el nombre de los torneos del mes ({e.Message}).", Color.Orange);
        }

        _buildMonthHook!.OriginalFunction(self);
    }

    /// <summary>Puts in the loaded calendar texts the tournament each shared number means in that month.</summary>
    private unsafe bool SwapTexts(int month)
    {
        var swapped = false;
        var section = (nuint)(*(uint*)(_exeBase + TextSectionPointer));
        if (section < 0x10000) return false;
        foreach (var (id, variants) in _textVariants)
        {
            var pick = variants.FirstOrDefault(v => v.Months.Contains(month));
            if (pick.Text == null) continue; // the number is not used that month
            var off = *(uint*)(section + (nuint)(id * 4));
            var entry = off < 0x100000 ? section + off : (nuint)off; // offset, or pointer if the game converted it
            var current = new Span<byte>((void*)entry, pick.Text.Length).ToArray();
            // Only over one of our texts (the buffer can hold another file).
            if (!variants.Any(v => current.AsSpan().SequenceEqual(v.Text))) return swapped;
            if (current.AsSpan().SequenceEqual(pick.Text)) continue;
            pick.Text.CopyTo(new Span<byte>((void*)entry, pick.Text.Length));
            swapped = true;
        }

        return swapped;
    }

    /// <summary>
    ///     Tournament tables for the month being played: each new number takes the data of the tournament that uses it
    ///     that month (name, format, rivals, prize, images). "Rivales Kokemon" picks up btl_form/btl_gold from generado.
    /// </summary>
    private void ApplyMonth(int month0, int period, HashSet<int> present)
    {
        var map = _monthIds[month0];
        var key = period + ":" + string.Join(",", present.OrderBy(x => x)) + ":" + string.Join(",", map.OrderBy(k => k.Key)
            .Select(k => $"{k.Key}={System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.Value)}")); // each edition by itself, not by name
        if (key == _activeKey) return;
        _activeKey = key;
        _active = new Dictionary<int, Entry>(map);
        var records = _originalRecords.Select(r => (byte[])r.Clone()).ToArray();
        var gold = (byte[])_originalGold.Clone();
        // Fixed participants of the month's tournaments: one free slot each ("Rivales Kokemon" puts the monster).
        var slotOf = new Dictionary<int, int>();
        // Only for the tournaments that are in this month's calendar.
        foreach (var k in map.Where(kv => present.Contains(kv.Key)).SelectMany(kv => kv.Value.FixedKeys).Distinct())
        {
            if (slotOf.Count >= FreeSlotCount)
            {
                Log($"Mes {month0 + 1}: más de {FreeSlotCount} participantes fijos; los que sobran quedan con el rival de la plantilla.", Color.Orange);
                break;
            }

            slotOf[k] = FirstFreeSlot + slotOf.Count;
        }

        File.WriteAllText(Path.Combine(_outDir, "participantes.json"), JsonSerializer.Serialize(
            slotOf.Select(kv => (Slot: kv.Value, Key: _fixedKeys[-kv.Key - 1]))
                .Select(x => new { hueco = x.Slot, liga = x.Key.League, puesto = x.Key.Place, nombre = x.Key.Name }),
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

        foreach (var (id, e) in map)
        {
            // Participants in a different order each month (same for everyone: it depends on the seed), so the
            // matchups of the bracket change. Bytes 11.. of the record are the rival slots.
            var rec = (byte[])e.Record.Clone();
            for (var k = 0; k < e.Slots.Count; k++)
                if (e.Slots[k] < 0 && slotOf.TryGetValue(e.Slots[k], out var slot)) rec[11 + k] = (byte)slot;
            var order = rec[11..(11 + rec[10])].ToList();
            Shuffle(order, new Random(Hash(_config.Seed, period * 100 + id)));
            order.ToArray().CopyTo(rec, 11);
            records[id] = rec;
            e.Gold.CopyTo(gold, id * GoldEntrySize);
            if (BonusOf(e.T, _shownYears[month0]) is { } bonusItem)
                BitConverter.GetBytes((ushort)bonusItem).CopyTo(gold, id * GoldEntrySize);
            ServeImages(e.T, id);
        }

        Serve("btl_form.obj", WriteRecords(records));
        Serve("btl_gold.obj", gold);
        if (_msgBtl != null)
            try
            {
                Serve("msg_btl_en.obj", BattleMessages(_msgBtl, map));
            }
            catch (Exception ex)
            {
                Log($"No se pudieron poner los nombres para el comentarista: {ex.Message}", Color.Orange);
            }

        Log($"Torneos nuevos del mes {month0 + 1}: " +
            (map.Count == 0 ? "ninguno" : string.Join(", ", map.OrderBy(k => k.Key).Select(k => $"{k.Key:X2} = {k.Value.T.Name}"))));
    }

    /// <summary>Numbers with an "id" in torneos.json, for a month (0-11).</summary>
    private Dictionary<int, Entry> FixedIdsOfMonth(int month0) => _variants
        .GroupBy(v => v.T.Id!.Value)
        .Select(g => (g.Key, E: g.FirstOrDefault(v => v.T.Months.Contains(month0 + 1)) ?? g.FirstOrDefault(v => v.T.Months.Count == 0)))
        .Where(x => x.E != null).ToDictionary(x => x.Key, x => x.E!);

    private byte[][]? _records;
    private byte[] _originalYear = [], _originalStage = [];
    private byte[]? _farmEn, _farmJp;
    private readonly Dictionary<int, int> _fixedCells = new(); // calendar index → tournament (-1 = none)
    private int _servedPeriod = int.MinValue; // year * 12 + month (0-11) of the calendar served
    private readonly nuint _exeBase = (nuint)Process.GetCurrentProcess().MainModule!.BaseAddress;

    private const int WeekCounterOffset = 0x379444; // week counter (from the start of the .exe)

    /// <summary>
    ///     The game's week counter. In the test save, 900 was January week 1, so the year starts when
    ///     (counter + 12) is a multiple of 48.
    /// </summary>
    private unsafe uint? CurrentCounter()
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

    /// <summary>Year * 12 + month (0-11) of a week counter value (year 0 = 1000).</summary>
    private static int Period(uint w) => (int)((w + 12) / 4);

    private int? CurrentPeriod() => CurrentCounter() is { } w ? Period(w) : null;

    /// <summary>
    ///     Tournaments the game shows only under a condition (function 0x4EDCC0 / 0x4EDA10): the "Elimination" cups of
    ///     July week 4 (every 4 years, only the monster's rank), the majors, Legend, invitations, FIMBA and the free
    ///     cups 00/01/03/04. They keep their date.
    /// </summary>
    private static readonly HashSet<int> ConditionalTournaments =
        [0x00, 0x01, 0x03, 0x04, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x12, 0x16, 0x1A, 0x1C, 0x27, 0x33, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F];

    /// <summary>Small tournament: rank A to E, not official/major/free, normal format, regular league rivals.</summary>
    private bool IsSmall(int t)
    {
        if (_records == null || t is < 0 or >= TournamentCount || ConditionalTournaments.Contains(t)) return false;
        var r = _records[t];
        if (r.Length < 11 || r[3] is < 0x80 or > 0x84 || r[8] > 4 || r[10] is < 1 or > 15) return false;
        for (var k = 0; k < r[10]; k++)
            if (r[11 + k] is < 1 or > 50) return false; // special rivals (exhibitions, guests)
        return true;
    }

    /// <summary>Full draw of one year: the original calendar with the small tournaments moved, plus torneos.json.</summary>
    private (byte[] Year, byte[] Stage, List<string> Summary, Dictionary<int, Entry>[] Auto) DrawYear(int yearIndex)
    {
        var year = (byte[])_originalYear.Clone();
        var stage = (byte[])_originalStage.Clone();
        var summary = new List<string>();
        // Tournaments without "id" of this year, per month: number → tournament.
        var auto = Enumerable.Range(0, 12).Select(m => FixedIdsOfMonth(m).ToDictionary(k => k.Key, _ => (Entry?)null))
            .Select(d => new Dictionary<int, Entry?>(d)).ToArray();

        void Set(int i, int t)
        {
            year[i] = t < 0 ? (byte)0xFF : (byte)t;
            stage[i] = t < 0 ? (byte)0 : _records![t][4]; // stage of the tournament (header byte 4)
        }

        // Codes of the pool: original numbers (0-79), or AutoCode + index in _autoPool.
        const int AutoCode = 1000;
        bool PlaceEntry(int i, Entry e)
        {
            var month = i / 24;
            // Room for its fixed participants in the free slots of that month.
            // (the tournaments with "id" only count in the months where their date in "calendario" is)
            var inMonth = auto[month].Values.Where(x => x != null).Select(x => x!)
                .Concat(FixedIdsOfMonth(month).Where(k => _fixedCells.Any(c => c.Value == k.Key && c.Key / 24 == month)).Select(k => k.Value));
            if (FixedCount(inMonth.Append(e)) > FreeSlotCount)
            {
                Set(i, -1);
                return false;
            }

            // A free number not used that month (by torneos.json or by another drawn tournament).
            var id = _freeIds.FirstOrDefault(f => !auto[month].ContainsKey(f) && !Enumerable.Range(0, 24)
                .Any(k => year[month * 24 + k] == f), -1);
            if (id < 0)
            {
                Set(i, -1);
                return false;
            }

            auto[month][id] = e;
            year[i] = (byte)id;
            stage[i] = e.Record[4];
            return true;
        }

        void Place(int i, int code)
        {
            if (code < AutoCode) Set(i, code);
            else PlaceEntry(i, _autoPool[code - AutoCode].E);
        }

        // Fixed dates first (they reserve their number and their participants).
        foreach (var (cell, e) in _dated)
            if (!PlaceEntry(cell, e)) summary.Add($"Sin lugar para \"{e.T.Name}\" el {cell / 24 + 1}/{cell % 4 + 1}");

        if (_config.ShuffleSmall)
        {
            var rng = new Random(Hash(_config.Seed, yearIndex));
            for (var rank = 1; rank < Ranks.Length; rank++) // A..E (S has no small tournaments)
            {
                var cells = Enumerable.Range(0, 12).SelectMany(m => Enumerable.Range(0, 4)
                    .Select(w => (m * Ranks.Length + rank) * 4 + w))
                    .Where(i => !_fixedCells.ContainsKey(i) && _dated.All(d => d.Cell != i)).ToList();
                var small = cells.Where(i => IsSmall(_originalYear[i])).ToList();
                var candidates = cells.Where(i => _originalYear[i] == 0xFF || small.Contains(i)).ToList();
                foreach (var i in small) Set(i, -1);

                // Pool: the original small tournaments of the rank plus the new ones; how many are played is drawn
                // within the range of "cantidades" (without it, as many as in the original).
                var pool = small.Select(i => (int)_originalYear[i]).Distinct().Where(id => !_def!.Exclude.Contains(id))
                    .Concat(_autoPool.Select((n, k) => (n.Rank, Code: AutoCode + k)).Where(n => n.Rank == rank).Select(n => n.Code))
                    .OrderBy(id => id).ToList();
                var (min, max) = _counts.TryGetValue(rank, out var c) ? c : (small.Count, small.Count);
                var count = Math.Min(rng.Next(min, max + 1), candidates.Count);
                var tournaments = new List<int>();
                while (tournaments.Count < count && pool.Count > 0)
                {
                    // Without repeating, unless the pool is smaller than the count.
                    var round = new List<int>(pool);
                    Shuffle(round, rng);
                    tournaments.AddRange(round.Take(count - tournaments.Count));
                }

                Shuffle(candidates, rng);
                var chosen = candidates.Take(tournaments.Count).OrderBy(i => i / 24 * 4 + i % 4).ToList();
                for (var k = 0; k < chosen.Count; k++) Place(chosen[k], tournaments[k]);
                summary.Add($"{Ranks[rank]} ({chosen.Count}): " + string.Join(" ", chosen.Select(i => $"{i / 24 + 1}/{i % 4 + 1}={year[i]:X2}")));
            }
        }

        foreach (var (i, t) in _fixedCells) Set(i, t);
        var drawn = auto.Select(d => d.Where(k => k.Value != null).ToDictionary(k => k.Key, k => k.Value!)).ToArray();
        return (year, stage, summary, drawn);
    }

    /// <summary>Months ahead (including the current one) that already show their draw: in July, January is next year's.</summary>
    private const int MonthsAhead = 7;

    /// <summary>
    ///     The game has a single 12-month calendar. Each month shows the draw of the year in which it is played next,
    ///     within a window: the current month and the 6 after it are upcoming; the 5 before it, the ones already
    ///     played. So in July the next January is already visible, and in January the previous August to December.
    /// </summary>
    private void ServeCalendar(int period)
    {
        if (_def == null || _records == null) return;
        _servedPeriod = period;
        var current = Math.DivRem(period, 12, out var month);
        if (month < 0) { month += 12; current--; }

        var year = new byte[CalendarSize];
        var stage = new byte[CalendarSize];
        var draws = new Dictionary<int, (byte[] Year, byte[] Stage, List<string> Summary, Dictionary<int, Entry>[] Auto)>();
        var shown = _shownYears = new int[12];
        for (var m = 0; m < 12; m++)
        {
            var ahead = (m - month + 12) % 12 < MonthsAhead;
            var y = ahead ? (m >= month ? current : current + 1) : (m < month ? current : current - 1);
            shown[m] = y;
            if (!draws.TryGetValue(y, out var d)) draws[y] = d = DrawYear(y);
            const int monthSize = CalendarSize / 12; // 6 ranks x 4 weeks
            Array.Copy(d.Year, m * monthSize, year, m * monthSize, monthSize);
            Array.Copy(d.Stage, m * monthSize, stage, m * monthSize, monthSize);
        }

        // What each new number means in each month: the fixed ones of torneos.json plus the drawn ones of that year.
        _monthIds = Enumerable.Range(0, 12).Select(m =>
        {
            var map = FixedIdsOfMonth(m);
            foreach (var (id, e) in draws[shown[m]].Auto[m]) map[id] = e;
            return map;
        }).ToArray();
        ApplyMonth(month, period, year.Skip(month * 24).Take(24).Select(b => (int)b).ToHashSet());

        var previousYear = _servedYearTable;
        _servedYearTable = year;
        _servedStageTable = stage;
        UpdateLoadedCalendar(previousYear, year, stage);

        Serve("btl_year.obj", year);
        Serve("btl_stag.obj", stage);

        // The ranch calendar screen (farm_etc_en.obj) has its own copies: text for each tournament, calendar and venues.
        if (_farmEn != null)
            try
            {
                Serve("farm_etc_en.obj", BuildFarmCalendar(_farmEn, _def, _originalYear, year, stage));
            }
            catch (Exception e)
            {
                Log($"farm_etc_en.obj: no se pudo actualizar la pantalla del calendario ({e.Message}).", Color.Orange);
            }

        // Japanese version: only the calendar (its texts are in Japanese).
        if (_farmJp != null)
        {
            var data = (byte[])_farmJp.Clone();
            var at = data.AsSpan().IndexOf(_originalYear);
            if (at >= 0)
            {
                year.CopyTo(data, at);
                Serve("farm_etc.obj", data);
            }
        }

        var months = string.Join(", ", Enumerable.Range(0, 12).GroupBy(m => shown[m])
            .Select(g => $"{string.Join("/", g.Select(m => m + 1))} del {1000 + g.Key}"));
        Log($"Calendario de {month + 1}/{1000 + current}: meses {months}" +
            (_config.ShuffleSmall ? " (torneos chicos sorteados)." : "."));
        if (_config.VerboseLog)
            foreach (var (y, d) in draws.OrderBy(k => k.Key))
            foreach (var line in d.Summary)
                Log($"  {1000 + y} {line}");
    }

    private byte[]? _servedYearTable, _servedStageTable;

    // The game loads farm_etc_en.obj into a fixed buffer when a save is loaded; the town and the ranch calendar
    // build each month's tournaments from the calendar section of that buffer (function 0x4EDA10 reads
    // [0x948F1C] = calendar offset). Static address 0x948F10 (base 0x400000).
    private const int FarmBufferOffset = 0x548F10;

    /// <summary>
    ///     Mid-session year change: the game does not reread the file, so the calendar and venues are written into
    ///     its copy. Only if the buffer still holds the calendar the mod served before (it is not reused for another
    ///     file).
    /// </summary>
    private unsafe void UpdateLoadedCalendar(byte[]? previousYear, byte[] year, byte[] stage)
    {
        if (previousYear == null || previousYear.AsSpan().SequenceEqual(year)) return;
        try
        {
            var buf = (byte*)(_exeBase + FarmBufferOffset);
            if (*(int*)buf != 3) return;
            // The second u32 is a flag: 0 = section offsets as in the file; 1 = the game already turned them into
            // absolute pointers (it does so the first time it uses the calendar).
            var relocated = *(int*)(buf + 4) != 0;
            var calPtr = (nuint)(*(uint*)(buf + 12)) + (relocated ? 0 : (nuint)buf);
            var stagePtr = (nuint)(*(uint*)(buf + 16)) + (relocated ? 0 : (nuint)buf);
            bool Inside(nuint p) => p > (nuint)buf && p + CalendarSize < (nuint)buf + 0x10000;
            if (!Inside(calPtr) || !Inside(stagePtr))
            {
                if (_config.VerboseLog) Log("Calendario cargado en el juego: formato inesperado, no se modifica.");
                return;
            }

            var calendar = new Span<byte>((void*)calPtr, CalendarSize);
            if (!calendar.SequenceEqual(previousYear))
            {
                if (_config.VerboseLog) Log("Calendario cargado en el juego: no coincide, no se modifica.");
                return;
            }

            year.CopyTo(calendar);
            stage.CopyTo(new Span<byte>((void*)stagePtr, CalendarSize));
            Log("Calendario del juego actualizado sin recargar.");
        }
        catch (Exception e)
        {
            Log($"No se pudo actualizar el calendario cargado: {e.Message}", Color.Orange);
        }
    }

    private void OnWeekChange(IWeekChange change)
    {
        if (_def == null || change.NewWeek == uint.MaxValue) return;
        var p = Period(change.NewWeek);
        if (p == _servedPeriod) return;
        try
        {
            ServeCalendar(p);
        }
        catch (Exception e)
        {
            Log($"No se pudo armar el calendario del año: {e.Message}", Color.Red);
        }
    }

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static int Hash(int seed, int year)
    {
        unchecked
        {
            var x = (ulong)(uint)seed << 32 ^ (ulong)(uint)year * 0x9E3779B97F4A7C15UL ^ 0x4C4947414B4F4BUL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return (int)((x ^ (x >> 31)) & 0x7FFFFFFF);
        }
    }

    /// <summary>
    ///     farm_etc_en.obj: container (u32 3, u32 0, 3 section offsets). Section 0 = one text per tournament
    ///     (table of 80 u32 offsets relative to the section; an empty one has the same offset as the next), 1 = calendar,
    ///     2 = venues. For each new tournament, the template's text is copied with the name changed.
    /// </summary>
    private byte[] BuildFarmCalendar(byte[] farm, Definition def, byte[] originalYear, byte[] year, byte[] stage)
    {
        if (BitConverter.ToInt32(farm, 0) != 3) throw new InvalidDataException("formato inesperado");
        var secs = Enumerable.Range(0, 3).Select(s => BitConverter.ToInt32(farm, 8 + s * 4)).ToArray();
        var texts = farm[secs[0]..secs[1]];
        var calendar = farm[secs[1]..secs[2]];
        var stages = farm[secs[2]..];
        if (!calendar.AsSpan(0, CalendarSize).SequenceEqual(originalYear))
            throw new InvalidDataException("el calendario no es el esperado");

        var offsets = Enumerable.Range(0, TournamentCount).Select(i => BitConverter.ToInt32(texts, i * 4)).ToArray();
        var entries = new byte[TournamentCount][];
        for (var i = 0; i < TournamentCount; i++)
            entries[i] = texts[offsets[i]..(i + 1 < TournamentCount ? offsets[i + 1] : texts.Length)];

        byte[] Encode(Tournament t, int? bonusItem)
        {
            var template = entries[t.Template];
            // <D0 80 80 00> name block <D0 80 80 80> rest (grade, format, entries, bonus, prize)
            var marker = new byte[] { 0xD0, 0x80, 0x80, 0x80 };
            var restAt = template.AsSpan().IndexOf(marker);
            if (restAt < 0) throw new InvalidDataException($"texto de la plantilla {t.Template:X2} inesperado");
            var name = t.Name.Trim().ToUpperInvariant();
            var title = new List<byte> { 0xD0, 0x80, 0x80, 0x00 };
            for (var k = 0; k < Math.Max(0, (30 - name.Length) / 2); k++) title.AddRange([0xB5, 0x61]);
            title.AddRange(name.AsMr2().AsBytes().TakeWhile(b => b != 0xFF));
            title.AddRange([0xFD, 0xB5, 0x61, 0xFD, 0xB5, 0x61, 0xFD]);
            var rest = template[restAt..];
            if (t.Prizes.Count > 0) rest = WithPrizeText(rest, t.Prizes[0]);
            // With a list, the calendar does not tell which one it is this year.
            if (BonusNames(t).Count > 1) rest = WithBonusText(rest, "Aleatorio");
            else if (bonusItem is { } item && _itemNames.TryGetValue(item, out var itemName)) rest = WithBonusText(rest, itemName);
            return title.Concat(rest).ToArray();
        }

        // "Prize: 5000G" of the template's text with the new 1st prize.
        static byte[] Mr2(string x) => x.AsMr2().AsBytes().TakeWhile(b => b != 0xFF).ToArray();

        // "Prize:     5000G" (right-aligned) of the template's text with the new 1st prize, keeping the width.
        static byte[] WithPrizeText(byte[] text, int prize)
        {
            var label = Mr2("Prize:");
            var at = text.AsSpan().IndexOf(label);
            if (at < 0) return text;
            var start = at + label.Length;
            var end = start;
            while (end + 1 < text.Length && text[end] == 0xB5 && text[end + 1] != 0x06) end += 2; // up to "G"
            if (end + 1 >= text.Length || text[end] != 0xB5) return text;
            var width = (end - start) / 2;
            var number = prize.ToString();
            var value = Mr2(new string(' ', Math.Max(1, width - number.Length)) + number);
            return text[..start].Concat(value).Concat(text[end..]).ToArray();
        }

        // "Bonus: <item>" line: replaced if the template has one, otherwise added before "Prize:".
        static byte[] WithBonusText(byte[] text, string item)
        {
            var line = Mr2("Bonus: " + item);
            var bonus = text.AsSpan().IndexOf(Mr2("Bonus:"));
            if (bonus >= 0)
            {
                var end = bonus;
                while (end < text.Length && text[end] != 0xFD && text[end] != 0xFF) end += 2;
                return text[..bonus].Concat(line).Concat(text[end..]).ToArray();
            }

            var prize = text.AsSpan().IndexOf(Mr2("Prize:"));
            if (prize < 0) return text;
            return text[..prize].Concat(line).Append((byte)0xFD).Concat(text[prize..]).ToArray();
        }

        // Numbers shared by several tournaments: every text gets the same size (padded with 0xFF after its end), so
        // the calendar screen can swap them in memory depending on the month it shows.
        _textVariants.Clear();
        var uses = Enumerable.Range(0, 12).SelectMany(m => _monthIds[m].Select(k => (Id: k.Key, Month: m + 1, k.Value.T)));
        foreach (var g in uses.GroupBy(u => u.Id))
        {
            var byTournament = g.GroupBy(u => u.T).Select(x => (T: x.Key, Months: x.Select(u => u.Month).ToHashSet(),
                Bytes: Encode(x.Key, BonusOf(x.Key, _shownYears[x.First().Month - 1])))).ToList();
            var size = byTournament.Max(x => x.Bytes.Length);
            var padded = byTournament.Select(x => (x.T, x.Months,
                Bytes: x.Bytes.Concat(Enumerable.Repeat((byte)0xFF, size - x.Bytes.Length)).ToArray())).ToList();
            if (padded.Count > 1) _textVariants[g.Key] = padded.Select(x => (x.Months, x.Bytes)).ToList();
            var active = _active.TryGetValue(g.Key, out var a) ? a.T : null;
            entries[g.Key] = (padded.FirstOrDefault(x => ReferenceEquals(x.T, active)).Bytes ?? padded[0].Bytes);
        }

        var newTexts = new List<byte>(new byte[TournamentCount * 4]);
        for (var i = 0; i < TournamentCount; i++)
        {
            var b = BitConverter.GetBytes(newTexts.Count);
            for (var k = 0; k < 4; k++) newTexts[i * 4 + k] = b[k];
            newTexts.AddRange(entries[i]);
        }

        year.CopyTo(calendar, 0);
        stage.CopyTo(stages, 0);

        var o = new List<byte>(farm[..secs[0]]);
        foreach (var (part, s) in new[] { (newTexts.ToArray(), 0), (calendar, 1), (stages, 2) })
        {
            while (o.Count % 4 != 0) o.Add(0);
            var b = BitConverter.GetBytes(o.Count);
            for (var k = 0; k < 4; k++) o[8 + s * 4 + k] = b[k];
            o.AddRange(part);
        }

        if (_config.VerboseLog) Log("Pantalla del calendario (farm_etc_en.obj): textos, calendario y estadios actualizados.");
        return o.ToArray();
    }

    /// <summary>Records of TAIKAI_FORM_DATA: table of 80 offsets (u16, from the start) and then the records.</summary>
    private static byte[][] ReadRecords(byte[] form)
    {
        var offsets = Enumerable.Range(0, TournamentCount).Select(i => (int)BitConverter.ToUInt16(form, i * 2)).ToArray();
        // The records follow the order of the table; empty entries (e.g. BTL_02) have the same offset as the next one.
        var records = new byte[TournamentCount][];
        for (var i = 0; i < TournamentCount; i++)
        {
            var end = i + 1 < TournamentCount ? offsets[i + 1] : form.Length;
            records[i] = form[offsets[i]..end];
        }

        return records;
    }

    private static byte[] WriteRecords(byte[][] records)
    {
        var o = new List<byte>(new byte[TournamentCount * 2]);
        for (var i = 0; i < TournamentCount; i++)
        {
            var off = (ushort)o.Count;
            o[i * 2] = (byte)off;
            o[i * 2 + 1] = (byte)(off >> 8);
            o.AddRange(records[i]);
        }

        return o.ToArray();
    }

    private void Serve(string name, byte[] data)
    {
        var target = Path.Combine(_outDir, name);
        File.WriteAllBytes(target, data);
        // The game tries Resources\mf2\... first and, if it does not exist, Resources\data\mf2\...
        foreach (var source in new[]
                 {
                     Path.Combine(_objDir, name),
                     Path.Combine(_exeDir, "Resources", "mf2", "data", "obj", name)
                 })
        {
            _redirector!.RemoveRedirect(source);
            _redirector.AddRedirect(source, target);
        }
    }

    /// <summary>
    ///     The DX version draws the banner and the name of each tournament from new\taikai\taikai_logo_XX[_en].png
    ///     and taikai_name_XX[_en].png. The free numbers have no name image and the game crashes when trying to draw
    ///     it, so a new tournament always gets both images (its own or the template's).
    /// </summary>
    /// <summary>
    ///     240x64 PNG with the name in black bold letters on a transparent background (like taikai_name_XX_en.png),
    ///     in one or two lines. Saved in generado/nombres; null if it cannot be made.
    /// </summary>
    private string? GeneratedNameImage(string name)
    {
        try
        {
            var text = name.Trim().ToUpperInvariant();
            var slug = new string(text.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_').ToArray());
            var dir = Path.Combine(_outDir, "nombres");
            var path = Path.Combine(dir, $"{slug}.png");
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(dir);

            using var bmp = new Bitmap(240, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            // One line if it fits; otherwise two lines split at the space closest to the middle; smaller if needed.
            var lines = text;
            using (var probe = new Font("Arial", 27, FontStyle.Bold, GraphicsUnit.Pixel))
                if (g.MeasureString(text, probe).Width > 232 && text.Contains(' '))
                {
                    var mid = text.Length / 2;
                    var cut = Enumerable.Range(0, text.Length).Where(i => text[i] == ' ').OrderBy(i => Math.Abs(i - mid)).First();
                    lines = text[..cut] + Environment.NewLine + text[(cut + 1)..];
                }

            var size = 27f;
            Font font;
            while (true)
            {
                font = new Font("Arial", size, FontStyle.Bold, GraphicsUnit.Pixel);
                var m = g.MeasureString(lines, font, 1000, format);
                if ((m.Width <= 236 && m.Height <= 64) || size <= 12) break;
                font.Dispose();
                size -= 1;
            }

            using (font)
                g.DrawString(lines, font, Brushes.Black, new RectangleF(0, 0, 240, 64), format);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return path;
        }
        catch (Exception e)
        {
            Log($"No se pudo generar la imagen del nombre \"{name}\": {e.Message}", Color.Orange);
            return null;
        }
    }

    private void ServeImages(Tournament t, int id)
    {
        var newDir = Path.Combine(_exeDir, "Resources", "data", "mf2", "data", "new", "taikai");
        string? TemplateImage(string kind)
        {
            foreach (var n in new[] { $"taikai_{kind}_{t.Template:x2}_en.png", $"taikai_{kind}_{t.Template:x2}.png" })
                if (File.Exists(Path.Combine(newDir, n))) return Path.Combine(newDir, n);
            return null;
        }

        foreach (var (kind, own) in new[] { ("logo", t.Banner), ("name", t.NameImage) })
        {
            // Without its own name image, one is generated with the tournament's name (black text, like the originals).
            var image = !string.IsNullOrWhiteSpace(own) ? Path.Combine(_modDir, own)
                : kind == "name" ? GeneratedNameImage(t.Name) ?? TemplateImage(kind) : TemplateImage(kind);
            if (image == null || !File.Exists(image))
            {
                Log($"Torneo {id:X2}: no se encontró la imagen \"{own}\" ({kind}); se usa la de la plantilla.", Color.Orange);
                image = TemplateImage(kind);
                if (image == null) continue;
            }

            // The DX interface (cocos2d) only loads these images if they are inside the game folder:
            // served from the mod folder, the texture is not created and the game crashes. A copy is placed next to
            // the originals, with its own name.
            if (!Path.GetDirectoryName(image)!.Equals(newDir, StringComparison.OrdinalIgnoreCase))
            {
                // One copy per image (the same number can mean several tournaments).
                // Named after the image file (several tournaments can share a name and have different images).
                var slug = new string(Path.GetFileNameWithoutExtension(image).ToLowerInvariant()
                    .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_').ToArray());
                var copy = Path.Combine(newDir, $"kokemon_{kind}_{slug}.png");
                if (!File.Exists(copy) || !File.ReadAllBytes(copy).AsSpan().SequenceEqual(File.ReadAllBytes(image)))
                    File.Copy(image, copy, true);
                image = copy;
            }

            foreach (var name in new[] { $"taikai_{kind}_{id:x2}.png", $"taikai_{kind}_{id:x2}_en.png" })
            foreach (var dir in new[] { newDir, Path.Combine(_exeDir, "Resources", "mf2", "data", "new", "taikai") })
            {
                var source = Path.Combine(dir, name);
                _redirector!.RemoveRedirect(source);
                _redirector.AddRedirect(source, image);
            }

            if (_config.VerboseLog) Log($"Torneo {id:X2}: {(kind == "logo" ? "banner" : "nombre")} → {Path.GetFileName(image)}");
        }
    }

    private void DeleteGenerated()
    {
        try
        {
            if (Directory.Exists(_outDir)) Directory.Delete(_outDir, true);
        }
        catch
        {
            // not critical
        }
    }

    private static readonly string[] CalendarFiles = ["btl_year.obj", "btl_stag.obj", "farm_etc_en.obj", "farm_etc.obj"];

    private void OnLoading(string path)
    {
        // Before the game reads a calendar table: if the year changed (new year, or another save loaded), regenerate.
        if (_def != null && !path.StartsWith(_outDir, StringComparison.OrdinalIgnoreCase) &&
            CalendarFiles.Any(f => path.EndsWith(@"\" + f, StringComparison.OrdinalIgnoreCase)))
        {
            if (_config.VerboseLog) Log($"El juego lee {Path.GetFileName(path)}");
            try
            {
                if (CurrentPeriod() is { } p && p != _servedPeriod) ServeCalendar(p);
            }
            catch (Exception e)
            {
                Log($"No se pudo armar el calendario del año: {e.Message}", Color.Red);
            }
        }

        if (!_config.LogFileReads) return;
        if (!path.Contains(@"\mf2\data\", StringComparison.OrdinalIgnoreCase)) return;
        if (path.Contains(@"\mon\", StringComparison.OrdinalIgnoreCase)) return;
        Log($"Lee: {path[(path.IndexOf(@"\mf2\data\", StringComparison.OrdinalIgnoreCase) + 10)..]}");
    }

    private void Log(string msg, Color? color = null) =>
        _logger.WriteLine($"[{_modConfig.ModId}] {msg}", color ?? _logger.TextColor);
}
