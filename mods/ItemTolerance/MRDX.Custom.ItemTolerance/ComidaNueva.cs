using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MRDX.Base.Mod.Interfaces;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using Reloaded.Universal.Redirector.Interfaces;

namespace MRDX.Custom.ItemTolerance;

/// <summary>Una comida nueva del menú del mes, sobre un número de objeto que el juego no usa.</summary>
public sealed record Comida(
    uint Id, // objeto que la representa: 62, 40 o 66 (los únicos sin nombre en el juego)
    string Nombre,
    string Titulo,
    string[] Texto,
    int Precio,
    int Modelo, // número del objeto cuyo modelo 3D se usa (o el archivo propio en la carpeta Modelos del mod)
    int Estres,
    int Miedo,
    int Mimo,
    int Forma,
    int Cansancio = 0,
    Especial Especial = Especial.Ninguno);

/// <summary>Lo que hace una comida nueva además de sus valores fijos.</summary>
public enum Especial
{
    Ninguno,

    /// <summary>Green Tea: suma 1 a la racha (tope 10) y tira para desintoxicar con 15 % por punto (máx. 75 %).</summary>
    Te,

    /// <summary>Chicken: baja 1 la tolerancia de la Nuts Oil; con racha del té de 2 o menos, la mantiene y tira.</summary>
    Pollo
}

/// <summary>
///     Comidas nuevas en el menú de la comida del mes, en todas las ranuras.
///     El menú reconoce cada comida por su número de objeto (nombre, modelo, precio y el aviso de "cambió la
///     selección"), así que cada comida nueva usa uno propio de los que el juego no usa: #62 (una comida descartada),
///     #40 (una droga de entrenamiento descartada) y #66 (un objeto de uso descartado). Probado: repetir un número en
///     la ruleta hace que el menú se refresque en cada cuadro y se cuelgue; números de 0xB2 en adelante cortan la
///     lista; y números de otros objetos muestran esos objetos.
///     - Lista: el menú guarda las comidas como números de objeto (obj+0x60: 0, 3, 1, 4, 2, 5, fin = 0xB2) y se la
///       pasa a la ruleta (0x455FB0) al abrir el menú y al volver a él (0x4E7B3D, 0x4E7D58). El mod pasa una lista
///       propia con las comidas nuevas al final.
///     - Nombre y descripción: el menú muestra el mensaje 0x151 + 2·objeto, que sale de la copia de los nombres de
///       objetos dentro de msg_farm_en.obj (desde el texto 336); item_msg_en.obj tiene la general (desde el 0). Los
///       textos nuevos se agregan al final de cada archivo (los del juego comparten bytes; no se reacomoda nada) y el
///       redirector de Reloaded sirve las copias.
///     - Modelo: item\itm\item_XX.itm (XX = objeto en hexadecimal), redirigido a una copia del modelo elegido.
///     - Precio y tipo: se escriben en la tabla de objetos en memoria (compra; tipo = comida). El menú mostraba el
///       precio de Potato para cualquier número de 6 en adelante (0x4E7BB5: cmp eax,6 / jae); pasa a hacerlo solo con
///       el 6 ("nada elegido todavía").
///     - Efectos propios del objeto (el juego los aplica al comer, como la forma de cada comida): se ponen en 0; los
///       pone el mod.
///     - Al confirmar (0x4E7FC9, antes de llamar a 0x4BEB50 con ecx = comida, edx = gusto): el juego aplica una
///       comida base (Potato, "le gusta"); el mod pone los valores fijos de la comida nueva descontando esa fila, y lo
///       especial de cada una (racha del Green Tea, tolerancia de la Nuts Oil con Chicken).
/// </summary>
public unsafe partial class Mod
{
    private const uint MenuAbrirVa = 0x4E7B3D, MenuVolverVa = 0x4E7D58, PrecioMenuVa = 0x4E7BB8;
    private const uint ConfirmarVa = 0x4E7FC9;
    private const int DeTablaAEfectos = 0x590; // item_dat.bin: tabla en 0x314, efectos en 0x8A4
    private const byte TipoComida = 0x08;

