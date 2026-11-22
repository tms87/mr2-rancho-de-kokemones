using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EditorKokemon;

/// <summary>
///     Un monstruo tal como está en una grabación: registro de 0x20C bytes (8 de cabecera + el monstruo).
///     Offsets del monstruo como en memoria (exe+0x37667C): razas 0/4, stats 8..0x12, nombre 0x168, rango 0x18A.
/// </summary>
public sealed class MonstruoGuardado
{
    public const int Tamano = 0x20C;
    private const int Ini = 8; // el monstruo empieza después de la cabecera

    public required byte[] Crudo { get; init; }
    public string Lugar { get; init; } = "";

    public int Principal => BitConverter.ToInt32(Crudo, Ini + 0x00);
    public int Sub => BitConverter.ToInt32(Crudo, Ini + 0x04);
    public bool Vacio => Principal == 0x2E || Crudo[Ini + 0x168] == 0xFF;
    public string Raza => $"{Datos.NombreRaza(Principal)}/{Datos.NombreRaza(Sub)}";
    public string Rango => Crudo[Ini + 0x18A] is var r && r < 6 ? "EDCBAS"[r].ToString() : "?";
    public int Total => Enumerable.Range(0, 6).Sum(i => BitConverter.ToUInt16(Crudo, Ini + 0x08 + i * 2));

    public string Nombre
    {
        get
        {
            var sb = new StringBuilder();
            for (var i = Ini + 0x168; i < Ini + 0x168 + 24 && Crudo[i] != 0xFF;)
            {
                var b = Crudo[i];
                if (b is >= 0xA2 and <= 0xAB) { sb.Append((char)('0' + b - 0xA2)); i++; continue; }
                var c = Crudo[i + 1];
                sb.Append(c switch
                {
                    <= 0x19 => (char)('A' + c),
                    <= 0x33 => (char)('a' + c - 0x1A),
                    >= 0x57 and <= 0x60 => (char)('0' + c - 0x57),
                    _ => '?'
                });
                i += 2;
            }

            return sb.ToString().Trim();
        }
    }

    public string NombreArchivo()
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var n = new string(Nombre.Where(c => !invalidos.Contains(c) && c != '?').ToArray()).Trim();
        return $"{(n == "" ? "monstruo" : n)}_{Datos.NombreRaza(Principal)}-{Datos.NombreRaza(Sub)}.json";
    }

    public JsonObject AJson(string? origen = null) => new()
    {
        ["nombre"] = Nombre,
        ["raza"] = Raza,
        ["rango"] = Rango,
        ["total"] = Total,
        ["origen"] = origen ?? Lugar,
        ["crudo"] = Convert.ToBase64String(Crudo)
    };

    public static MonstruoGuardado DeJson(JsonObject o, string lugar = "") => new()
    {
        Crudo = Convert.FromBase64String((string?)o["crudo"] ?? ""),
        Lugar = lugar
    };
}

/// <summary>Lectura de las grabaciones del juego (Documentos\KoeiTecmo\mfdx_en\bu00). Solo lectura.</summary>
public static class Grabaciones
{
    public static string Carpeta => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "KoeiTecmo", "mfdx_en", "bu00");

    public static string Archivo(int ranura) => Path.Combine(Carpeta, $"BISLPS-01906{ranura - 1:00}");

    /// <summary>Ranuras 1..15 con su fecha ("1020, mes 8, sem. 1"), o null si no existe.</summary>
    public static string? Fecha(int ranura)
    {
        var f = Archivo(ranura);
        if (!File.Exists(f)) return null;
        var d = File.ReadAllBytes(f);
        int? anio = null, mes = null, sem = null;
        var num = 0;
        var dig = 0;
        for (var i = 0; i < Math.Min(d.Length - 1, 0x80); i++)
        {
            if (d[i] == 0x82 && d[i + 1] is >= 0x4F and <= 0x58) { num = num * 10 + d[i + 1] - 0x4F; dig++; i++; continue; }
            if (dig > 0)
            {
                if (d[i] == 0x94 && d[i + 1] == 0x4E) anio = num;
                else if (d[i] == 0x8C && d[i + 1] == 0x8E) mes = num;
                else if (d[i] == 0x8F && d[i + 1] == 0x54) sem = num;
            }

            num = 0;
            dig = 0;
        }

        return anio == null ? "" : $"año {anio}, mes {mes}, sem. {sem}";
    }

    /// <summary>El monstruo del rancho y los 20 del congelador (solo los ocupados).</summary>
    public static List<MonstruoGuardado> Monstruos(int ranura)
    {
        var d = File.ReadAllBytes(Archivo(ranura));
        var res = new List<MonstruoGuardado>();
        void Agregar(int off, string lugar)
        {
            var m = new MonstruoGuardado { Crudo = d.AsSpan(off, MonstruoGuardado.Tamano).ToArray(), Lugar = lugar };
            if (!m.Vacio) res.Add(m);
        }

        Agregar(0x3188, "rancho");
        for (var k = 0; k < 20; k++) Agregar(0x20C + k * MonstruoGuardado.Tamano, $"congelador {k + 1}");
        return res;
    }

    public static JsonSerializerOptions Json { get; } = new()
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
