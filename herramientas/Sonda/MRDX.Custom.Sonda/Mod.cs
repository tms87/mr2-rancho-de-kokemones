using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MRDX.Base.Mod.Interfaces;
using MRDX.Custom.Sonda.Configuration;
using MRDX.Custom.Sonda.Template;
using Reloaded.Mod.Interfaces;

namespace MRDX.Custom.Sonda;

/// <summary>
///     Sonda de desarrollo: puntos de interrupción por hardware (registros de depuración DR0-DR3 de la CPU) sobre el
///     inventario (exe+0x379458, 20 lugares de 4 bytes, fin = 0xB2) y la capacidad del depósito (exe+0x3791D9).
///     Cada vez que una instrucción lee o escribe esas direcciones, la CPU lanza una excepción de paso único; un
///     manejador de excepciones vectorizado anota la dirección de la instrucción y sigue. Se registra cada dirección
///     nueva con la hora, para cruzarla con lo que el jugador hacía (abrir el laboratorio, el menú de objetos, la tienda).
/// </summary>
public unsafe class Mod : ModBase
{
    private const uint Inventario = 0x379458, Capacidad = 0x3791D9;

    private readonly ILogger _logger = null!;
    private readonly IModConfig _modConfig = null!;
    private static nint _exeBase;

    // Búfer circular que llena el manejador (sin reservar memoria): (EIP, registro DR, ESP[0]).
    private const int Slots = 4096;
    private static uint* _buf;
    private static int _index;
    private int _read;
    private readonly Dictionary<uint, int> _vistas = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private nint _vectored;
    private readonly uint _ctxFlags = 0x00010010; // CONTEXT_i386 | CONTEXT_DEBUG_REGISTERS

    public Mod(ModContext context)
    {
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        if (!context.Configuration.Enabled) return;

        _exeBase = Process.GetCurrentProcess().MainModule!.BaseAddress;
        _buf = (uint*)NativeMemory.AllocZeroed(Slots * 3 * sizeof(uint));
        _vectored = AddVectoredExceptionHandler(1, (nint)(delegate* unmanaged[Stdcall]<ExceptionPointers*, int>)&Handler);

        IGame? game = null;
        context.ModLoader.GetController<IGame>()?.TryGetTarget(out game);
        if (game != null) game.OnGameSceneChanged += escena => Log($"Escena: {escena}", Color.Cyan);

        new Thread(Bucle) { IsBackground = true, Name = "Sonda" }.Start();
        Log($"Sonda activa: inventario en 0x{_exeBase + Inventario:X}, capacidad en 0x{_exeBase + Capacidad:X}.", Color.LightGreen);
    }

    public Mod()
    {
    }

    // ------------------------------------------------------------------

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Handler(ExceptionPointers* p)
    {
        const uint SingleStep = 0x80000004;
        if (p->Record->Code != SingleStep) return 0; // EXCEPTION_CONTINUE_SEARCH
        var ctx = (byte*)p->Context;
        var dr6 = *(uint*)(ctx + 0x14);
        if ((dr6 & 0xF) == 0) return 0; // no fue uno de los nuestros
        var eip = *(uint*)(ctx + 0xB8);
        var esp = *(uint*)(ctx + 0xC4);
        var i = Interlocked.Increment(ref _index) - 1;
        var s = (i & (Slots - 1)) * 3;
        _buf[s] = eip;
        _buf[s + 1] = dr6 & 0xF;
        _buf[s + 2] = *(uint*)esp; // lo que está arriba de la pila (a veces una dirección de retorno)
        *(uint*)(ctx + 0x14) = 0; // limpiar DR6
        return -1; // EXCEPTION_CONTINUE_EXECUTION
    }