    // Racha del Green Tea ("acostumbramiento antifatiga a la inversa"): cada mes que lo come suma 1, hasta 10; cada mes sin comerlo
    // (ni mantenerla con Chicken) baja 1. Al comerlo tira para bajar 1 de intoxicación: 15 % por punto, con tope de 75 % (desde racha 5). Como el acostumbramiento antifatiga, la racha sigue
    // juntando hasta 10 aunque la chance ya no suba: aguanta más meses sin té antes de bajar del 75 %.
    private const int ChancePorRacha = 15, ChanceMaxima = 75, RachaMaxima = 10, RachaPollo = 2;
    private const string ClaveTe = "Green Tea";

    // Tolerancia de la Nuts Oil que baja cada comida (la Meat del juego también).
    private const uint Fish = 1, Meat = 2;
    private const int ToleranciaMeat = 2, ToleranciaPollo = 1, RachaPescado = 1;

    // Gusto (como 0x4E8440): 0 normal, 1 le gusta, 2 no le gusta. Primero el del monstruo (objeto que le gusta o no
    // en +0x106 / +0x108 de su ficha: la comida o el objeto de su modelo); si no, el de su raza (GustosRaza; la
    // tabla del juego en [0x76E290], 2 bits por comida y raza, no tiene lugar para las nuevas).

    // Ajuste según el gusto, sobre los valores fijos: (estrés, miedo, mimo).
    private static readonly (int Estres, int Miedo, int Mimo)[] AjusteGusto = [(0, 0, 0), (-2, 0, 2), (3, 1, -3)];
    private static readonly string[] NombreGusto = ["normal", "le gusta", "no le gusta"];

    // El juego aplica la fila de una comida base con el gusto que se le pasa (así el monstruo reacciona según el
    // gusto). Por gusto, la fila más cercana a nada: normal Milk (-2, 0, +1), le gusta Potato (0, 0, +1), no le gusta
    // Meat (0, +1, +1). El mod la lee de la tabla y la descuenta.
    private static readonly uint[] ComidaBasePorGusto = [3, 0, 2];
    private const int DeTablaAComidas = 0x122C; // filas (estrés, miedo, mimo): comida x 3 (le gusta, normal, no le gusta)

    // Provisorias, para probar.
    private static readonly Comida[] ComidasNuevas =
    [
        new(62, "Squash", "Squash", ["Hearty squash. Builds", "form, eases fatigue."], 500, 0x0E,
            Estres: 0, Miedo: -2, Mimo: -2, Forma: 5, Cansancio: -10),
        new(40, "Chicken", "Chicken", ["Roast chicken. Eases", "antifatigue habit."], 250, 0x02,
            Estres: -2, Miedo: -2, Mimo: 2, Forma: 2, Especial: Especial.Pollo),
        new(66, "Green Tea", "Green Tea", ["Calming tea. Monthly", "use cleanses toxins."], 800, 0x87,
            Estres: -10, Miedo: -2, Mimo: 0, Forma: -2, Especial: Especial.Te)
    ];

    // Gusto natural de cada raza principal (en el orden del juego) por las comidas nuevas, en el orden de
    // ComidasNuevas (Squash, Chicken, Green Tea): L le gusta, D no le gusta, - normal.
    private static readonly (string Raza, string Gustos)[] GustosRaza =
    [
        ("Pixie", "-DL"), ("Dragon", "-LD"), ("Centaur", "L--"), ("ColorPandora", "--L"), ("Beaclon", "LD-"),
        ("Henger", "--D"), ("Wracky", "LD-"), ("Golem", "L-D"), ("Zuum", "-L-"), ("Durahan", "DL-"),
        ("Arrowhead", "L-D"), ("Tiger", "DL-"), ("Hopper", "LD-"), ("Hare", "LD-"), ("Baku", "L--"),
        ("Gali", "-DL"), ("Kato", "-LD"), ("Zilla", "-L-"), ("Bajarl", "--L"), ("Mew", "DL-"),
        ("Phoenix", "-DL"), ("Ghost", "L-D"), ("Metalner", "--L"), ("Suezo", "L--"), ("Jill", "-LD"),
        ("Mocchi", "-DL"), ("Joker", "L-D"), ("Gaboo", "-L-"), ("Jell", "D-L"), ("Undine", "-DL"),
        ("Niton", "L--"), ("Mock", "-DL"), ("Ducken", "LD-"), ("Plant", "-DL"), ("Monol", "---"),
        ("Ape", "L-D"), ("Worm", "L--"), ("Naga", "DL-")
    ];

