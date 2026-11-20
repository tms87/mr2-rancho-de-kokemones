using System.Diagnostics;
using Reloaded.Universal.Redirector.Interfaces;

namespace MRDX.Custom.RanchRules;

/// <summary>
///     Mensajes del santuario según las invocaciones que quedan en el año.
///     En vez de tocar la memoria del juego, se preparan copias modificadas de msg_park_en.obj (una por cantidad
///     restante) y se le pide al redirector de Reloaded que el juego lea la que corresponde. Se cambian:
///     - Los dos saludos (0x193D y 0x1C6D), antes de elegir disco: "This shrine can generate N more monsters this year..."
///       o, si no quedan, "The shrine has no more power left this year. Please come again next year."
///     - El rechazo (0x1AB1), si igual se elige un disco sin invocaciones: página 2 de "I regret to tell you that...".
///     - El cierre tras crear (0x1CEA, "Please take good care of it."), cuando queda una sola:
///       "It was the last one this year."
///     El juego lee el archivo al cargar la ciudad, así que un cambio se ve la próxima vez que se entra.
/// </summary>
public sealed class ShrineMessage
{
    private const string FileName = "msg_park_en.obj";

    // Mensajes originales: posición, longitud (hasta el 0xFF final, incluido) y comienzo esperado.
    private const int GreetingOffset = 0x193D;
    private const int GreetingLength = 0xB2;
    private static readonly byte[] GreetingStart = [0xD3, 0x00, 0x93, 0x03, 0xB5, 0x08, 0xB5, 0x27]; // "In"

    // Segundo saludo: "In this shrine, you can generate monsters also from slates."
    private const int Greeting2Offset = 0x1C6D;
    private const int Greeting2Length = 0x7D;

    // Pregunta al entrar al santuario: "Which kind of generating do you want?" (después de "Hello.").
    private const int ChoiceOffset = 0x29BE;
    private const int ChoiceLength = 0x52;
    private static readonly byte[] ChoiceStart = [0xD3, 0x00, 0x93, 0x03, 0xB5, 0x16, 0xB5, 0x21]; // "Wh"

    // Rechazo de esta versión: "I regret to inform you that... IMa does not permit us to generate this monster."
    private const int Reject2Offset = 0x2A78;
    private const int Reject2Length = 0xAB;

    private const int RejectOffset = 0x1AB1;
    private const int RejectLength = 0xFA;
    private static readonly byte[] RejectStart = [0xD3, 0x00, 0x9D, 0x03, 0xB5, 0x08, 0xB5, 0x61]; // "I "

    private const int FarewellOffset = 0x1CEA;
    private const int FarewellLength = 0x49;
    private static readonly byte[] FarewellStart = [0xD3, 0x00, 0x93, 0x03, 0xB5, 0x0F, 0xB5, 0x25]; // "Pl"

    private static readonly byte[] Open = [0xD3, 0x00, 0x93, 0x03];
    private static readonly byte[] Close = [0xD3, 0x00, 0x92, 0x03];
    private const byte NewLine = 0xFD;
    private const byte End = 0xFF;

    private readonly IRedirectorController? _redirector;
    private readonly Action<string> _log;
    private readonly string _workDir;
    private readonly byte[]? _original;
    private readonly HashSet<string> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _variants = new();
    private string? _target; // archivo que se le está sirviendo al juego (null = el original)

    public ShrineMessage(IRedirectorController? redirector, string workDir, Action<string> log)
    {
        _redirector = redirector;
        _log = log;
        _workDir = Path.Combine(workDir, "textos");

        if (_redirector == null)
        {
            _log("Sin el redirector de Reloaded no se pueden cambiar los mensajes del santuario (el límite sí funciona).");
            return;
        }

        try
        {
            var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!;
            var source = Path.GetFullPath(Path.Combine(exeDir, "Resources", "data", "mf2", "data", "obj", FileName));
            _original = File.ReadAllBytes(source);
            Directory.CreateDirectory(_workDir);
            BuildVariant(0); // comprueba ya que el archivo es el esperado
            _sources.Add(source);
            // El juego prueba primero Resources\mf2\... y, si no existe, Resources\data\mf2\...
            _sources.Add(Path.GetFullPath(Path.Combine(exeDir, "Resources", "mf2", "data", "obj", FileName)));
            _redirector.Loading += OnLoading;
        }
        catch (Exception e)
        {
            _original = null;
            _log($"No se pudieron preparar los mensajes del santuario: {e.Message}");
        }
    }

