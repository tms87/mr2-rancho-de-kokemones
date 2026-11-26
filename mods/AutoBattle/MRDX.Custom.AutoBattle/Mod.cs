using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using MRDX.Base.Mod.Interfaces;
using MRDX.Custom.AutoBattle.Configuration;
using MRDX.Custom.AutoBattle.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using Reloaded.Mod.Interfaces;

namespace MRDX.Custom.AutoBattle;

/// <summary>
///     Batallas automáticas: en los torneos y batallas del juego, la pregunta "¿controlar al monstruo?" de antes de
///     la batalla lleva siempre al modo automático (bit 0x80 del byte +0x61 de la estructura de la batalla).
///     El modo VS usa otra pregunta y no se modifica.
///     Opcional (solo desarrollo): diagnóstico que registra qué código lee cada menú del juego.
///     Funciones del menú (MF2.exe estático, base 0x400000), halladas desensamblando:
///       0x453AE0 / 0x453A60  devuelven la opción elegida (cursor en 0x769AB4)
///       0x452DD0             abre un menú
/// </summary>
public class Mod : ModBase
{
    private const uint StaticBase = 0x400000;
    private static readonly (uint Va, int Tag, string Name)[] Targets =
    [
        (0x453AE0, 1, "leer opción (A)"),
        (0x453A60, 2, "leer opción (B)"),
        (0x4533F0, 3, "menú 4533F0"),
        (0x452DD0, 4, "abrir menú 452DD0"),
        (0x452B20, 5, "menú 452B20"),
        (0x452B60, 6, "menú 452B60"),
        (0x452C00, 7, "menú 452C00"),
        (0x452CE0, 8, "menú 452CE0"),
        (0x452D70, 9, "menú 452D70"),
        (0x4538F0, 10, "menú 4538F0"),
        (0x453BB0, 11, "menú 453BB0"),
        (0x453C40, 12, "menú 453C40"),
        (0x453DC0, 13, "menú 453DC0"),
        (0x453E20, 14, "menú 453E20")
    ];

    private const int Slots = 1024; // búfer circular de (dirección de retorno, etiqueta)

    private readonly ILogger _logger = null!;
    private readonly IModConfig _modConfig = null!;
    private readonly Config _config = null!;
    private readonly nint _exeBase;
    private readonly nint _buffer;  // Slots * 8 bytes
    private readonly nint _index;   // contador (int)
    private readonly List<IAsmHook> _asmHooks = new();
    private IHook<FrameStart>? _frameHook;
    private int _read;
    private readonly Dictionary<(uint, int), long> _lastSeen = new();
    private readonly nint _selfSlot;
    private nint _battleStruct; // estructura vigilada (de [self+4Ch])
    private int _lastFlags = -1;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public Mod(ModContext context)
    {
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        _config = context.Configuration;
        _exeBase = Process.GetCurrentProcess().MainModule!.BaseAddress;

        IHooks? hooks = null;
        context.ModLoader.GetController<IHooks>()?.TryGetTarget(out hooks);
        if (hooks == null || context.Hooks == null)
        {
            Log("No se encontró MRDX.Base.Mod o Reloaded Hooks.", Color.Red);
            return;
        }

        if (_config.AutoBattle) PatchBattleQuestion();

        if (!_config.MenuDiagnostics)
        {
            Log("Cargado.", Color.LightGreen);
            return;
        }

        _buffer = Marshal.AllocHGlobal(Slots * 8);
        _index = Marshal.AllocHGlobal(4);
        Marshal.WriteInt32(_index, 0);

        foreach (var (va, tag, _) in Targets)
        {
            var addr = _exeBase + (nint)(va - StaticBase);
            string[] asm =
            [
                "use32",
                "push eax",
                "push edx",
                $"mov edx, [0x{_index:X}]",
                $"and edx, {Slots - 1}",
                "mov eax, [esp+8]", // dirección de retorno (al entrar a la función)
                $"mov [0x{_buffer:X} + edx*8], eax",
                $"mov dword [0x{_buffer:X} + edx*8 + 4], {tag}",
                $"inc dword [0x{_index:X}]",
                "pop edx",
                "pop eax"
            ];
            _asmHooks.Add(context.Hooks.CreateAsmHook(asm, addr, AsmHookBehaviour.ExecuteFirst).Activate());
        }

        // Estado 0x0E (batalla automática), función 0x4A3D60(self, ?, ?): guarda "self" para ubicar la estructura
        // de la batalla ([self+4Ch]), cuyo byte +0x61 lleva el bit 0x80 de "automático".
        _selfSlot = Marshal.AllocHGlobal(4);
        Marshal.WriteInt32(_selfSlot, 0);
        string[] asmSelf =
        [
            "use32",
            "push eax",
            "mov eax, [esp+8]", // primer argumento (tras el push: [esp+4] = retorno, [esp+8] = arg1)
            $"mov [0x{_selfSlot:X}], eax",
            "pop eax"
        ];
        _asmHooks.Add(context.Hooks.CreateAsmHook(asmSelf, _exeBase + (nint)(0x4A3D60 - StaticBase),
            AsmHookBehaviour.ExecuteFirst).Activate());

        hooks.AddHook<FrameStart>(OnFrame).ContinueWith(r => _frameHook = r.Result);
        Log($"Diagnóstico de menús activo (exe en 0x{_exeBase:X}).", Color.LightGreen);
    }

