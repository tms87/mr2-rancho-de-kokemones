using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;

namespace MRDX.Custom.ItemTolerance;

/// <summary>
///     Baúl de disc chips. Al empezar cada mes, los disc chips del inventario pasan al baúl de la partida (con su
///     cantidad) y dejan de ocupar lugar; antes de eso se pueden vender como siempre. En el laboratorio, la lista de
///     objetos muestra los de combinación del inventario más un disc chip de cada tipo del baúl, sin el límite de 20
///     (el juego arma esa lista en memoria propia, del tamaño que se le diga, y la recorre como una ruleta).
///     Al combinar, el juego busca el objeto en el inventario y, si no está (vino del baúl), no hace nada: el mod
///     lo descuenta del baúl.
///
///     Direcciones (MF2.exe, base estática 0x400000), halladas con la sonda de desarrollo:
///       0x779458  inventario: 20 números de objeto (u32), fin = 0xB2
///       0x5189BD  laboratorio: tras contar los objetos (edi = cantidad, [ebp-1Ch] = origen), antes de reservar la lista
///       0x5191D9  al combinar: esi = objeto elegido, antes de buscarlo en el inventario
///       0x76E294  puntero a la tabla de objetos (8 bytes cada uno; byte 7 &amp; 0x7F == 2: se consume al combinar)
/// </summary>
public unsafe partial class Mod
{
    private const uint StaticBase = 0x400000;
    private const uint InventarioVa = 0x779458, TablaObjetosVa = 0x76E294;
    private const uint ListaLabVa = 0x5189BD, CombinarVa = 0x5191D9;
    private const int Lugares = 20, Vacio = 0xB2, MaxLista = 256;

    private static Mod? _yo;
    private static nint _base;
    private static uint* _lista; // lista armada para el laboratorio (la copia el juego)
    private readonly List<IAsmHook> _ganchos = new();

    private static uint* Inventario => (uint*)(_base + (nint)(InventarioVa - StaticBase));

    private unsafe void InstalarBaul(IReloadedHooks? reloaded)
    {
        _yo = this;
        _base = System.Diagnostics.Process.GetCurrentProcess().MainModule!.BaseAddress;
        _lista = (uint*)NativeMemory.AllocZeroed(MaxLista * sizeof(uint));
        if (reloaded == null)
        {
            Log("Sin Reloaded Hooks: el baúl de disc chips no está disponible.", Color.Orange);
            return;
        }

        // Solo se engancha si el código es el esperado (otra versión del juego podría tener otra cosa ahí).
        byte[] lab = [0x8D, 0x04, 0xBD, 0x64, 0x00, 0x00, 0x00];
        byte[] combinar = [0x33, 0xC0, 0x81, 0xC2, 0x98, 0x2B, 0x00, 0x00];
        if (!Coincide(ListaLabVa, lab) || !Coincide(CombinarVa, combinar))
        {
            Log("Baúl de disc chips desactivado: el código del juego no es el esperado.", Color.Orange);
            return;
        }

        var armar = (nint)(delegate* unmanaged[Stdcall]<int, uint*, int>)&ArmarLista;
        string[] asmLab =
        [
            "use32",
            "pushad",
            "pushfd",
            "push dword [ebp-0x1C]", // origen (inventario)
            "push edi", // cantidad
            $"mov eax, 0x{armar:X}",
            "call eax", // stdcall: devuelve la cantidad nueva
            "mov [esp+4], eax", // EDI guardado por pushad (debajo de EFLAGS)
            "popfd",
            "popad",
            $"mov dword [ebp-0x1C], 0x{(nint)_lista:X}" // la lista se copia desde la del mod
        ];
        var alCombinar = (nint)(delegate* unmanaged[Stdcall]<uint, void>)&AlCombinar;
        string[] asmCombinar =
        [
            "use32",
            "pushad",
            "pushfd",
            "push esi", // objeto elegido
            $"mov eax, 0x{alCombinar:X}",
            "call eax",
            "popfd",
            "popad"
        ];
        _ganchos.Add(reloaded.CreateAsmHook(asmLab, _base + (nint)(ListaLabVa - StaticBase), AsmHookBehaviour.ExecuteFirst).Activate());
        _ganchos.Add(reloaded.CreateAsmHook(asmCombinar, _base + (nint)(CombinarVa - StaticBase), AsmHookBehaviour.ExecuteFirst).Activate());
        Log("Baúl de disc chips activo.");
    }

    private static bool Coincide(uint va, byte[] esperado)
    {
        var p = (byte*)(_base + (nint)(va - StaticBase));
        for (var i = 0; i < esperado.Length; i++)
            if (p[i] != esperado[i]) return false;
        return true;
    }

    private static bool EsChip(uint id) => Objetos.NombreDe((int)id) is { } n && Objetos.Clave(n) == "discchips";

    private static bool DeCombinacion(uint id)
    {
        var tabla = *(byte**)(_base + (nint)(TablaObjetosVa - StaticBase));
        return tabla != null && id < 256 && (tabla[id * 8 + 7] & 0x7F) == 2;
    }

