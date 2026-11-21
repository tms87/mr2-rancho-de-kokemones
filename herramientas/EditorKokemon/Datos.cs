namespace EditorKokemon;

/// <summary>Torneo original que sirve de plantilla (formato, estadio, música, trofeo, premio y bonus).</summary>
public sealed record Plantilla(int Id, string Rango, string Nombre, bool Eliminacion, int[] Premios, string Bonus)
{
    public string Formato => Eliminacion ? "Eliminación" : "Todos contra todos";
    public override string ToString() => $"{Nombre} — {Formato}, {Premios[0]}G" + (Bonus != "" ? $", {Bonus}" : "");
}

/// <summary>Datos fijos del juego (no cambian): plantillas, huecos de rivales por liga e ítems.</summary>
public static class Datos
{
    public static readonly string[] Rangos = ["S", "A", "B", "C", "D", "E"];

    public static readonly Plantilla[] Plantillas =
    [
        new(0x08, "S", "Elimination Grade S", false, [9000, 4500], ""),
        new(0x11, "A", "Taurus Cup", false, [5000, 2500], "Taurus' Horn"),
        new(0x13, "A", "IMa Chairman Cup (Spring)", true, [3000, 2000, 1000], "Dino's Tail"),
        new(0x14, "A", "Telomeann Cup", false, [4000, 2000], "Teromeamn"),
        new(0x15, "A", "IMa Chairman Cup (Autumn)", true, [3000, 2000, 1000], "Pure Platina"),
        new(0x19, "B", "Spring Carnival (B)", true, [3000, 2000, 1000], "Disc Chips"),
        new(0x1B, "B", "Papas' Cup (Summer)", false, [4000, 2000], "Pure Gold"),
        new(0x1D, "B", "Summer Carnival", true, [3000, 2000, 1000], "Disc Chips"),
        new(0x1E, "B", "Manseitan Cup", false, [3000, 1500], "Manseitan"),
        new(0x1F, "B", "Papas' Cup (Autumn)", false, [4000, 2000], "Pure Silver"),
        new(0x20, "B", "Torble Port Cup", false, [5000, 2500], "Disc Chips"),
        new(0x21, "B", "Larox Cup", false, [3000, 1500], "Larox"),
        new(0x22, "B", "Sirius Cup", false, [4000, 2000], ""),
        new(0x23, "B", "Poannka Cup", false, [5000, 2500], "Disc Chips"),
        new(0x26, "C", "Spring Carnival (C)", true, [2000, 1300, 700], "Disc Chips"),
        new(0x28, "C", "Crab Cup", false, [3000, 1500], "Crab's Claw"),
        new(0x29, "C", "Torble Sea Cup", false, [3000, 1500], "Star Prune"),
        new(0x2A, "C", "Colart Cup", false, [3000, 1500], "Disc Chips"),
        new(0x2B, "C", "Kasseitan Cup", false, [2000, 1000], "Kasseitan"),
        new(0x2C, "C", "Mandy Cup", false, [3000, 1500], "Disc Chips"),
        new(0x2D, "C", "Torble Univ. Cup", false, [3000, 1500], ""),
        new(0x2E, "C", "Kawrea Cup", false, [3000, 1500], "Disc Chips"),
        new(0x2F, "C", "Troron Cup", false, [2000, 1000], "Troron"),
        new(0x32, "D", "Spring Carnival (D)", true, [1000, 500, 250], "Disc Chips"),
        new(0x34, "D", "Gemini Cup", false, [2000, 1000, 500], "Gemini's Pot"),
        new(0x35, "D", "Nageel Cup", false, [2000, 1000, 500], "Nageel"),
        new(0x36, "D", "Desert Moon Cup", true, [1000, 500, 250], "Disc Chips"),
        new(0x37, "D", "Galoe Cup", false, [2000, 1000, 500], "Disc Chips"),
        new(0x38, "D", "Parepare Cup (Winter)", false, [2000, 1000, 500], ""),
        new(0x39, "D", "Torles Tourney", true, [1000, 500, 250], "Disc Chips"),
        new(0x3A, "D", "Parepare Cup (Spring)", false, [2000, 1000, 500], "Magic Banana"),
        new(0x3D, "E", "Blue Sky Cup", false, [1000, 500, 250], ""),
        new(0x3E, "E", "Monster Pups' Cup", false, [1000, 500, 250], "Disc Chips"),
        new(0x3F, "E", "Artemis Cup", false, [1000, 500, 250], "Sculpture"),
        new(0x40, "E", "Rookie Cup", true, [800, 400, 200], "Disc Chips"),
        new(0x41, "E", "Maple Cup", true, [800, 400, 200], "Disc Chips"),
        new(0x42, "E", "Blizzard Cup", false, [1000, 500, 250], "Disc Chips"),
    ];