    private static readonly uint[] ComidasJuego = [0, 3, 1, 4, 2, 5, Vacio];
    private static uint* _menuComida;

    private readonly System.Diagnostics.Stopwatch _comidaReloj = new(); // para registrar los cambios al comer
    private IRedirectorController? _redirector;
    private readonly Dictionary<string, string> _redirecciones = new(StringComparer.OrdinalIgnoreCase);
    private string _exeDir = "";

    private static Comida? ComidaDe(uint id) => ComidasNuevas.FirstOrDefault(c => c.Id == id);

    private void InstalarComidaNueva(IReloadedHooks? reloaded)
    {
        if (reloaded == null) return;
        byte[] esperado = [0x6A, 0x00, 0x50, 0x8D, 0x4A, 0xCF];
        var controles = new (string Que, uint Va, byte[] Bytes)[]
        {
            ("lista al abrir", MenuAbrirVa, esperado),
            ("lista al volver", MenuVolverVa, esperado),
            ("precio", PrecioMenuVa, [0x73, 0x0A]),
            ("confirmar", ConfirmarVa, [0x8B, 0x4E, 0x7C, 0x8B, 0xD0])
        };
        var fallan = controles.Where(c => !Coincide(c.Va, c.Bytes)).ToList();
        if (fallan.Count > 0)
        {
            Log("Comidas nuevas desactivadas: el código del menú no es el esperado (" +
                string.Join("; ", fallan.Select(f => $"{f.Que} en 0x{f.Va:X}: " +
                    string.Join(" ", Enumerable.Range(0, f.Bytes.Length).Select(i => ((byte*)(_base + (nint)(f.Va - StaticBase)))[i].ToString("X2"))))) +
                ").", Color.Orange);
            return;
        }

        _menuComida = (uint*)NativeMemory.AllocZeroed(32 * sizeof(uint));

        // Lista del menú.
        var elegir = (nint)(delegate* unmanaged[Stdcall]<uint*, uint*>)&ListaMenuComida;
        string[] asmLista =
        [
            "use32",
            "push ecx",
            "push edx",
            "push eax", // la lista del juego
            $"mov eax, 0x{elegir:X}",
            "call eax", // stdcall: devuelve la lista a usar
            "pop edx",
            "pop ecx"
        ];
        Enganchar(reloaded, asmLista, MenuAbrirVa, AsmHookBehaviour.ExecuteFirst);
        Enganchar(reloaded, asmLista, MenuVolverVa, AsmHookBehaviour.ExecuteFirst);

        // Precio en el menú: "jae" (6 o más → Potato) pasa a "je" (solo el 6, que es "nada elegido todavía").
        var p = (byte*)(_base + (nint)(PrecioMenuVa - StaticBase));
        if (VirtualProtect((nint)p, 1, 0x40, out var antes))
        {
            *p = 0x74;
            VirtualProtect((nint)p, 1, antes, out _);
        }

        // Al confirmar (ecx = comida, edx = gusto): para una comida nueva, la comida base con su gusto.
        var alConfirmar = (nint)(delegate* unmanaged[Stdcall]<uint, int, byte*, int>)&AlConfirmarComida;
        string[] asmConfirmar =
        [
            "use32",
            "push ecx",
            "push edx",
            "push esi", // el menú (su +0x44 lleva a la ficha del monstruo)
            "push edx", // gusto que calculó el juego
            "push ecx", // comida elegida
            $"mov eax, 0x{alConfirmar:X}",
            "call eax", // -1: no tocar; si no, (comida base << 8) | gusto
            "pop edx",
            "pop ecx",
            "cmp eax, -1",
            "je comida_igual",
            "movzx edx, al",
            "shr eax, 8",
            "mov ecx, eax",
            "mov eax, edx", // el gusto también queda en eax (el juego lo guarda en [esi+88h])
            "jmp comida_fin",
            "comida_igual:",
            "mov eax, edx",
            "comida_fin:"
        ];
        Enganchar(reloaded, asmConfirmar, ConfirmarVa, AsmHookBehaviour.ExecuteAfter);

        PrepararArchivosComida();
    }

