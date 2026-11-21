using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EditorKokemon;

/// <summary>
///     Saca el fondo liso (magenta o verde) de un banner hecho por IA y lo deja en 480x192 con transparencia real.
///     Cada píxel = a * logo + (1 - a) * fondo: la "cantidad de fondo" da la transparencia, y al color del logo se le
///     quita esa parte (sin bordes de color). Una sombra sobre el fondo queda como sombra negra semitransparente.
/// </summary>
public static class Croma
{
    public static (Bitmap Resultado, string Fondo) Procesar(string archivo)
    {
        using var img = new Bitmap(archivo);
        int w = img.Width, h = img.Height, n = w * h;
        var px = new int[n];
        using (var copia = new Bitmap(img))
        {
            var d = copia.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(d.Scan0, px, 0, n);
            copia.UnlockBits(d);
        }

        // Color de fondo: promedio de las 4 esquinas.
        double kr = 0, kg = 0, kb = 0;
        var cnt = 0;
        var t = Math.Max(4, Math.Min(20, Math.Min(w, h) / 20));
        foreach (var (x0, y0) in new[] { (0, 0), (w - t, 0), (0, h - t), (w - t, h - t) })
            for (var y = y0; y < y0 + t; y++)
            for (var x = x0; x < x0 + t; x++)
            {
                var c = px[y * w + x];
                kr += (c >> 16) & 255; kg += (c >> 8) & 255; kb += c & 255; cnt++;
            }

        kr /= cnt; kg /= cnt; kb /= cnt;
        var verde = kg > kr && kg > kb;
        double Fuerza(double r, double g, double b) => verde ? g - Math.Max(r, b) : Math.Min(r, b) - g;
        var kFuerza = Fuerza(kr, kg, kb);
        if (kFuerza < 120) throw new InvalidDataException("el fondo no es magenta ni verde liso (mirá las esquinas de la imagen)");

        var salida = new int[n];
        for (var i = 0; i < n; i++)
        {
            var c = px[i];
            double r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
            var m = Fuerza(r, g, b);
            var a = Math.Clamp(1 - m / (kFuerza * 0.92), 0, 1);
            if (a < 0.04) { salida[i] = 0; continue; }
            var s = Math.Clamp(m / kFuerza, 0, 1);
            int R = (int)Math.Clamp((r - s * kr) / a, 0, 255), G = (int)Math.Clamp((g - s * kg) / a, 0, 255), B = (int)Math.Clamp((b - s * kb) / a, 0, 255);
            salida[i] = ((int)(a * 255) << 24) | (R << 16) | (G << 8) | B;
        }

        using var limpia = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var d2 = limpia.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(salida, 0, d2.Scan0, n);
        limpia.UnlockBits(d2);

        // A 480x192 (premultiplicado para que no aparezcan bordes grises).
        using var chica = new Bitmap(480, 192, PixelFormat.Format32bppPArgb);
        using (var gr = Graphics.FromImage(chica))
        {
            gr.Clear(Color.Transparent);
            gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
            gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
            gr.CompositingQuality = CompositingQuality.HighQuality;
            using var ia = new ImageAttributes();
            ia.SetWrapMode(WrapMode.TileFlipXY);
            gr.DrawImage(limpia, new Rectangle(0, 0, 480, 192), 0, 0, w, h, GraphicsUnit.Pixel, ia);
        }

        var final = new Bitmap(480, 192, PixelFormat.Format32bppArgb);
        using (var gr = Graphics.FromImage(final)) { gr.Clear(Color.Transparent); gr.DrawImage(chica, 0, 0); }
        return (final, verde ? "verde" : "magenta");
    }

    /// <summary>Vista previa: el banner sobre varios fondos (para ver bordes y sombra).</summary>
    public static Bitmap Vista(Bitmap banner)
    {
        var v = new Bitmap(960, 384);
        using var g = Graphics.FromImage(v);
        var fondos = new[] { Color.FromArgb(78, 80, 74), Color.Black, Color.White, Color.FromArgb(40, 90, 160) };
        for (var k = 0; k < 4; k++)
        {
            var r = new Rectangle(k % 2 * 480, k / 2 * 192, 480, 192);
            using var br = new SolidBrush(fondos[k]);
            g.FillRectangle(br, r);
            g.DrawImage(banner, r);
        }

        return v;
    }
}
