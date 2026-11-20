using System.Text.Json;
using MRDX.Base.Mod.Interfaces;

namespace MRDX.Custom.ItemTolerance;

/// <summary>Datos que el mod recuerda de cada monstruo.</summary>
public class MonsterRecord
{
    /// <summary>Puntos de tolerancia por clave (ID de objeto, o SharedKey si es compartida).</summary>
    public Dictionary<int, int> Tolerance { get; set; } = new();

    /// <summary>Claves usadas durante la semana en curso (no se descuentan al pasar la semana).</summary>
    public HashSet<int> UsedThisWeek { get; set; } = new();

    /// <summary>Nuts Oil + Magic Banana ingeridas en toda la vida del monstruo.</summary>
    public int LifetimeCount { get; set; }
}

/// <summary>Datos de una ranura de guardado: un registro por monstruo.</summary>
public class SlotData
{
    public Dictionary<string, MonsterRecord> Monsters { get; set; } = new();

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