    private void Enganchar(IReloadedHooks reloaded, string[] asm, uint va, AsmHookBehaviour cuando) =>
        _ganchos.Add(reloaded.CreateAsmHook(asm, _base + (nint)(va - StaticBase), cuando).Activate());

    /// <summary>Archivos de mensajes con los textos de las comidas y sus modelos, servidos por el redirector.</summary>
    private void PrepararArchivosComida()
    {
        _modLoader.GetController<IRedirectorController>()?.TryGetTarget(out _redirector);
        if (_redirector == null)
        {
            Log("Sin el redirector de Reloaded: las comidas nuevas no tienen nombre ni modelo propios.", Color.Orange);
            return;
        }

        try
        {
            _exeDir = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName)!;
            var dir = Path.Combine(_modLoader.GetModConfigDirectory(_modConfig.ModId), "comida");
            Directory.CreateDirectory(dir);

            var obj = Path.Combine(_exeDir, "Resources", "data", "mf2", "data", "obj");
            foreach (var (archivo, desde) in new[] { ("item_msg_en.obj", 0), ("msg_farm_en.obj", 336) })
                try
                {
                    var copia = Path.Combine(dir, archivo);
                    File.WriteAllBytes(copia, MensajesConComidas(File.ReadAllBytes(Path.Combine(obj, archivo)), desde));
                    Redirigir(Path.Combine("data", "obj", archivo), copia);
                }
                catch (Exception e)
                {
                    Log($"Comidas nuevas: no se pudo preparar {archivo}: {e.Message}", Color.Orange);
                }

            var itm = Path.Combine(_exeDir, "Resources", "data", "mf2", "data", "item", "itm");
            // Modelo de cada comida: el del objeto de origen con la textura de su receta (Texturas), armado sobre el
            // archivo del jugador. Un archivo en la carpeta Modelos del mod con el número de la comida (item_42.itm
            // para la #66) le gana, para probar otros. Solo lo usa esa comida; el objeto original no cambia.
            var propios = Path.Combine(_modLoader.GetDirectoryForModId(_modConfig.ModId), "Modelos");
            foreach (var c in ComidasNuevas)
                try
                {
                    var modelo = Path.Combine(dir, $"item_{c.Id:x2}.itm");
                    var propio = Path.Combine(propios, $"item_{c.Id:x2}.itm");
                    var origen = File.ReadAllBytes(Path.Combine(itm, $"item_{c.Modelo:x2}.itm"));
                    var armado = Texturas.TieneReceta(c.Id) ? Texturas.Aplicar(c.Id, origen) : origen;
                    if (File.Exists(propio))
                    {
                        var archivo = File.ReadAllBytes(propio);
                        Log($"Comida {c.Nombre}: modelo propio {Path.GetFileName(propio)} " +
                            $"({(archivo.AsSpan().SequenceEqual(armado) ? "igual" : "distinto")} al que arma la receta).", Color.LightGreen);
                        armado = archivo;
                    }

                    File.WriteAllBytes(modelo, armado);
                    Redirigir(Path.Combine("data", "item", "itm", $"item_{c.Id:x2}.itm"), modelo);
                }
                catch (Exception e)
                {
                    Log($"Comida {c.Nombre}: no se pudo armar el modelo: {e.Message}", Color.Orange);
                }

            _redirector.Loading += AlCargarArchivo;
            Log($"Comidas nuevas listas: {string.Join(", ", ComidasNuevas.Select(c => $"{c.Nombre} (#{c.Id})"))}.");
        }
        catch (Exception e)
        {
            Log($"No se pudieron preparar los archivos de las comidas nuevas: {e.Message}", Color.Orange);
        }
    }

    // El juego prueba Resources\mf2\... y Resources\data\mf2\...: se redirigen las dos.
    private void Redirigir(string relativo, string destino)
    {
        foreach (var raiz in new[] { Path.Combine(_exeDir, "Resources", "data", "mf2"), Path.Combine(_exeDir, "Resources", "mf2") })
            RedirigirRuta(Path.GetFullPath(Path.Combine(raiz, relativo)), destino);
    }

    private void RedirigirRuta(string origen, string destino)
    {
        _redirector!.RemoveRedirect(origen);
        _redirecciones[origen] = destino;
        _redirector.AddRedirect(origen, destino);
    }

    // Por si el juego abre el archivo con otra forma de la ruta: se registra para la próxima vez.
    private void AlCargarArchivo(string ruta)
    {
        var nombre = Path.GetFileName(ruta);
        foreach (var (origen, destino) in _redirecciones.ToList())
        {
            if (!string.Equals(Path.GetFileName(origen), nombre, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(ruta, destino, StringComparison.OrdinalIgnoreCase) || _redirecciones.ContainsKey(ruta)) return;
            RedirigirRuta(ruta, destino);
            return;
        }
    }

    /// <summary>
    ///     Archivo de mensajes con el nombre y la descripción de cada comida nueva, agregados al final y apuntados desde
    ///     los textos vacíos de su objeto. Los textos del juego pueden compartir bytes (los desplazamientos no van
    ///     siempre en orden), así que no se reacomoda nada.
    ///     <paramref name="desde" /> = texto donde empieza la lista de objetos (nombre, descripción, ...).
    /// </summary>
    private static byte[] MensajesConComidas(byte[] b, int desde)
    {
        var textosN = BitConverter.ToInt32(b, 0) / 4;
        var potato = desde < textosN ? BitConverter.ToInt32(b, desde * 4) : -1;
        byte[] esperado = [0xB5, 0x0F, 0xB5, 0x28, 0xB5, 0x2D]; // "Pot"
        if (potato < 0 || potato + esperado.Length > b.Length || !b.AsSpan(potato, esperado.Length).SequenceEqual(esperado))
            throw new InvalidDataException("archivo de mensajes no esperado");

        var salida = new List<byte>(b);
        var punteros = new List<(int Texto, int Desplazamiento)>();
        foreach (var comida in ComidasNuevas)
        {
            var iNombre = desde + (int)comida.Id * 2;
            if (iNombre + 1 >= textosN) throw new InvalidDataException($"no hay lugar para el objeto #{comida.Id}");
            if (b[BitConverter.ToInt32(b, iNombre * 4)] != 0xFF)
                throw new InvalidDataException($"el objeto #{comida.Id} ya tiene nombre");

            punteros.Add((iNombre, salida.Count));
            salida.AddRange(Codificar(comida.Nombre));
            salida.Add(0xFF);
            punteros.Add((iNombre + 1, salida.Count));
            salida.AddRange([0xD0, 0x80, 0x50, 0x00]);
            salida.AddRange(Codificar(comida.Titulo));
            salida.AddRange([0xFD, 0xD0, 0x80, 0x80, 0x80]);
            for (var i = 0; i < comida.Texto.Length; i++)
            {
                if (i > 0) salida.Add(0xFD);
                salida.AddRange(Codificar(comida.Texto[i]));
            }

            salida.Add(0xFF);
        }

        // El juego lee los desplazamientos como u16.
        if (salida.Count > 0xFFFF) throw new InvalidDataException("los textos nuevos no entran en el archivo");
        var res = salida.ToArray();
        foreach (var (texto, desp) in punteros) BitConverter.GetBytes(desp).CopyTo(res, texto * 4);
        return res;
    }

    // Codificación del juego: 2 bytes por carácter (A=B500, a=B51A, 0=B557, espacio=B561, punto=B534, coma=B539).
    private static IEnumerable<byte> Codificar(string texto)
    {
        foreach (var c in texto)
        {
            ushort code = c switch
            {
                >= 'A' and <= 'Z' => (ushort)(0xB500 + (c - 'A')),
                >= 'a' and <= 'z' => (ushort)(0xB51A + (c - 'a')),
                >= '0' and <= '9' => (ushort)(0xB557 + (c - '0')),
                ' ' => 0xB561,
                '.' => 0xB534,
                ',' => 0xB539,
                '?' => 0xB537,
                _ => throw new ArgumentException($"Carácter no soportado: {c}")
            };
            yield return (byte)(code >> 8);
            yield return (byte)(code & 0xFF);
        }
    }

    private static byte* TablaObjetos => *(byte**)(_base + (nint)(TablaObjetosVa - StaticBase));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint* ListaMenuComida(uint* original)
    {
        try
        {
            if (_yo == null) return original;
            for (var i = 0; i < ComidasJuego.Length; i++)
                if (original[i] != ComidasJuego[i]) return original; // otra lista: no se toca

            // Cada vez: si el juego vuelve a cargar la tabla, se pierden los cambios (repetirlos no cambia nada).
            if (!PrepararTabla()) return original;

            var n = 0;
            for (; n < ComidasJuego.Length - 1; n++) _menuComida[n] = original[n];
            foreach (var c in ComidasNuevas) _menuComida[n++] = c.Id;
            _menuComida[n] = Vacio;
            if (_yo._config.VerboseLog) _yo.Log($"Menú de comida: {ComidasNuevas.Length} comidas nuevas al final.");
            return _menuComida;
        }
        catch (Exception e)
        {
            _yo?.Log($"Error armando el menú de comida: {e.Message}", Color.Red);
            return original;
        }
    }

    /// <summary>
    ///     En la tabla de objetos en memoria: cada comida nueva pasa a ser comida, con su precio y sin efectos propios
    ///     (sus registros de efectos quedan con valor 0, o apuntan a uno ya vaciado si el suyo es compartido).
    /// </summary>
    private static bool PrepararTabla()
    {
        var tabla = TablaObjetos;
        if (tabla == null) return false;
        var efectos = tabla + DeTablaAEfectos;
        uint Registro(uint id) => *(uint*)(efectos + tabla[id * 8 + 6] * 4);

        // Varios objetos pueden compartir un registro (el del #66 es el de #64 a #68): solo se vacían los que usa
        // nada más que la comida nueva; las demás pasan a usar uno de esos.
        byte? vacio = null;
        var compartidas = new List<Comida>();
        foreach (var c in ComidasNuevas)
        {
            var reg = Registro(c.Id);
            var usan = Enumerable.Range(0, Vacio).Count(i => Registro((uint)i) == reg);
            if (usan > 1)
            {
                compartidas.Add(c);
                continue;
            }

            // Registros [tipo u16, índice u16, parámetro u16, valor u16] hasta FFFF: valor 0.
            var r = efectos + reg;
            for (var k = 0; k < 8 * 8 && *(ushort*)(r + k) != 0xFFFF; k += 8) *(short*)(r + k + 6) = 0;
            vacio ??= tabla[c.Id * 8 + 6];
        }

        if (compartidas.Count > 0 && vacio == null)
        {
            _yo?.Log("Comidas nuevas: no hay un registro de efectos propio para vaciar.", Color.Orange);
            return false;
        }

        foreach (var c in compartidas) tabla[c.Id * 8 + 6] = vacio!.Value;

        foreach (var c in ComidasNuevas)
        {
            var ficha = tabla + c.Id * 8;
            *(ushort*)(ficha + 2) = (ushort)c.Precio;
            ficha[7] = TipoComida;
        }

        return true;
    }

    /// <summary>Al confirmar la comida del mes. Devuelve -1 si no es una nueva; si no, (comida base &lt;&lt; 8) | gusto.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int AlConfirmarComida(uint id, int gusto, byte* menu)
    {
        if (_yo is { } yo)
            try
            {
                yo.ComerComidaJuego(id);
            }
            catch (Exception e)
            {
                yo.Log($"Error con la comida del mes #{id}: {e.Message}", Color.Red);
            }

        if (ComidaDe(id) is not { } comida) return -1;
        var elegido = 0;
        try
        {
            elegido = GustoDe(comida, menu, out var porque);
            _yo?.ComerComidaNueva(comida, elegido, porque);
        }
        catch (Exception e)
        {
            _yo?.Log($"Error con la comida nueva: {e.Message}", Color.Red);
        }

        return (int)(ComidaBasePorGusto[elegido] << 8) | elegido;
    }

    /// <summary>Gusto del monstruo por una comida nueva: el suyo propio, o el de su raza por la comida pariente.</summary>
    private static int GustoDe(Comida comida, byte* menu, out string porque)
    {
        porque = "sin datos";
        var ficha = *(byte**)(menu + 0x44);
        var mon = ficha == null ? null : *(byte**)(ficha + 4);
        if (mon == null) return 0;

        int gusta = *(short*)(mon + 0x106), odia = *(short*)(mon + 0x108), raza = *(int*)(mon + 4);
        var ficha2 = $"objeto que le gusta #{gusta}, que no le gusta #{odia}, raza #{raza}";
        if (gusta == comida.Id || gusta == comida.Modelo)
        {
            porque = $"le gusta ese objeto; {ficha2}";
            return 1;
        }

        if (odia == comida.Id || odia == comida.Modelo)
        {
            porque = $"no le gusta ese objeto; {ficha2}";
            return 2;
        }

        var i = Array.IndexOf(ComidasNuevas, comida);
        if (raza < 0 || raza >= GustosRaza.Length || i < 0)
        {
            porque = $"sin dato de raza; {ficha2}";
            return 0;
        }

        porque = $"su raza, {GustosRaza[raza].Raza}; {ficha2}";
        return GustosRaza[raza].Gustos[i] switch { 'L' => 1, 'D' => 2, _ => 0 };
    }

    /// <summary>Fila (estrés, miedo, mimo) que el juego aplica para una comida y un gusto.</summary>
    private static (int Estres, int Miedo, int Mimo) FilaComida(uint comida, int gusto)
    {
        var tabla = TablaObjetos;
        if (tabla == null) return (0, 0, 0);
        var fila = (sbyte*)(tabla + DeTablaAComidas + (comida * 3 + (gusto == 1 ? 0 : gusto == 0 ? 1 : 2)) * 3);
        return (fila[0], fila[1], fila[2]);
    }

    /// <summary>Mes del juego según la semana (el contador va 12 semanas atrás, como en <see cref="EmpiezaMes" />).</summary>
    private static long MesDe(long semana) => (semana + 12) / 4;

    private void ComerComidaNueva(Comida comida, int gusto, string porque)
    {
        if (_game?.Monster is not { } m) return;
        _comidaReloj.Restart();
        Log($"{comida.Nombre}: antes de comer: estrés {m.Stress}, miedo {m.LoyalFear}, mimo {m.LoyalSpoil}, forma {m.FormRaw}, " +
            $"cansancio {m.Fatigue}. Gusto: {NombreGusto[gusto]} ({porque}).");

        // Valores fijos más el ajuste por gusto. El juego suma después la fila de la comida base: se descuenta acá.
        var aj = AjusteGusto[gusto];
        var fila = FilaComida(ComidaBasePorGusto[gusto], gusto);
        int estres = comida.Estres + aj.Estres, miedo = comida.Miedo + aj.Miedo, mimo = comida.Mimo + aj.Mimo;
        m.Stress = (sbyte)Math.Clamp(m.Stress + estres - fila.Estres, 0, 100);
        m.LoyalFear = (byte)Math.Clamp(m.LoyalFear + miedo - fila.Miedo, 0, 100);
        m.LoyalSpoil = (byte)Math.Clamp(m.LoyalSpoil + mimo - fila.Mimo, 0, 100);
        m.FormRaw = (sbyte)Math.Clamp(m.FormRaw + comida.Forma, -100, 100);
        if (comida.Cansancio != 0) m.Fatigue = (byte)Math.Clamp(m.Fatigue + comida.Cansancio, 0, 100);
        Log($"{comida.Nombre}: estrés {estres:+0;-0;0}, miedo {miedo:+0;-0;0}, mimo {mimo:+0;-0;0}, " +
            $"forma {comida.Forma:+0;-0;0}, cansancio {comida.Cansancio:+0;-0;0} (fila base del juego {fila}).");

        switch (comida.Especial)
        {
            case Especial.Te:
                RachaTe(m, comida.Nombre, (r, yaEsteMes) => yaEsteMes ? r : Math.Min(r + 1, RachaMaxima));
                break;
            case Especial.Pollo:
                BajarToleranciaNuts(m, ToleranciaPollo, comida.Nombre);
                RachaTe(m, comida.Nombre, (r, _) => r <= RachaPollo ? r : null);
                break;
        }
    }

    /// <summary>Comidas del juego con algo extra: Meat (acostumbramiento antifatiga) y Fish (racha del Green Tea).</summary>
    private void ComerComidaJuego(uint id)
    {
        if (_game?.Monster is not { } m) return;
        if (id == Meat) BajarToleranciaNuts(m, ToleranciaMeat, "Meat");
        // Fish: con racha del té de 1 o menos la deja en 1 (la mantiene o la empieza); tira con la racha que tenga.
        if (id == Fish) RachaTe(m, "Fish", (r, _) => r <= RachaPescado ? RachaPescado : null);
    }

    /// <summary>
    ///     Racha del Green Tea: primero baja 1 por cada mes sin comerlo (ni mantenerla) desde el último; después
    ///     <paramref name="regla" /> da la racha nueva (null: no la toca y sigue bajando como si no hubiera comido).
    ///     Con racha 1 o más, tira siempre con la racha que tenga para bajar 1 de intoxicación.
    /// </summary>
    private void RachaTe(IMonster m, string quien, Func<int, bool, int?> regla)
    {
        var rec = _data.For(m);
        var mes = _week >= 0 ? MesDe(_week) : -1;
        var ultimo = rec.MesesComidas.GetValueOrDefault(ClaveTe, -1);
        var racha = rec.ConstanciaComidas.GetValueOrDefault(ClaveTe);
        if (mes >= 0 && ultimo >= 0 && mes > ultimo + 1) racha = Math.Max(0, racha - (int)(mes - ultimo - 1));

        if (regla(racha, mes >= 0 && ultimo == mes) is { } nueva)
        {
            racha = nueva;
            rec.ConstanciaComidas[ClaveTe] = racha;
            rec.MesesComidas[ClaveTe] = mes;
        }
        else
            Log($"{quien}: racha del Green Tea {racha}: no la mantiene (sigue bajando), pero tira igual.");
        if (racha < 1)
        {
            Log($"{quien}: racha del Green Tea 0, sin tirada.");
            return;
        }

        var chance = Math.Min(ChancePorRacha * racha, ChanceMaxima);
        var antes = rec.LifetimeCount;
        if (antes > 0 && _rng.Next(100) < chance) rec.LifetimeCount = antes - 1;
        Log($"{quien}: racha del Green Tea {racha} (chance {chance} %). " +
            (rec.LifetimeCount < antes ? $"Desintoxica: {antes}→{rec.LifetimeCount}." : $"Esta vez no desintoxicó (intoxicación {antes})."),
            rec.LifetimeCount < antes ? Color.LightGreen : null);
    }

    private void BajarToleranciaNuts(IMonster m, int cuanto, string quien)
    {
        var rec = _data.For(m);
        var clave = KeyFor(Tipo.NutsOil);
        var tol = rec.Tolerance.GetValueOrDefault(clave);
        if (tol > 0) rec.Tolerance[clave] = Math.Max(0, tol - cuanto);
        Log($"{quien}: acostumbramiento antifatiga {tol}→{Math.Max(0, tol - cuanto)}.");
    }

    [DllImport("kernel32")] private static extern bool VirtualProtect(nint address, nint size, uint protect, out uint old);
}