    public Mod()
    {
    }

    private int OnFrame()
    {
        var ret = _frameHook!.OriginalFunction();
        try
        {
            var written = Marshal.ReadInt32(_index);
            if (written - _read > Slots) _read = written - Slots;
            for (; _read < written; _read++)
            {
                var slot = _read & (Slots - 1);
                var retAddr = (uint)Marshal.ReadInt32(_buffer, slot * 8);
                var tag = Marshal.ReadInt32(_buffer, slot * 8 + 4);
                var stat = (uint)(retAddr - (uint)_exeBase + StaticBase);

                // Solo registra cada llamador cuando reaparece tras 2 s de silencio (los menús se leen cada frame).
                var key = (stat, tag);
                var now = _clock.ElapsedMilliseconds;
                var nuevo = !_lastSeen.TryGetValue(key, out var last) || now - last > 2000;
                _lastSeen[key] = now;
                if (!nuevo) continue;

                var cursor = Marshal.ReadInt16(_exeBase + (nint)(0x769AB4 - StaticBase));
                var name = Targets.First(t => t.Tag == tag).Name;
                Log($"[diag] {name}: llamado desde 0x{stat:X} (opción actual {cursor})");
            }

            // Estructura de la batalla: dónde está y cómo cambia su byte +0x61 (bit 0x80 = automático).
            var self = (nint)Marshal.ReadInt32(_selfSlot);
            if (self != 0)
            {
                Marshal.WriteInt32(_selfSlot, 0);
                var ptr = (nint)Marshal.ReadInt32(self + 0x4C);
                var ptr44 = (nint)Marshal.ReadInt32(self + 0x44);
                Log($"[diag] estado automático: self=0x{self:X} ({Ubicacion(self)}), [self+4Ch]=0x{ptr:X} ({Ubicacion(ptr)}), [self+44h]=0x{ptr44:X} ({Ubicacion(ptr44)})");
                if (ptr != 0 && ptr != _battleStruct)
                {
                    _battleStruct = ptr;
                    _lastFlags = -1;
                }
            }

            if (_battleStruct != 0)
            {
                var flags = Marshal.ReadByte(_battleStruct + 0x61);
                if (flags != _lastFlags)
                {
                    Log($"[diag] byte +0x61 = 0x{flags:X2} (automático: {((flags & 0x80) != 0 ? "sí" : "no")})");
                    _lastFlags = flags;
                }
            }
        }
        catch (Exception e)
        {
            Log($"Error en el diagnóstico: {e.Message}", Color.Red);
        }

        return ret;
    }

    // ------------------------------------------------------------------
    // Preguntas de antes de la batalla ("¿controlar al monstruo?"). En cada una se cambia UN byte para que la
    // opción "controlar" haga lo mismo que "automático". Se verifica todo el fragmento antes de escribir.
    // ------------------------------------------------------------------
    private sealed record Patch(string Name, uint Va, byte[] Expected, int Offset, byte NewValue);