    /// <summary>Huecos de rivales de cada liga (los que sortea "Rivales Kokemon" cada semana).</summary>
    public static readonly Dictionary<string, (int Primero, int Cantidad)> Huecos = new()
    {
        ["S"] = (1, 8), ["A"] = (9, 8), ["B"] = (17, 10), ["C"] = (27, 10), ["D"] = (37, 8), ["E"] = (45, 6)
    };

    /// <summary>3 huecos aleatorios repartidos en la liga (como los torneos originales).</summary>
    public static int[] HuecosAleatorios(string rango, int cantidad = 3)
    {
        var (primero, n) = Huecos[rango];
        return Enumerable.Range(0, cantidad).Select(k => primero + k * n / cantidad + (k % 2)).Select(x => Math.Min(x, primero + n - 1)).Distinct().ToArray();
    }

    /// <summary>Números de los 38 Disc Chips (todos se llaman igual: cada uno es un disco distinto).</summary>
    public static readonly int[] DiscChips = [.. Enumerable.Range(42, 16), .. Enumerable.Range(146, 22)];

    /// <summary>Ítems que puede dar un torneo (nombres como los escribe el juego).</summary>
    public static readonly string[] Items =
    [
        "Potato", "Fish", "Meat", "Milk", "Cup jelly", "Tablets", "Sculpture", "Gemini's Pot", "Lump of Ice", "Fire Stone",
        "Pure Gold", "Pure Silver", "Pure Platina", "Mango", "Candy", "Smoked Snake", "Apple Cake", "Mint Leaf", "Powder",
        "Sweet Jellies", "Sour Jellies", "Crab's Claw", "Nuts oil", "Star Prune", "Gold Peach", "Silver Peach", "Magic Banana",
        "Irritater", "Griever", "Teromeamn", "Manseitan", "Larox", "Kasseitan", "Troron", "Nageel", "Paradoxine", "Bay Shrimp",
        "Incense", "Shoes", "Rice Cracker", "Olive Oil", "Kaleidoscope", "Torles Water", "Perfume", "Stick", "Bone",
        "Perfume Oil", "Wool Ball", "Cedar Log", "Pile of Meat", "Soil", "Rock Candy", "Ice of Papas", "Grease", "DNA Capsule",
        "God Slate", "Hero Badge", "Heel Badge", "Quack Doll", "Dragon Tusk", "Old Sheath", "Double-edged", "Magic Pot", "Mask",
        "Big Footstep", "Big Boots", "Fire Feather", "Taurus' Horn", "Dino's Tail", "Zilla Beard", "Fun Can", "Strong Glue",
        "Mystic Seed", "Parepare Tea", "Match", "Toothpick", "Playmate", "Whetstone", "Polish", "Silk Cloth", "Disc Dish",
        "Gramophone", "Shiny Stone", "Meteorite", "Steamed Bun", "Razor Blade", "Ice Candy", "Fish Bone", "Sun Lamp", "Silk Hat",
        "Half Cake", "Shaved Ice", "Sweet Potato", "Medal", "Gold Medal", "Silver Medal", "Music Box", "Medallion", "Mirror",
        "Colart Tea", "Galoe Nut", "Stick of Ice", "Ocean Stone", "Seaweed", "Clay Doll", "Colt's Cake", "Flower", "Gali Mask",
        "Crystal", "Undine Slate", "Spear", "Wracky Doll", "Disc Chips"
    ];

    /// <summary>Comparación de nombres de ítems como la hace Liga Kokemon (solo letras y números).</summary>
    public static string Clave(string nombre) => new string(nombre.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static bool ItemValido(string item) =>
        int.TryParse(item, out var n) ? n is >= 0 and < 178 : Items.Any(i => Clave(i) == Clave(item));
}