    private void Bucle()
    {
        var ctx = (byte*)NativeMemory.AlignedAlloc(0x2CC + 16, 16);
        // Sesión 2: la combinación (solo el inventario: vigilar la memoria del laboratorio cerraba el juego, porque
        // se reutiliza para la lista de resultados).
        var dr = new[]
        {
            (uint)(_exeBase + Inventario), // DR0: lugar 1
            (uint)(_exeBase + Inventario + 6 * 4), // DR1: lugar 7 (Old Sheath)
            (uint)(_exeBase + Inventario + 12 * 4), // DR2: lugar 13 (el último)
            (uint)(_exeBase + Inventario + 13 * 4) // DR3: lugar 14 (la marca de fin)
        };
        // L0-L3 activados; RW = 11 (lectura o escritura); LEN = 11 (4 bytes).
        const uint dr7 = 0x55 | (0xFu << 16) | (0xFu << 20) | (0xFu << 24) | (0xFu << 28);
        var mio = GetCurrentThreadId();
        var armados = new HashSet<uint>();

        while (true)
        {
            try
            {
                foreach (ProcessThread t in Process.GetCurrentProcess().Threads)
                {
                    var id = (uint)t.Id;
                    if (id == mio || armados.Contains(id)) continue;
                    var h = OpenThread(0x0002 | 0x0008 | 0x0010, false, id); // SUSPEND_RESUME | GET_CONTEXT | SET_CONTEXT
                    if (h == 0) continue;
                    if (SuspendThread(h) != uint.MaxValue)
                    {
                        NativeMemory.Clear(ctx, 0x2CC);
                        *(uint*)ctx = _ctxFlags;
                        if (GetThreadContext(h, ctx))
                        {
                            *(uint*)(ctx + 0x04) = dr[0];
                            *(uint*)(ctx + 0x08) = dr[1];
                            *(uint*)(ctx + 0x0C) = dr[2];
                            *(uint*)(ctx + 0x10) = dr[3];
                            *(uint*)(ctx + 0x18) = dr7;
                            if (SetThreadContext(h, ctx)) armados.Add(id);
                        }

                        ResumeThread(h);
                    }

                    CloseHandle(h);
                }
            }
            catch (Exception e)
            {
                Log($"Error armando hilos: {e.Message}", Color.Red);
            }

            Vaciar();
            Thread.Sleep(500);
        }
    }

    private void Vaciar()
    {
        var hasta = Volatile.Read(ref _index);
        if (hasta - _read > Slots) _read = hasta - Slots;
        for (; _read < hasta; _read++)
        {
            var s = (_read & (Slots - 1)) * 3;
            var eip = _buf[s];
            var cual = _buf[s + 1];
            var pila = _buf[s + 2];
            _vistas.TryGetValue(eip, out var n);
            _vistas[eip] = n + 1;
            if (n == 0)
                Log($"[{_clock.Elapsed:mm\\:ss\\.f}] NUEVA lectura/escritura en {Rel(eip)} (DR{Bits(cual)}), pila {Rel(pila)}",
                    Color.Yellow);
        }
    }

    private static string Bits(uint b) => string.Join(",", Enumerable.Range(0, 4).Where(i => (b & (1u << i)) != 0));

    // Dirección como la usamos en el desensamblado (base estática 0x400000).
    private static string Rel(uint a)
    {
        var off = (long)a - _exeBase;
        return off is >= 0 and < 0x2000000 ? $"0x{0x400000 + off:X6}" : $"0x{a:X8}";
    }

    private void Log(string msg, Color? color = null) => _logger.WriteLine($"[Sonda] {msg}", color ?? Color.White);

    // ------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionRecord
    {
        public uint Code;
        public uint Flags;
        public nint Next;
        public nint Address;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionPointers
    {
        public ExceptionRecord* Record;
        public void* Context;
    }

    [DllImport("kernel32")] private static extern nint AddVectoredExceptionHandler(uint first, nint handler);
    [DllImport("kernel32")] private static extern nint OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32")] private static extern uint SuspendThread(nint h);
    [DllImport("kernel32")] private static extern uint ResumeThread(nint h);
    [DllImport("kernel32")] private static extern bool GetThreadContext(nint h, void* ctx);
    [DllImport("kernel32")] private static extern bool SetThreadContext(nint h, void* ctx);
    [DllImport("kernel32")] private static extern bool CloseHandle(nint h);
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
}
