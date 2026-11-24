namespace MRDX.Custom.ItemTolerance;

/// <summary>
///     Texturas propias de las comidas nuevas, armadas al arrancar sobre el modelo original del jugador (no se
///     distribuye ningún archivo del juego).
///
///     Un .itm es un modelo de PlayStation (TMD) seguido de texturas TIM de 4 bits: el u32 en 0x0C dice dónde empiezan.
///     Cada TIM: id 0x10, flags (8 = con paleta), bloque de paleta (largo u32, x, y, ancho 16, alto 1, 16 colores de
///     15 bits: R, G, B de 5 bits y el bit de transparencia arriba) y bloque de imagen. Solo se cambian los colores de
///     las paletas; el dibujo queda igual.
/// </summary>
public static class Texturas
{
    private abstract record Cambio(int Tim);

    /// <summary>Intercambia dos canales de color (R↔G o R↔B).</summary>
    private sealed record Intercambio(int Tim, char Con) : Cambio(Tim);

    /// <summary>
    ///     Reemplaza los colores por un degradé de <paramref name="Sombra" /> a <paramref name="Luz" />, según el brillo
    ///     de cada color dentro de la paleta. <paramref name="SoloRojizos" />: solo los colores rojizos (la carne), con el
    ///     brillo como suma de canales; si no, todos menos el negro y el blanco puro, con brillo ponderado.
    /// </summary>
    private sealed record Degrade(int Tim, (int R, int G, int B) Sombra, (int R, int G, int B) Luz, bool SoloRojizos = false)
        : Cambio(Tim);

    // Por comida (número de objeto): los cambios sobre el modelo de origen.
    private static readonly Dictionary<uint, Cambio[]> Recetas = new()
    {
        // Squash, sobre el Mango: cáscara de anco, tallo tostado y la estrella de hojas como base del tallo.
        [62] =
        [
            new Degrade(2, (196, 152, 100), (246, 218, 170)),
            new Degrade(1, (140, 120, 75), (205, 190, 140)),
            new Degrade(0, (120, 100, 60), (190, 170, 120))
        ],
        // Chicken, sobre la Meat: la carne pasa a dorado de pollo asado; el hueso queda igual.
        [40] =
        [
            new Degrade(1, (115, 62, 18), (240, 182, 92), SoloRojizos: true),
            new Degrade(2, (115, 62, 18), (240, 182, 92), SoloRojizos: true)
        ],
        // Green Tea, sobre el Colart Tea: té verde, y bandas y hojas azules en la jarra y la taza.
        [66] =
        [
            new Intercambio(5, 'G'),
            new Intercambio(0, 'B'),
            new Intercambio(3, 'B')
        ]
    };

    public static bool TieneReceta(uint comida) => Recetas.ContainsKey(comida);

    /// <summary>Copia del modelo con las paletas de la receta aplicadas.</summary>
    public static byte[] Aplicar(uint comida, byte[] original)
    {
        var b = (byte[])original.Clone();
        var tims = Tims(b);
        foreach (var cambio in Recetas[comida])
        {
            if (cambio.Tim >= tims.Count) throw new InvalidDataException($"el modelo no tiene la textura {cambio.Tim}");
            var paleta = tims[cambio.Tim];
            switch (cambio)
            {
                case Intercambio i:
                    for (var k = 0; k < 16; k++)
                    {
                        var (r, g, bl, stp) = Leer(b, paleta, k);
                        if (r == 0 && g == 0 && bl == 0 && stp == 0) continue; // transparente
                        Escribir(b, paleta, k, i.Con == 'G' ? (g, r, bl) : (bl, g, r), stp);
                    }

                    break;
                case Degrade d:
                    var brillo = new Dictionary<int, double>();
                    for (var k = 0; k < 16; k++)
                    {
                        var (r, g, bl, stp) = Leer(b, paleta, k);
                        var c = r | (g << 5) | (bl << 10);
                        if (d.SoloRojizos)
                        {
                            if ((c | stp) != 0 && r > 1.5 * g && r > 3) brillo[k] = r + g + bl;
                        }
                        else if ((c | stp) != 0 && c != 0x7FFF)
                            brillo[k] = 0.3 * r + 0.59 * g + 0.11 * bl;
                    }

                    if (brillo.Count == 0) break;
                    double min = brillo.Values.Min(), max = brillo.Values.Max();
                    if (max == min) max = min + 1;
                    foreach (var (k, l) in brillo)
                    {
                        var t = (l - min) / (max - min);
                        int Canal(int s, int z) => (int)Math.Round((s + (z - s) * t) * 31 / 255);
                        Escribir(b, paleta, k, (Canal(d.Sombra.R, d.Luz.R), Canal(d.Sombra.G, d.Luz.G), Canal(d.Sombra.B, d.Luz.B)),
                            Leer(b, paleta, k).Stp);
                    }

                    break;
            }
        }

        return b;
    }

    /// <summary>Posición del primer color de la paleta de cada TIM del modelo.</summary>
    private static List<int> Tims(byte[] b)
    {
        var lista = new List<int>();
        var o = (int)BitConverter.ToUInt32(b, 0x0C);
        while (o + 8 <= b.Length && BitConverter.ToUInt32(b, o) == 0x10)
        {
            if ((BitConverter.ToUInt32(b, o + 4) & 8) == 0) throw new InvalidDataException("textura sin paleta");
            var p = o + 8;
            lista.Add(p + 12);
            p += (int)BitConverter.ToUInt32(b, p); // paleta
            p += (int)BitConverter.ToUInt32(b, p); // imagen
            o = p;
        }

        if (lista.Count == 0) throw new InvalidDataException("el modelo no tiene texturas");
        return lista;
    }

    private static (int R, int G, int B, int Stp) Leer(byte[] b, int paleta, int k)
    {
        var c = BitConverter.ToUInt16(b, paleta + k * 2);
        return (c & 31, (c >> 5) & 31, (c >> 10) & 31, c & 0x8000);
    }

    private static void Escribir(byte[] b, int paleta, int k, (int R, int G, int B) rgb, int stp)
    {
        var c = (ushort)(stp | rgb.R | (rgb.G << 5) | (rgb.B << 10));
        BitConverter.GetBytes(c).CopyTo(b, paleta + k * 2);
    }
}
