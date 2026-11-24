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
        var (px, w, h) = Leer(archivo);
        var n = w * h;

        // Si ya viene con transparencia real (esquinas transparentes), no hay fondo que sacar: solo se achica.
        var alfaEsquinas = new[] { 0, w - 1, (h - 1) * w, h * w - 1 }.Average(i => (px[i] >>> 24) & 255);
        if (alfaEsquinas < 128) return (Achicar(px, w, h), "transparente");

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
        // Algunas IA dan un magenta "apagado" (p. ej. 213,45,133 o 205,74,122): alcanza con que se distinga del logo.
        if (kFuerza < 30) throw new InvalidDataException(T("el fondo no es magenta ni verde liso (mirá las esquinas de la imagen)", "the background is not flat magenta or green (check the image corners)"));

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

        return (Achicar(salida, w, h), verde ? "verde" : "magenta");
    }

    /// <summary>Lee cualquier imagen común (WebP aunque tenga extensión .png, PNG, JPG/JFIF, BMP, GIF) como ARGB.</summary>
    private static (int[] Px, int W, int H) Leer(string archivo)
    {
        using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Bgra32>(archivo);
        var px = new int[img.Width * img.Height];
        img.CopyPixelDataTo(System.Runtime.InteropServices.MemoryMarshal.AsBytes(px.AsSpan()));
        return (px, img.Width, img.Height);
    }

    /// <summary>
    ///     Lleva la imagen (ARGB) a 480x192. Si no viene en proporción 5:2 (p. ej. una imagen cuadrada), se recorta al
    ///     logo y se encaja centrado sin deformarlo.
    /// </summary>
    private static Bitmap Achicar(int[] salida, int w, int h)
    {
        using var limpia = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var d2 = limpia.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(salida, 0, d2.Scan0, w * h);
        limpia.UnlockBits(d2);

        var origen = new Rectangle(0, 0, w, h);
        var destino = new Rectangle(0, 0, 480, 192);
        if (Math.Abs((double)w / h - 2.5) > 0.25)
        {
            // Caja del logo con un pequeño margen: filas y columnas con una cantidad mínima de píxeles bastante
            // opacos (así un resto suelto del fondo o un brillo tenue no agranda el recorte).
            var porCol = new int[w];
            var porFila = new int[h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (((salida[y * w + x] >>> 24) & 255) > 128) { porCol[x]++; porFila[y]++; }

            int minCol = Math.Max(3, h / 100), minFila = Math.Max(3, w / 100);
            var x0 = Array.FindIndex(porCol, c => c >= minCol);
            var x1 = Array.FindLastIndex(porCol, c => c >= minCol);
            var y0 = Array.FindIndex(porFila, c => c >= minFila);
            var y1 = Array.FindLastIndex(porFila, c => c >= minFila);

            if (x1 >= 0)
            {
                var m = Math.Max(4, (x1 - x0) / 40);
                x0 = Math.Max(0, x0 - m); y0 = Math.Max(0, y0 - m);
                x1 = Math.Min(w - 1, x1 + m); y1 = Math.Min(h - 1, y1 + m);
                origen = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
                var esc = Math.Min(480.0 / origen.Width, 192.0 / origen.Height);
                int dw = (int)Math.Round(origen.Width * esc), dh = (int)Math.Round(origen.Height * esc);
                destino = new Rectangle((480 - dw) / 2, (192 - dh) / 2, dw, dh);
            }
        }

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
            gr.DrawImage(limpia, destino, origen.X, origen.Y, origen.Width, origen.Height, GraphicsUnit.Pixel, ia);
        }

        var final = new Bitmap(480, 192, PixelFormat.Format32bppArgb);
        using (var gr = Graphics.FromImage(final)) { gr.Clear(Color.Transparent); gr.DrawImage(chica, 0, 0); }
        return final;
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