    /// <summary>Invocaciones que quedan este año; null = mensajes originales (límite desactivado o fecha desconocida).</summary>
    public void SetRemaining(int? remaining)
    {
        if (_original == null) return;
        try
        {
            var target = remaining == null ? null : VariantPath(Math.Max(0, remaining.Value));
            if (target == _target) return;
            _target = target;
            foreach (var source in _sources) Apply(source);
            _log($"Texto del santuario: {(target == null ? "original" : Path.GetFileName(target))}");
        }
        catch (Exception e)
        {
            _log($"No se pudo cambiar el mensaje del santuario: {e.Message}");
        }
    }

    private void Apply(string source)
    {
        _redirector!.RemoveRedirect(source);
        if (_target != null) _redirector.AddRedirect(source, _target);
    }

    // Por si el juego abre el archivo con otra forma de la ruta: la registramos para la próxima carga.
    private void OnLoading(string path)
    {
        if (!path.EndsWith(FileName, StringComparison.OrdinalIgnoreCase)) return;
        if (path.StartsWith(_workDir, StringComparison.OrdinalIgnoreCase)) return;
        if (_sources.Add(path)) Apply(path);
    }

    private string VariantPath(int remaining)
    {
        if (!_variants.TryGetValue(remaining, out var path))
        {
            path = BuildVariant(remaining);
            _variants[remaining] = path;
        }

        return path;
    }

    private string BuildVariant(int remaining)
    {
        var data = (byte[])_original!.Clone();
        Replace(data, GreetingOffset, GreetingLength, GreetingStart, Greeting(remaining));
        Replace(data, Greeting2Offset, Greeting2Length, GreetingStart, ShortGreeting(remaining));
        Replace(data, ChoiceOffset, ChoiceLength, ChoiceStart, Choice(remaining));
        // El juego solo relee los textos al entrar a la ciudad: si quedan invocaciones al entrar, el límite puede
        // alcanzarse sin salir, así que el rechazo usa un texto que vale tanto para eso como para un monstruo bloqueado.
        Replace(data, RejectOffset, RejectLength, RejectStart, remaining == 0 ? Rejection(_original) : NeutralRejection(_original));
        Replace(data, Reject2Offset, Reject2Length, RejectStart, remaining == 0 ? Rejection2(_original) : NeutralRejection2());
        if (remaining == 1) Replace(data, FarewellOffset, FarewellLength, FarewellStart, LastOne());

        var path = Path.Combine(_workDir, $"msg_park_en_quedan_{remaining}.obj");
        File.WriteAllBytes(path, data);
        return path;
    }

    private static void Replace(byte[] data, int offset, int length, byte[] expectedStart, byte[] message)
    {
        if (!data.AsSpan(offset, expectedStart.Length).SequenceEqual(expectedStart) || data[offset + length - 1] != End)
            throw new InvalidOperationException($"El archivo no tiene el mensaje esperado en 0x{offset:X}.");
        if (message.Length > length)
            throw new InvalidOperationException($"El mensaje nuevo no cabe en 0x{offset:X}.");

        message.CopyTo(data, offset);
        data.AsSpan(offset + message.Length, length - message.Length).Fill(End);
    }

    // ---------------- Textos ----------------

    private static byte[] Greeting(int remaining) => remaining == 0
        ? Text(Open, "The shrine has no more", NewLine, "power left this year.", NewLine, "Please come again", NewLine,
            "next year", Close, ".", End)
        : Text(Open, "This shrine can", NewLine,
            $"generate {remaining} more {(remaining == 1 ? "monster" : "monsters")}", NewLine,
            "this year from disc", NewLine, "stones or slates", Close, ".", End);

