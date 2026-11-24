using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EditorKokemon;

/// <summary>Un monstruo de la lista de rivales (un .json de Rivales Kokemon).</summary>
public sealed class Rival
{
    public required string Archivo { get; init; }
    public required JsonObject Datos { get; init; }

    /// <summary>Entrenador de la grabación de donde se exportó (vacío en los originales y los cargados antes).</summary>
    public string Entrenador => (string?)Datos["entrenador"] ?? "";

    /// <summary>Número del issue de GitHub por el que llegó (si vino de "MR2 Subir exportados").</summary>
    public int? Issue => (int?)Datos["issue"];

    public string Origen => Issue is { } n ? $"#{n}" : "";

    /// <summary>Si trae el monstruo completo ("crudo"), su único dato editable además de la liga es el nombre.</summary>
    public bool Completo => Datos["crudo"] != null;

    /// <summary>
    ///     Nombre como rival. Se puede cambiar (p. ej. para distinguir al mismo monstruo campeón de la E con 1 año y
    ///     su versión adulta); los bytes del monstruo ("crudo") no se tocan.
    /// </summary>
    public string Nombre { get => (string?)Datos["nombre"] ?? ""; set => Datos["nombre"] = value; }

    /// <summary>
    ///     Versión del monstruo (p. ej. "Campeón E" o "Definitivo"): distingue distintas instancias del mismo monstruo
    ///     (el nombre, de hasta 12 letras, es el del juego). Lo que no se puede repetir es nombre + versión.
    /// </summary>
    public string Version
    {
        get => ((string?)Datos["version"] ?? "").Trim();
        set
        {
            if (string.IsNullOrWhiteSpace(value)) Datos.Remove("version");
            else Datos["version"] = value.Trim();
        }
    }

    /// <summary>Como se muestra: "Angie (Campeón E)", o solo el nombre si no tiene versión.</summary>
    public string Etiqueta => Version == "" ? Nombre : $"{Nombre} ({Version})";

    /// <summary>Cómo lo nombran los torneos (participantes fijos): el nombre, o el archivo si tiene versión.</summary>
    public string Referencia => Version == "" ? Nombre : Path.GetFileNameWithoutExtension(NombreArchivo());
    public string Liga { get => ((string?)Datos["liga"] ?? "").Trim().ToUpperInvariant(); set => Datos["liga"] = value; }
    public string Raza => $"{Datos["razaPrincipal"]}/{Datos["razaSub"]}";
    public int Especiales => (int?)Datos["especiales"] ?? 0;

    public int Total => new[] { "vida", "poder", "inteligencia", "habilidad", "velocidad", "defensa" }
        .Sum(k => (int?)Datos[k] ?? 0);

    /// <summary>Nombre de archivo como lo arma Rivales Kokemon: Nombre_Principal-Sub.json.</summary>
    public string NombreArchivo()
    {
        var invalidos = Path.GetInvalidFileNameChars();
        string Limpio(string s) => new string(s.Where(c => !invalidos.Contains(c)).ToArray()).Trim();
        var n = Limpio(Nombre);
        var v = Version == "" ? "" : $" ({Limpio(Version)})";
        return $"{(n == "" ? "monstruo" : n)}{v}_{Datos["razaPrincipal"]}-{Datos["razaSub"]}.json";
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
    public string Torneos => Path.Combine(Raiz, @"mods\CalendarioKokemon\MRDX.Custom.CalendarioKokemon\torneos.json");
    public string Imagenes => Path.Combine(Raiz, @"mods\CalendarioKokemon\MRDX.Custom.CalendarioKokemon\imagenes");
    public string Competencias => Path.Combine(Raiz, @"mods\RanchRules\MRDX.Custom.RanchRules\Competencias");

    /// <summary>Carpetas y archivos que sube el botón "Subir" (solo contenido).</summary>
    public string[] Contenido =>
    [
        @"mods/Leagues/MRDX.Custom.Leagues/Liga",
        @"mods/CalendarioKokemon/MRDX.Custom.CalendarioKokemon/torneos.json",
        @"mods/CalendarioKokemon/MRDX.Custom.CalendarioKokemon/imagenes",
        @"mods/RanchRules/MRDX.Custom.RanchRules/Competencias"
    ];

    public Repo(string raiz) => Raiz = raiz;

    /// <summary>Busca la raíz del repositorio (la carpeta con actualizacion.json) desde la carpeta del programa.</summary>
    public static string? Buscar(string desde)
    {
        for (var d = new DirectoryInfo(desde); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "actualizacion.json"))) return d.FullName;
        return null;
    }

    /// <summary>
    ///     Rivales de una carpeta. Un archivo puede ser un rival (un monstruo) o un exportado de grabación
    ///     ("fecha Entrenador.json", con "monstruos"): en ese caso cada monstruo es un rival.
    /// </summary>
    public List<Rival> LeerRivales(string carpeta)
    {
        var res = new List<Rival>();
        if (!Directory.Exists(carpeta)) return res;
        foreach (var f in Directory.GetFiles(carpeta, "*.json"))
        {
            try
            {
                var o = JsonNode.Parse(File.ReadAllText(f))!.AsObject();
                if (o["monstruos"] is JsonArray lista)
                {
                    var de = $"exportado {o["fecha"]}{(o["entrenador"] is { } e ? $" de {e}" : "")}";
                    foreach (var m in lista.OfType<JsonObject>())
                    {
                        var datos = m.DeepClone().AsObject();
                        datos["origen"] = $"{de} ({datos["lugar"]})";
                        datos.Remove("lugar");
                        if (o["entrenador"] is { } entrenador) datos["entrenador"] = (string?)entrenador;
                        // Recibidos de GitHub: "#123 fecha Entrenador.json" → issue 123.
                        var issue = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(f), @"^#(\d+) ");
                        if (issue.Success) datos["issue"] = int.Parse(issue.Groups[1].Value);
                        res.Add(new Rival { Archivo = f, Datos = datos });
                    }
                }
                else if (o["nombre"] != null) res.Add(new Rival { Archivo = f, Datos = o });
            }
            catch
            {
                // archivo inválido: se ignora
            }
        }

        return res;
    }

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
