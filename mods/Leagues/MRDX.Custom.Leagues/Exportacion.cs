using System.Text.Json;
using MRDX.Base.Mod.Interfaces;

namespace MRDX.Custom.Leagues;

/// <summary>
///     Exportación de monstruos, sin nada de Reloaded: la usan el mod (desde la memoria del juego, al guardar) y el
///     exportador manual del paquete (desde un archivo de grabación). Un monstruo es un registro de 0x20C bytes: 8 de
///     cabecera y el monstruo (como el del rancho en exe+0x37667C), igual en memoria y en la grabación.
/// </summary>
public static class Exportacion
{
    public const int Registro = 0x20C, Cabecera = 8;

    // Monstruo (desde el final de la cabecera):
    private const int OffGenusMain = 0x00, OffGenusSub = 0x04;
    private const int OffLife = 0x08, OffPower = 0x0A, OffDefense = 0x0C, OffSkill = 0x0E, OffSpeed = 0x10, OffIntelligence = 0x12;
    private const int OffNatureRaw = 0x1C, OffSpoil = 0x24, OffFear = 0x25, OffNatureBase = 0x41;
    private const int NameOffset = 0x168; // 12 caracteres x 2 bytes
    private const int LearnedTechsOffset = 0x192; // 24 posiciones x 2 bytes
    private const int OffRank = 0x18A;    // 0 = E ... 4 = A, 5 = S
    private const int OffArenaSpeed = 0x1CE, OffGutsRate = 0x1CF, OffBattleSpecials = 0x1D0;

    // Grabación (archivo BISLPS-01906NN): congelador (20 registros) desde 0x20C, el monstruo del rancho en 0x3188,
    // entrenador en 0x2B06 y la fecha en la cabecera.
    public const int CongeladorEnGrabacion = 0x20C, RanchoEnGrabacion = 0x3188, Congelador = 20;
    private const int EntrenadorEnGrabacion = 0x2B06;

    /// <summary>Códigos de caracteres de nombres que el decodificador no conoce (se avisan después de exportar).</summary>
    public static readonly HashSet<string> CaracteresDesconocidos = [];

    /// <summary>El monstruo de un registro de 0x20C bytes, o null si el lugar está vacío.</summary>
    public static LeagueMonster? Monstruo(ReadOnlySpan<byte> r, string donde)
    {
        if (r.Length < Registro) return null;
        var p = r[Cabecera..];
        if (p[NameOffset] == 0xFF) return null; // lugar vacío
        static ushort U16(ReadOnlySpan<byte> s, int o) => BitConverter.ToUInt16(s.Slice(o, 2));
        var main = BitConverter.ToInt32(p.Slice(OffGenusMain, 4));
        var sub = BitConverter.ToInt32(p.Slice(OffGenusSub, 4));
        var life = U16(p, OffLife);
        if (main is < 0 or > 45 || sub is < 0 or > 45 || life == 0) return null;

        var techs = new List<int>();
        for (var i = 0; i < 24; i++)
            if (p[LearnedTechsOffset + i * 2] != 0) techs.Add(i);

        var rank = p[OffRank];
        return new LeagueMonster
        {
            Name = Nombre(p.Slice(NameOffset, 24)) ?? "Rival",
            Origin = $"exportado ({donde})",
            // Rango alcanzado en el juego; si no se sabe, la liga sale del total de stats al cargarlo.
            League = rank < LeagueMonster.Leagues.Length ? LeagueMonster.Leagues[^(rank + 1)] : (rank >= 5 ? "S" : ""),
            GenusMain = (MonsterGenus)main, GenusSub = (MonsterGenus)sub,
            Life = life,
            Power = U16(p, OffPower), Intelligence = U16(p, OffIntelligence),
            Skill = U16(p, OffSkill), Speed = U16(p, OffSpeed), Defense = U16(p, OffDefense),
            Nature = (sbyte)Math.Clamp((sbyte)p[OffNatureBase] + MonsterHelper.NatureRawToMod(BitConverter.ToInt16(p.Slice(OffNatureRaw, 2))), -100, 100),
            Fear = p[OffFear], Spoil = p[OffSpoil],
            Techniques = techs,
            ArenaSpeed = p[OffArenaSpeed], GutsRate = p[OffGutsRate],
            BattleSpecials = U16(p, OffBattleSpecials),
            Place = donde,
            Raw = Convert.ToBase64String(r[..Registro])
        };
    }

    /// <summary>Los monstruos de un archivo de grabación (rancho y congelador), con su entrenador y fecha.</summary>
    public static ExportedSave DeGrabacion(byte[] d)
    {
        var res = new ExportedSave { Trainer = Entrenador(d), Date = Fecha(d) ?? "" };
        if (d.Length >= RanchoEnGrabacion + Registro && Monstruo(d.AsSpan(RanchoEnGrabacion, Registro), "rancho") is { } m)
            res.Monsters.Add(m);
        for (var i = 0; i < Congelador; i++)
        {
            var o = CongeladorEnGrabacion + i * Registro;
            if (d.Length >= o + Registro && Monstruo(d.AsSpan(o, Registro), $"congelador {i + 1}") is { } c) res.Monsters.Add(c);
        }

        return res;
    }

