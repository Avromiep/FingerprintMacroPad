using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;   // GDI types; WPF Media equivalents unused here
using Pen = System.Drawing.Pen;
using LinearGradientBrush = System.Drawing.Drawing2D.LinearGradientBrush;

namespace FingerprintMacroPad;

/// <summary>
/// Icon provider. The exe/window/title-bar use the multi-resolution <c>app.ico</c>
/// (each size drawn natively, so it stays crisp). The tray uses a native GDI render
/// because GDI's Icon can't decode PNG-compressed .ico frames. Both share one design.
/// </summary>
internal static class IconFactory
{
    private static readonly Uri IcoUri = new("pack://application:,,,/app.ico");

    private static readonly Color Glyph = Color.FromArgb(0x2F, 0x80, 0xFF);  // vivid electric blue

    // ── WPF (window + title bar) : load native frames from app.ico ──────────────
    public static BitmapFrame LoadWindowIcon() =>
        BitmapFrame.Create(IcoUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

    /// <summary>Nearest native frame to <paramref name="target"/> px (crisp, no rescale).</summary>
    public static ImageSource LoadFrame(int target)
    {
        var dec = BitmapDecoder.Create(IcoUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return dec.Frames.OrderBy(f => Math.Abs(f.PixelWidth - target)).First();
    }

    // ── Tray : native GDI render (GDI can't read PNG-framed .ico) ───────────────
    public static Icon CreateTrayIcon() => Icon.FromHandle(Render(32).GetHicon());

    private static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        // Solid fingerprint glyph on transparent (no button): bold blue ridges.
        int loops = size <= 40 ? 2 : 3;
        float stroke = MathF.Max(2.6f, size * 0.10f);
        float cx = size * 0.5f, cy = size * 0.5f;
        float baseR = size * 0.155f, outerR = size * 0.42f;
        float step = loops > 1 ? (outerR - baseR) / (loops - 1) : 0f;
        using (var pen = new Pen(Glyph, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            for (int i = 0; i < loops; i++)
            {
                float rx = baseR + i * step;
                float ry = rx * 1.12f;
                float gap = 50f;
                float gapCenter = 90f + (i % 2 == 0 ? -20f : 20f);
                g.DrawArc(pen, cx - rx, cy - ry, rx * 2, ry * 2, gapCenter + gap / 2f, 360f - gap);
            }
            float r0 = size * 0.062f;
            g.DrawArc(pen, cx - r0, cy - r0 * 1.1f - size * 0.01f, r0 * 2, r0 * 2.2f, 120f, 300f);
        }
        return bmp;
    }
}