    /// <summary>
    ///     Lista del laboratorio: los objetos de combinación del inventario (como están, sin agrupar) más un disc chip
    ///     de cada tipo del baúl que no esté ya en la lista. Si queda vacía, la del juego tal cual.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int ArmarLista(int cantidad, uint* origen)
    {
        try
        {
            var n = 0;
            var chips = new HashSet<uint>();
            for (var i = 0; i < cantidad && n < MaxLista; i++)
            {
                var id = origen[i];
                var chip = EsChip(id);
                if (chip && !chips.Add(id)) continue; // un disc chip de cada tipo
                if (chip || DeCombinacion(id)) _lista[n++] = id;
            }

            if (_yo != null)
                foreach (var (id, cuenta) in _yo._data.Baul.OrderBy(kv => kv.Key))
                    if (cuenta > 0 && n < MaxLista && chips.Add((uint)id))
                        _lista[n++] = (uint)id;

            if (n > 0)
            {
                _lista[n] = Vacio;
                _yo?.Log($"Laboratorio: {n} objetos en la lista ({chips.Count} tipos de disc chip, " +
                         $"{_yo._data.Baul.Values.Sum()} chips en el baúl).");
                return n;
            }
        }
        catch (Exception e)
        {
            _yo?.Log($"Error armando la lista del laboratorio: {e.Message}", Color.Red);
        }

        // Respaldo: la lista original del juego.
        for (var i = 0; i < cantidad && i < MaxLista - 1; i++) _lista[i] = origen[i];
        _lista[Math.Min(cantidad, MaxLista - 1)] = Vacio;
        return cantidad;
    }

    /// <summary>Al combinar: si el objeto es un disc chip que no está en el inventario, vino del baúl.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void AlCombinar(uint id)
    {
        try
        {
            if (_yo == null || !EsChip(id)) return;
            for (var i = 0; i < Lugares; i++)
            {
                if (Inventario[i] == id) return; // está en el inventario: lo saca el juego
                if (Inventario[i] == Vacio) break;
            }

            var baul = _yo._data.Baul;
            if (baul.TryGetValue((int)id, out var cuenta) && cuenta > 0)
            {
                baul[(int)id] = cuenta - 1;
                if (cuenta - 1 == 0) baul.Remove((int)id);
                _yo.Log($"Combinación: disc chip #{id} sacado del baúl (quedan {cuenta - 1}).", Color.LightGreen);
            }
        }
        catch (Exception e)
        {
            _yo?.Log($"Error al descontar el disc chip: {e.Message}", Color.Red);
        }
    }

    /// <summary>Al empezar un mes: todos los disc chips del inventario pasan al baúl y el resto se corre.</summary>
    private void GuardarChipsEnBaul()
    {
        var inv = Inventario;
        var quedan = new List<uint>();
        var movidos = 0;
        for (var i = 0; i < Lugares && inv[i] != Vacio; i++)
        {
            if (EsChip(inv[i]))
            {
                _data.Baul[(int)inv[i]] = _data.Baul.GetValueOrDefault((int)inv[i]) + 1;
                movidos++;
            }
            else
                quedan.Add(inv[i]);
        }

        if (movidos == 0) return;
        for (var i = 0; i < Lugares; i++) inv[i] = i < quedan.Count ? quedan[i] : Vacio;
        Log($"Mes nuevo: {movidos} disc chips pasaron al baúl ({_data.Baul.Values.Sum()} en total, " +
            $"{_data.Baul.Count} tipos).", Color.LightGreen);
    }

    /// <summary>Diagnóstico: los disc chips del baúl vuelven al inventario, en los lugares libres.</summary>
    private void DevolverBaulAlInventario()
    {
        var inv = Inventario;
        // Capacidad según la mejora del depósito (byte en 0x7791D9): 12, 14, 16, 18 o 20.
        var nivel = *(byte*)(_base + (nint)(0x7791D9 - StaticBase));
        var capacidad = Math.Min(Lugares, 12 + 2 * Math.Min((int)nivel, 4));
        var n = 0;
        while (n < Lugares && inv[n] != Vacio) n++;
        var devueltos = 0;
        foreach (var id in _data.Baul.Keys.OrderBy(k => k).ToList())
        {
            while (_data.Baul[id] > 0 && n < capacidad)
            {
                inv[n++] = (uint)id;
                _data.Baul[id]--;
                devueltos++;
            }

            if (_data.Baul[id] == 0) _data.Baul.Remove(id);
        }

        if (n < Lugares) inv[n] = Vacio;
        Log($"Diagnóstico: {devueltos} disc chips volvieron del baúl al inventario (quedan {_data.Baul.Values.Sum()} en el baúl).",
            Color.Orange);
    }

    /// <summary>¿Entre las dos semanas empezó un mes? (el contador va 12 semanas atrás: semana 0 = mes 4, semana 1).</summary>
    private static bool EmpiezaMes(uint antes, uint despues) => (despues + 12) / 4 > (antes + 12) / 4;
}
