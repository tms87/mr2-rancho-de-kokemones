using System.Text.Json;
using MRDX.Base.Mod.Interfaces;

namespace MRDX.Custom.ItemTolerance;

/// <summary>Datos que el mod recuerda de cada monstruo.</summary>
public class MonsterRecord
{
    /// <summary>Puntos de tolerancia por clave (ID de objeto, o SharedKey si es compartida).</summary>
    public Dictionary<int, int> Tolerance { get; set; } = new();

    /// <summary>Nuts Oil + Magic Banana ingeridas en toda la vida del monstruo.</summary>
    public int LifetimeCount { get; set; }

    /// <summary>Por comida nueva (nombre): meses seguidos que la comió (sube la chance de desintoxicar).</summary>
    public Dictionary<string, int> ConstanciaComidas { get; set; } = new();

    /// <summary>Por comida nueva (nombre): último mes (del contador de semanas) en que la comió.</summary>
    public Dictionary<string, long> MesesComidas { get; set; } = new();
}

/// <summary>Datos de una ranura de guardado: un registro por monstruo.</summary>
public class SlotData
{
    public Dictionary<string, MonsterRecord> Monsters { get; set; } = new();

    /// <summary>Baúl de disc chips de la partida: número de objeto → cantidad guardada.</summary>
    public Dictionary<int, int> Baul { get; set; } = new();

    public static string KeyFor(IMonster m) =>
        $"{m.Name}|{(int)m.GenusMain}|{(int)m.GenusSub}|{m.InitalLifespan}";

    public MonsterRecord For(IMonster m)
    {
        var key = KeyFor(m);
        if (!Monsters.TryGetValue(key, out var rec))
        {
            rec = new MonsterRecord();
            Monsters[key] = rec;
        }

        return rec;
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static SlotData Load(string path)
    {
        if (!File.Exists(path)) return new SlotData();
        return JsonSerializer.Deserialize<SlotData>(File.ReadAllText(path), Options) ?? new SlotData();
    }

    public void Save(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, true);
    }
}
