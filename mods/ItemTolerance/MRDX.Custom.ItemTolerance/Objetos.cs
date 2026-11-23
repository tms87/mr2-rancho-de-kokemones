using System.Diagnostics;
using MRDX.Base.Mod.Interfaces;

namespace MRDX.Custom.ItemTolerance;

/// <summary>Qué tipo de objeto se dio (Nuts Oil y Magic Banana tienen tolerancia; el resto, efectos propios).</summary>
public enum Tipo
{
    Otro,
    NutsOil,
    MagicBanana
}

/// <summary>
///     Efectos que el mod agrega a otros objetos (los efectos originales del juego se mantienen):
///     - Desintoxicación: baja la cuenta de Nuts Oil y Magic Banana comidas en su vida (la que sube el riesgo de vida).
///     - Vida: reemplaza la vida que el objeto le saca por una cantidad al azar (semanas, ambos extremos incluidos).
/// </summary>
public sealed record Efecto(string Nombre, Func<Random, int, int>? Desintoxicar = null, int VidaMin = 0, int VidaMax = 0)
{
    public bool CambiaVida => VidaMax > 0;

    /// <summary>
    ///     Semanas de vida que resta, sesgadas por la parte de la vida que ya consumió (0 = recién nacido, 1 = al final):
    ///     de joven salen más las restas chicas, a mitad de vida es parejo y de viejo salen más las grandes.
    ///     Se sortea x = u^a con a = 2^(1 - 2·vivida): a = 2 (joven, promedio 1/3 del rango), 1 (mitad, 1/2), 0,5 (viejo, 2/3).
    /// </summary>
    public int RestaVida(Random r, double vivida)
    {
        var a = Math.Pow(2, 1 - 2 * Math.Clamp(vivida, 0, 1));
        var x = Math.Pow(r.NextDouble(), a);
        return Math.Min(VidaMax, VidaMin + (int)Math.Floor(x * (VidaMax - VidaMin + 1)));
    }
}

public static class Objetos
{
    // Clave: el nombre del objeto con solo letras y números, en minúsculas ("Half—Eaten" → "halfeaten").
    private static readonly Dictionary<string, Efecto> Efectos = new()
    {
        // Quack Doll: 50% desintoxicación total, 50% la mitad.
        ["quackdoll"] = new("Quack Doll", (r, c) => r.Next(2) == 0 ? 0 : c / 2),
        ["rockcandy"] = new("Rock Candy", (r, c) => r.Next(100) < 50 ? c - 3 : c),
        ["irritater"] = new("Irritater", (r, c) => r.Next(100) < 30 ? c - 1 : c),
        ["griever"] = new("Griever", (r, c) => r.Next(100) < 30 ? c - 1 : c),
        ["halfeaten"] = new("Half Eaten", (_, c) => c - 1),
        ["goldpeach"] = new("Gold Peach", (_, c) => c - 3),
        ["silverpeach"] = new("Silver Peach", (_, c) => c - 1),

        ["paradoxine"] = new("Paradoxine", VidaMin: 5, VidaMax: 20),
        ["teromeamn"] = new("Teromeamn", VidaMin: 5, VidaMax: 21),
        ["troron"] = new("Troron", VidaMin: 2, VidaMax: 6),
        ["larox"] = new("Larox", VidaMin: 0, VidaMax: 4),
        ["manseitan"] = new("Manseitan", VidaMin: 0, VidaMax: 5),
        ["kasseitan"] = new("Kasseitan", VidaMin: 10, VidaMax: 22),
        ["nageel"] = new("Nageel", VidaMin: 10, VidaMax: 22)
    };

    private static Dictionary<int, string>? _nombres;

    public static string Clave(string nombre) => new(nombre.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    ///     Nombres de los objetos por número, de la tabla del juego (taikai_en.flk, sección 5), como Liga Kokemon.
    ///     Hay objetos con varios números (Quack Doll, Nuts Oil, Magic Banana): por nombre se reconocen todos.
    /// </summary>
    public static string? NombreDe(int id)
    {
        if (_nombres == null)
        {
            _nombres = new Dictionary<int, string>();
            try
            {
                var dir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!;
                var flk = Path.Combine(dir, "Resources", "data", "mf2", "data", "taikai", "taikai_en.flk");
                var b = File.ReadAllBytes(flk);
                var sec = BitConverter.ToInt32(b, 8 + 5 * 4);
                var count = BitConverter.ToInt32(b, sec) / 4;
                for (var i = 0; i < count; i++)
                {
                    var at = sec + BitConverter.ToInt32(b, sec + i * 4);
                    var end = at;
                    while (end + 1 < b.Length && b[end] != 0xFF) end += 2;
                    try
                    {
                        var n = b[at..(end + 1)].AsShorts().AsString().Trim();
                        if (n.Length > 0) _nombres[i] = n;
                    }
                    catch
                    {
                        // nombre que el decodificador no conoce: se ignora
                    }
                }
            }
            catch
            {
                // sin la tabla, solo se reconocen Nuts Oil y Magic Banana por su número de la configuración
            }
        }

        return _nombres.GetValueOrDefault(id);
    }

    public static Efecto? EfectoDe(int id) => NombreDe(id) is { } n ? Efectos.GetValueOrDefault(Clave(n)) : null;

    public static Tipo TipoDe(int id, int nutsOilId, int magicBananaId)
    {
        if (id == nutsOilId) return Tipo.NutsOil;
        if (id == magicBananaId) return Tipo.MagicBanana;
        return NombreDe(id) is { } n
            ? Clave(n) switch { "nutsoil" => Tipo.NutsOil, "magicbanana" => Tipo.MagicBanana, _ => Tipo.Otro }
            : Tipo.Otro;
    }
}