    /// <summary>
    ///     Nombre de un monstruo. En DX (inglés) cada carácter va con el prefijo 0xB0, mientras que el archivo de torneos
    ///     y el Base Mod usan 0xB5 (mismo código), así que el Base Mod lo decodifica mal (表表表...).
    /// </summary>
    public static string? Nombre(ReadOnlySpan<byte> p)
    {
        // Las letras ocupan 2 bytes (prefijo + código); los dígitos escritos en la pantalla del nombre, 1 byte:
        // 0xA2 = '0' ... 0xAB = '9' (un monstruo llamado "99" es AB AB). Todo pasa a la forma de 2 bytes del
        // decodificador (B5 57 = '0').
        var raw = Enumerable.Repeat((byte)0xFF, 26).ToArray();
        var at = 0;
        for (var i = 0; i < 24 && i < p.Length && at < 24;)
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

            if (i + 1 >= p.Length) break;
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
            // Algún carácter que el decodificador no conoce (símbolos, acentos): uno por uno, los desconocidos como "?".
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
                    CaracteresDesconocidos.Add($"{raw[i * 2]:X2}{raw[i * 2 + 1]:X2}");
                }
            }

            name = sb.ToString().Trim();
        }

        return name.Length > 0 && !name.Contains('表') ? name : null;
    }

    /// <summary>Entrenador de una grabación (0x2B06: como los nombres de monstruo, B0 + código, fin 0xFF).</summary>
    public static string? Entrenador(byte[] d)
    {
        if (d.Length < EntrenadorEnGrabacion + 26) return null;
        var sb = new System.Text.StringBuilder();
        for (var i = EntrenadorEnGrabacion; i < EntrenadorEnGrabacion + 24 && d[i] != 0xFF;)
        {
            if (d[i] is >= 0xA2 and <= 0xAB) { sb.Append((char)('0' + d[i] - 0xA2)); i++; continue; }
            var c = d[i + 1];
            sb.Append(c <= 0x19 ? (char)('A' + c) : c <= 0x33 ? (char)('a' + c - 0x1A) : c is >= 0x57 and <= 0x60 ? (char)('0' + c - 0x57) : '?');
            i += 2;
        }

        var name = sb.ToString().Trim();
        return name.Length > 0 ? name : null;
    }

    /// <summary>Fecha de la partida ("1020-08-1") de la cabecera de una grabación, o null.</summary>
    public static string? Fecha(byte[] d)
    {
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

        return year != null && month != null && week != null ? $"{year}-{month:00}-{week}" : null;
    }

    public static string NombreArchivo(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string((name ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return s.Length > 0 ? s : "monstruo";
    }
}

/// <summary>
///     Carpeta de exportados: un archivo por grabación, "año-mes-semana Entrenador.json", con todos sus monstruos (datos
///     de batalla legibles y el monstruo completo, "crudo"). Si el conjunto de monstruos es igual al de un archivo
///     anterior, no se repite.
/// </summary>
public sealed class CarpetaExportados(string dir)
{
    private HashSet<string>? _hechos; // huella de los monstruos de cada archivo ya exportado

    public string Dir => dir;

    private static string Huella(IEnumerable<LeagueMonster> monsters) =>
        string.Join("|", monsters.Select(m => m.Raw).Order(StringComparer.Ordinal));

    /// <summary>Escribe el archivo de la grabación. Devuelve su ruta, o null si ya había uno igual (o no hay monstruos).</summary>
    public string? Escribir(ExportedSave save)
    {
        if (save.Monsters.Count == 0) return null;
        Directory.CreateDirectory(dir);
        if (_hechos == null)
        {
            _hechos = [];
            foreach (var f in Directory.GetFiles(dir, "*.json"))
                try
                {
                    var old = JsonSerializer.Deserialize<ExportedSave>(File.ReadAllText(f), LeagueMonster.Json);
                    if (old?.Monsters is { Count: > 0 }) _hechos.Add(Huella(old.Monsters));
                }
                catch
                {
                    // exportados del formato anterior (un monstruo por archivo) u otros: se ignoran
                }
        }

        if (!_hechos.Add(Huella(save.Monsters))) return null;

        var baseName = $"{save.Date} {Exportacion.NombreArchivo(save.Trainer ?? "Entrenador")}";
        var path = Path.Combine(dir, baseName + ".json");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(dir, $"{baseName} ({n}).json");
        File.WriteAllText(path, JsonSerializer.Serialize(save, LeagueMonster.Json));
        return path;
    }
}
