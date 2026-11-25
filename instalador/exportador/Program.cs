using System.Text;
using System.Text.Json;
using MRDX.Custom.Leagues;

// Exportar grabación: exporta los monstruos (rancho y congelador) de las ranuras que elija el jugador a la carpeta de
// exportados, con el mismo formato que "Rivales Kokemon" al guardar. Sirve para grabaciones de antes del Rancho y para
// exportar sin esperar a enero. Si ya hay un exportado con los mismos monstruos, no se repite.
//
// Se instala en <instalación>\exportador. Opciones (para pruebas): --exportados <carpeta>, --grabaciones <carpeta>,
// --ranuras 1,15 (sin preguntar), --idioma es|en.
Console.OutputEncoding = Encoding.UTF8;
var opciones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i + 1 < args.Length; i += 2) opciones[args[i].TrimStart('-')] = args[i + 1];

var instalacion = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
var idioma = opciones.GetValueOrDefault("idioma") ?? LeerIdioma(Path.Combine(instalacion, "liga.json"));
string T(string es, string en) => idioma == "en" ? en : es;

var grabaciones = opciones.GetValueOrDefault("grabaciones") ??
                  Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KoeiTecmo", "mfdx_en", "bu00");
var exportados = new CarpetaExportados(opciones.GetValueOrDefault("exportados") ??
                                       Path.Combine(instalacion, "Reloaded-II", "User", "Mods", "MRDX.Custom.Leagues", "Exportados"));
var preguntar = !opciones.ContainsKey("ranuras");

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine(T("Exportar grabación - MR2 Rancho de Kokemones", "Export save - MR2 Rancho de Kokemones"));
Console.ResetColor();
Console.WriteLine(T("Exporta los monstruos de una grabación (rancho y congelador) para subirlos con \"MR2 Subir exportados\".",
    "Exports a save's monsters (ranch and freezer) so you can upload them with \"MR2 Subir exportados\"."));
Console.WriteLine();

// Ranuras 1 a 15 (la 15 es el autoguardado).
var ranuras = new SortedDictionary<int, ExportedSave>();
for (var r = 1; r <= 15; r++)
{
    var archivo = Path.Combine(grabaciones, $"BISLPS-01906{r - 1:00}");
    if (!File.Exists(archivo)) continue;
    try
    {
        var g = Exportacion.DeGrabacion(File.ReadAllBytes(archivo));
        if (g.Monsters.Count == 0) continue;
        ranuras[r] = g;
        var nombres = string.Join(", ", g.Monsters.Take(4).Select(m => m.Name)) + (g.Monsters.Count > 4 ? $" (+{g.Monsters.Count - 4})" : "");
        Console.WriteLine($"{r,3}. {(r == 15 ? T("autoguardado", "autosave") + " · " : "")}{g.Date,-10} {g.Trainer ?? "?"} · {nombres}");
    }
    catch (Exception e)
    {
        Console.WriteLine($"{r,3}. " + T($"no se pudo leer ({e.Message})", $"could not be read ({e.Message})"));
    }
}

if (ranuras.Count == 0)
{
    Terminar(T($"No hay grabaciones con monstruos en {grabaciones}.", $"There are no saves with monsters in {grabaciones}."), ConsoleColor.Yellow);
    return;
}

Console.WriteLine();
string respuesta;
if (preguntar)
{
    Console.Write(T("Ranuras a exportar, separadas por coma (T = todas, Enter = cancelar): ",
        "Slots to export, comma separated (A = all, Enter = cancel): "));
    respuesta = (Console.ReadLine() ?? "").Trim();
}
else respuesta = opciones["ranuras"];

if (respuesta == "")
{
    Terminar(T("No se exportó nada.", "Nothing was exported."), ConsoleColor.Yellow);
    return;
}

var elegidas = respuesta.Equals("T", StringComparison.OrdinalIgnoreCase) || respuesta.Equals("A", StringComparison.OrdinalIgnoreCase)
    ? ranuras.Keys.ToList()
    : respuesta.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
        .Select(s => int.TryParse(s, out var n) ? n : -1).Distinct().ToList();

var hechos = 0;
foreach (var r in elegidas)
{
    if (!ranuras.TryGetValue(r, out var g))
    {
        Console.WriteLine(T($"Ranura {r}: no hay una grabación con monstruos.", $"Slot {r}: there is no save with monsters."));
        continue;
    }

    if (g.Date == "") g.Date = $"ranura-{r}";
    try
    {
        var ruta = exportados.Escribir(g);
        if (ruta == null)
            Console.WriteLine(T($"Ranura {r}: ya estaba exportada (hay un exportado con los mismos monstruos).",
                $"Slot {r}: already exported (there is an export with the same monsters)."));
        else
        {
            hechos++;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(T($"Ranura {r}: {g.Monsters.Count} monstruos en {Path.GetFileName(ruta)}",
                $"Slot {r}: {g.Monsters.Count} monsters in {Path.GetFileName(ruta)}"));
            Console.ResetColor();
        }
    }
    catch (Exception e)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(T($"Ranura {r}: no se pudo exportar ({e.Message}).", $"Slot {r}: could not export ({e.Message})."));
        Console.ResetColor();
    }
}

if (Exportacion.CaracteresDesconocidos.Count > 0)
    Console.WriteLine(T("Algunos nombres tienen caracteres desconocidos (quedaron como ?).", "Some names have unknown characters (they became ?)."));
Terminar(T($"Listo: {hechos} exportados nuevos en {exportados.Dir}", $"Done: {hechos} new exports in {exportados.Dir}"),
    hechos > 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
return;

void Terminar(string mensaje, ConsoleColor color)
{
    Console.WriteLine();
    Console.ForegroundColor = color;
    Console.WriteLine(mensaje);
    Console.ResetColor();
    if (!preguntar || Console.IsInputRedirected) return;
    Console.Write(T("Enter para cerrar", "Press Enter to close"));
    Console.ReadLine();
}

static string LeerIdioma(string ligaJson)
{
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ligaJson));
        return doc.RootElement.TryGetProperty("idioma", out var i) ? i.GetString() ?? "es" : "es";
    }
    catch
    {
        return "es";
    }
}