    /// <summary>Pregunta al entrar ("Which kind of generating do you want?"), con muy poco espacio.</summary>
    private static byte[] Choice(int remaining) => remaining == 0
        ? Text(Open, "No more power left", NewLine, "until next year", Close, ".", End)
        : Text(Open, "Which kind", Close, "?", NewLine, Open, $"{remaining} left this year", Close, ".", End);

    /// <summary>Segundo rechazo: página 1 original ("I regret to inform you that...") + página 2 nueva.</summary>
    private static byte[] Rejection2(byte[] file)
    {
        var original = file.AsSpan(Reject2Offset, Reject2Length).ToArray();
        var page1 = original[..(Array.IndexOf(original, (byte)0xFE) + 1)];
        return Text(page1, new byte[] { 0xD3, 0x00, 0x9D, 0x03 }, "the shrine has no", NewLine, "power left this year",
            new byte[] { 0xD3, 0x00, 0x9C, 0x03 }, ".", End);
    }

    /// <summary>Versión corta para el segundo saludo, que tiene menos espacio.</summary>
    private static byte[] ShortGreeting(int remaining) => remaining == 0
        ? Text(Open, "No more power left", NewLine, "this year. Come back", NewLine, "next year", Close, ".", End)
        : Text(Open, "This shrine can", NewLine, $"generate {remaining} more", NewLine,
            remaining == 1 ? "monster this year" : "monsters this year", Close, ".", End);

    /// <summary>Rechazo: página 1 original ("I regret to tell you that...") + página 2 nueva.</summary>
    private static byte[] Rejection(byte[] file)
    {
        var original = file.AsSpan(RejectOffset, RejectLength).ToArray();
        var page1 = original[..(Array.IndexOf(original, (byte)0xFE) + 1)];
        return Text(page1, new byte[] { 0xD3, 0x00, 0x9D, 0x03 }, "the shrine has no more", NewLine,
            "power left this year.", NewLine, "Please come again", NewLine, "next yea",
            new byte[] { 0xD3, 0x00, 0x9C, 0x03 }, "r...", End);
    }

    /// <summary>Rechazo neutro (0x1AB1): página 1 original + motivo doble.</summary>
    private static byte[] NeutralRejection(byte[] file)
    {
        var original = file.AsSpan(RejectOffset, RejectLength).ToArray();
        var page1 = original[..(Array.IndexOf(original, (byte)0xFE) + 1)];
        return Text(page1, new byte[] { 0xD3, 0x00, 0x9D, 0x03 }, "IMa does not permit it,", NewLine,
            "or the shrine has no", NewLine, "power left this year", new byte[] { 0xD3, 0x00, 0x9C, 0x03 }, ".", End);
    }

    /// <summary>Rechazo neutro de esta versión (0x2A78), en una sola página por falta de espacio.</summary>
    private static byte[] NeutralRejection2() => Text(new byte[] { 0xD3, 0x00, 0x9D, 0x03 }, "Sorry. IMa does not",
        NewLine, "permit it, or the shrine", NewLine, "has no power this year", new byte[] { 0xD3, 0x00, 0x9C, 0x03 },
        ".", End);

    private static byte[] LastOne() => Text(Open, "It was the last one", NewLine, "this year", Close, ".", End);

    /// <summary>Arma un mensaje con textos (se codifican) y bytes de control.</summary>
    private static byte[] Text(params object[] parts)
    {
        var bytes = new List<byte>();
        foreach (var part in parts)
            switch (part)
            {
                case string s: bytes.AddRange(Encode(s)); break;
                case byte b: bytes.Add(b); break;
                case byte[] arr: bytes.AddRange(arr); break;
            }

        return bytes.ToArray();
    }

    // Codificación del juego: 2 bytes por carácter (A=B500, a=B51A, 0=B557, espacio=B561, punto=B534, coma=B539).
    private static IEnumerable<byte> Encode(string text)
    {
        foreach (var c in text)
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
}
