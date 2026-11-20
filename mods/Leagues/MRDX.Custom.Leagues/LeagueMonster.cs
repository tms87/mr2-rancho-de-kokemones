using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MRDX.Base.Mod.Interfaces;

namespace MRDX.Custom.Leagues;

/// <summary>
///     A league monster, as stored in the JSON files (one per monster). Contains exactly the fields of a
///     tournament rival record (60 bytes in taikai_en.flk).
/// </summary>
public sealed class LeagueMonster
{
    public static readonly string[] Leagues = ["S", "A", "B", "C", "D", "E"];

    [JsonPropertyName("nombre")] public string Name { get; set; } = "";
    /// <summary>S, A, B, C, D or E. Empty: decided by stat total (see LeagueForTotal).</summary>
    [JsonPropertyName("liga")] public string League { get; set; } = "";
    [JsonPropertyName("activo")] public bool Active { get; set; } = true;
    [JsonPropertyName("origen")] public string Origin { get; set; } = "";
    [JsonPropertyName("autor")] public string Author { get; set; } = "";

    [JsonPropertyName("razaPrincipal")] public MonsterGenus GenusMain { get; set; }
    [JsonPropertyName("razaSub")] public MonsterGenus GenusSub { get; set; }

    [JsonPropertyName("vida")] public ushort Life { get; set; }
    [JsonPropertyName("poder")] public ushort Power { get; set; }
    [JsonPropertyName("inteligencia")] public ushort Intelligence { get; set; }
    [JsonPropertyName("habilidad")] public ushort Skill { get; set; }
    [JsonPropertyName("velocidad")] public ushort Speed { get; set; }
    [JsonPropertyName("defensa")] public ushort Defense { get; set; }

    [JsonPropertyName("naturaleza")] public sbyte Nature { get; set; }
    [JsonPropertyName("miedo")] public byte Fear { get; set; }
    [JsonPropertyName("malcriado")] public byte Spoil { get; set; }

    /// <summary>Technique positions (0-5 melee, 6-11 short, 12-17 medium, 18-23 long range).</summary>
    [JsonPropertyName("tecnicas")] public List<int> Techniques { get; set; } = [];

    [JsonPropertyName("velocidadArena")] public byte ArenaSpeed { get; set; }
    [JsonPropertyName("agallas")] public byte GutsRate { get; set; }
    [JsonPropertyName("especiales")] public ushort BattleSpecials { get; set; }

    [JsonIgnore] public string File { get; set; } = "";
    [JsonIgnore] public int StatTotal => Life + Power + Intelligence + Skill + Speed + Defense;

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>League by stat total, for monsters without a league (for example, ones created by hand).</summary>
    public static string LeagueForTotal(int total) => total switch
    {
        >= 2800 => "S",
        >= 2000 => "A",
        >= 1550 => "B",
        >= 1150 => "C",
        >= 850 => "D",
        _ => "E"
    };

    public static LeagueMonster FromRecord(byte[] r)
    {
        var d = IBattleMonsterData.FromBytes(r);
        var techs = new List<int>();
        for (var i = 0; i < 24; i++)
            if ((d.TechsRaw & (1u << i)) != 0) techs.Add(i);
        return new LeagueMonster
        {
            Name = d.Name.Trim(),
            GenusMain = d.GenusMain,
            GenusSub = d.GenusSub,
            Life = d.Life, Power = d.Power, Intelligence = d.Intelligence,
            Skill = d.Skill, Speed = d.Speed, Defense = d.Defense,
            Nature = d.Nature, Fear = d.Fear, Spoil = d.Spoil,
            Techniques = techs,
            ArenaSpeed = d.ArenaSpeed, GutsRate = d.GutsRate,
            BattleSpecials = (ushort)d.BattleSpecial
        };
    }

    public byte[] ToRecord()
    {
        uint techs = 0;
        foreach (var t in Techniques)
            if (t is >= 0 and < 24) techs |= 1u << t;

        IBattleMonsterData d = new BattleMonsterData
        {
            Name = SafeName(Name),
            GenusMain = GenusMain, GenusSub = GenusSub,
            Life = Clamp(Life), Power = Clamp(Power), Intelligence = Clamp(Intelligence),
            Skill = Clamp(Skill), Speed = Clamp(Speed), Defense = Clamp(Defense),
            Nature = Nature, Fear = Fear, Spoil = Spoil,
            Techs = BitConverter.GetBytes(techs)[..3],
            TechsRaw = techs,
            ArenaSpeed = ArenaSpeed, GutsRate = GutsRate,
            BattleSpecial = (BattleSpecials)BattleSpecials
        };
        var o = d.Serialize();
        if (o.Length != 60) throw new InvalidDataException($"registro de {o.Length} bytes");
        return o;
    }

    private static ushort Clamp(ushort v) => Math.Clamp(v, (ushort)1, (ushort)999);

    /// <summary>Name that fits in the record (12 characters) and that the game alphabet can write.</summary>
    public static string SafeName(string name)
    {
        var n = (name ?? "").Trim();
        if (n.Length > 12) n = n[..12];
        try
        {
            if (n.AsMr2().AsBytes().Length <= 26) return n;
        }
        catch
        {
            // characters the game does not have: keep letters, numbers and spaces only
        }

        var clean = new string(n.Where(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or ' ').ToArray());
        return clean.Length > 0 ? clean : "Rival";
    }

    [JsonIgnore] public string Label =>$"{Name} ({GenusMain}/{GenusSub}, {StatTotal})";
}
