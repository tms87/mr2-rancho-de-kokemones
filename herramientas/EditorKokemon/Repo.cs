using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EditorKokemon;

/// <summary>Un monstruo de la lista de rivales (un .json de Rivales Kokemon).</summary>
public sealed class Rival
{
    public required string Archivo { get; init; }
    public required JsonObject Datos { get; init; }

    public string Nombre { get => (string?)Datos["nombre"] ?? ""; set => Datos["nombre"] = value; }
    public string Liga { get => ((string?)Datos["liga"] ?? "").Trim().ToUpperInvariant(); set => Datos["liga"] = value; }
    public string Raza => $"{Datos["razaPrincipal"]}/{Datos["razaSub"]}";
    public int Especiales => (int?)Datos["especiales"] ?? 0;

    public int Total => new[] { "vida", "poder", "inteligencia", "habilidad", "velocidad", "defensa" }
        .Sum(k => (int?)Datos[k] ?? 0);

    /// <summary>Nombre de archivo como lo arma Rivales Kokemon: Nombre_Principal-Sub.json.</summary>
    public string NombreArchivo()
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var n = new string(Nombre.Where(c => !invalidos.Contains(c)).ToArray()).Trim();
        return $"{(n == "" ? "monstruo" : n)}_{Datos["razaPrincipal"]}-{Datos["razaSub"]}.json";
    }

    public static Rival Leer(string archivo) => new()
    {
        Archivo = archivo,
        Datos = JsonNode.Parse(File.ReadAllText(archivo))!.AsObject()
    };

    public void Guardar(string archivo) =>
        File.WriteAllText(archivo, Datos.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n", new UTF8Encoding(false));
}

/// <summary>La copia local del repositorio y las carpetas de contenido.</summary>
public sealed class Repo
{
    public string Raiz { get; }
    public string Jugadores => Path.Combine(Raiz, @"mods\Leagues\MRDX.Custom.Leagues\Liga\jugadores");
    public string Originales => Path.Combine(Raiz, @"mods\Leagues\MRDX.Custom.Leagues\Liga\originales");
    public string Torneos => Path.Combine(Raiz, @"mods\LigaKokemon\MRDX.Custom.LigaKokemon\torneos.json");
    public string Imagenes => Path.Combine(Raiz, @"mods\LigaKokemon\MRDX.Custom.LigaKokemon\imagenes");

    /// <summary>Carpetas y archivos que sube el botón "Subir" (solo contenido).</summary>
    public string[] Contenido =>
    [
        @"mods/Leagues/MRDX.Custom.Leagues/Liga",
        @"mods/LigaKokemon/MRDX.Custom.LigaKokemon/torneos.json",
        @"mods/LigaKokemon/MRDX.Custom.LigaKokemon/imagenes"
    ];

    public Repo(string raiz) => Raiz = raiz;

    /// <summary>Busca la raíz del repositorio (la carpeta con actualizacion.json) desde la carpeta del programa.</summary>
    public static string? Buscar(string desde)
    {
        for (var d = new DirectoryInfo(desde); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "actualizacion.json"))) return d.FullName;
        return null;
    }

    public List<Rival> LeerRivales(string carpeta) =>
        Directory.Exists(carpeta)
            ? Directory.GetFiles(carpeta, "*.json").Select(f =>
            {
                try { return Rival.Leer(f); }
                catch { return null; }
            }).OfType<Rival>().ToList()
            : [];

    /// <summary>Rango de suma de stats de los rivales originales de cada liga.</summary>
    public Dictionary<string, (int Min, int Max)> RangosOriginales()
    {
        var res = new Dictionary<string, (int, int)>();
        foreach (var g in LeerRivales(Originales).GroupBy(r => r.Liga))
            res[g.Key] = (g.Min(r => r.Total), g.Max(r => r.Total));
        return res;
    }
}

/// <summary>
///     torneos.json: se conservan los comentarios del principio (la explicación de cada campo) y el resto se escribe
///     con un formato fijo (listas cortas en una línea).
/// </summary>
public sealed class ArchivoTorneos
{
    public string Encabezado { get; private set; } = "{\n";
    public JsonObject Raiz { get; private set; } = new();
    public JsonArray Torneos => Raiz["torneos"] as JsonArray ?? (JsonArray)(Raiz["torneos"] = new JsonArray());

    public static ArchivoTorneos Leer(string archivo)
    {
        var texto = File.ReadAllText(archivo).Replace("\r\n", "\n");
        var i = texto.IndexOf("  \"torneos\"", StringComparison.Ordinal);
        var raiz = JsonNode.Parse(texto, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        return new ArchivoTorneos { Encabezado = i > 0 ? texto[..i] : "{\n", Raiz = raiz };
    }

    public void Guardar(string archivo)
    {
        var sb = new StringBuilder(Encabezado);
        var props = Raiz.ToList();
        for (var k = 0; k < props.Count; k++)
        {
            sb.Append($"  \"{props[k].Key}\": ");
            Escribir(sb, props[k].Value, 1);
            sb.Append(k < props.Count - 1 ? ",\n" : "\n");
        }

        sb.Append("}\n");
        File.WriteAllText(archivo, sb.ToString(), new UTF8Encoding(false));
    }

    private static bool Simple(JsonNode? n) => n is null or JsonValue || (n is JsonArray a && a.All(x => x is null or JsonValue))
                                                || (n is JsonObject o && o.Count <= 2 && o.All(p => p.Value is JsonValue));

    private static void Escribir(StringBuilder sb, JsonNode? n, int nivel)
    {
        var sangria = new string(' ', nivel * 2);
        switch (n)
        {
            case null:
                sb.Append("null");
                break;
            case JsonValue v:
                sb.Append(v.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                break;
            case JsonArray a when a.Count == 0:
                sb.Append("[]");
                break;
            case JsonArray a when a.All(x => x is null or JsonValue) && a.Count <= 12:
                sb.Append('[');
                for (var k = 0; k < a.Count; k++) { if (k > 0) sb.Append(", "); Escribir(sb, a[k], nivel); }
                sb.Append(']');
                break;
            case JsonArray a when a.All(x => x is null or JsonValue):
                // listas largas (p. ej. números de Disc Chips): de a 16 por línea
                sb.Append("[\n");
                for (var k = 0; k < a.Count; k += 16)
                {
                    sb.Append(sangria + "  ");
                    var trozo = a.Skip(k).Take(16).ToList();
                    for (var j = 0; j < trozo.Count; j++) { if (j > 0) sb.Append(", "); Escribir(sb, trozo[j], nivel); }
                    sb.Append(k + 16 < a.Count ? ",\n" : "\n");
                }
                sb.Append(sangria + "]");
                break;
            case JsonArray a:
                sb.Append("[\n");
                for (var k = 0; k < a.Count; k++)
                {
                    sb.Append(sangria + "  ");
                    Escribir(sb, a[k], nivel + 1);
                    sb.Append(k < a.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(sangria + "]");
                break;
            case JsonObject o when Simple(o) && o.Count <= 2:
                sb.Append("{ ");
                var i = 0;
                foreach (var p in o) { if (i++ > 0) sb.Append(", "); sb.Append($"\"{p.Key}\": "); Escribir(sb, p.Value, nivel); }
                sb.Append(" }");
                break;
            case JsonObject o:
                sb.Append("{\n");
                var lista = o.ToList();
                for (var k = 0; k < lista.Count; k++)
                {
                    sb.Append($"{sangria}  \"{lista[k].Key}\": ");
                    Escribir(sb, lista[k].Value, nivel + 1);
                    sb.Append(k < lista.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(sangria + "}");
                break;
        }
    }
}