    private static readonly byte[] PreguntaFimba =
    [
        0x83, 0xE8, 0x00, 0x74, 0x0F,       // sub eax,0 ; je opcion0
        0x83, 0xE8, 0x01, 0x75, 0xDB,       // sub eax,1 ; jne seguir esperando
        0xC6, 0x47, 0x05, 0x12,             // opcion1: mov byte [edi+5],0x12
        0x5F, 0x5E, 0x8B, 0xE5, 0x5D, 0xC3, //          pop edi ; pop esi ; mov esp,ebp ; pop ebp ; ret
        0xC6, 0x47, 0x05, 0x11              // opcion0: mov byte [edi+5],0x11   <- 0x12
    ];

    private static readonly Patch[] Patches =
    [
        // Torneos y batallas del juego: función 0x4A3A20 (mensaje 0x7E1).
        //   opción 0 (controlar) -> [esi+5] = 0x0D ; opción 1 (automático) -> [esi+5] = 0x0E
        //   (estado 0x0D = 0x4A3CC0, mensaje 0x7E2, byte +0x63 = 1: control manual;
        //    estado 0x0E = 0x4A3D10, mensaje 0x7E3, byte +0x63 = 0: automático)
        new("torneos", 0x4A3A85,
        [
            0x83, 0xE8, 0x00, 0x74, 0x0D,   // sub eax,0 ; je opcion0
            0x83, 0xE8, 0x01, 0x75, 0xCC,   // sub eax,1 ; jne seguir esperando
            0x5F, 0xC6, 0x46, 0x05, 0x0E,   // opcion1: pop edi ; mov byte [esi+5],0x0E
            0x5E, 0x5D, 0xC3,               //          pop esi ; pop ebp ; ret
            0x5F, 0xC6, 0x46, 0x05, 0x0D    // opcion0: pop edi ; mov byte [esi+5],0x0D   <- 0x0E
        ], 22, 0x0E),

        // IMa-FIMBA: otra máquina de estados (mensajes 0x812 a 0x81D), con dos preguntas de control
        // (mensajes 0x816 y 0x81B), iguales a la de los torneos:
        //   opción 0 -> [edi+5] = 0x11 (0x4A2470, mensaje 0x7E2, control manual)
        //   opción 1 -> [edi+5] = 0x12 (0x4A2560, mensaje 0x7E3, automático)
        new("IMa-FIMBA", 0x4A1DFD, PreguntaFimba, 23, 0x12),
        new("IMa-FIMBA (segunda pregunta)", 0x4A244D, PreguntaFimba, 23, 0x12)

        // OJO: 0x51DA30 (modo VS) NO es la pregunta de control: es la confirmación de la elección de monstruo.
    ];

    private void PatchBattleQuestion()
    {
        var aplicados = new List<string>();
        foreach (var p in Patches)
        {
            var addr = _exeBase + (nint)(p.Va - StaticBase);
            var actual = new byte[p.Expected.Length];
            Marshal.Copy(addr, actual, 0, actual.Length);

            var esperado = (byte[])p.Expected.Clone();
            esperado[p.Offset] = p.NewValue;
            if (actual.AsSpan().SequenceEqual(esperado))
            {
                aplicados.Add(p.Name);
                continue;
            }

            if (!actual.AsSpan().SequenceEqual(p.Expected))
            {
                Log($"No se encontró la pregunta de batalla de {p.Name} en esta versión del juego: no se cambia.",
                    Color.Red);
                continue;
            }

            var target = addr + p.Offset;
            VirtualProtect(target, 1, 0x40, out var old);
            Marshal.WriteByte(target, p.NewValue);
            VirtualProtect(target, 1, old, out _);
            aplicados.Add(p.Name);
        }

        if (aplicados.Count > 0)
            Log($"Batallas automáticas activas en: {string.Join(", ", aplicados)}.", Color.LightGreen);
    }

    [DllImport("kernel32.dll")]
    private static extern bool VirtualProtect(nint address, nint size, uint newProtect, out uint oldProtect);

    private string Ubicacion(nint p)
    {
        var off = (long)p - (long)_exeBase;
        return off is >= 0 and < 0x2200000 ? $"exe estático 0x{off + StaticBase:X}" : "memoria dinámica";
    }

    private void Log(string msg, Color? color = null) =>
        _logger.WriteLine($"[{_modConfig.ModId}] {msg}", color ?? Color.White);
}
