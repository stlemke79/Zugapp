using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Diktat;

/// <summary>Zeichnet die Mikrofon-Symbole für den Infobereich (keine Bilddateien nötig).</summary>
internal static class Icons
{
    public static readonly Color Idle = Color.FromArgb(37, 99, 235);
    public static readonly Color Recording = Color.FromArgb(220, 38, 38);
    public static readonly Color Busy = Color.FromArgb(217, 119, 6);

    public static Icon Microphone(Color background, int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(background);
            g.FillEllipse(bg, 0, 0, size - 1, size - 1);

            DrawMicrophone(g, size / 2f, size / 2f, size * 0.62f, Color.White);
        }

        IntPtr handle = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>Zeichnet ein Mikrofon-Symbol, zentriert um (cx, cy), Gesamthöhe <paramref name="height"/>.</summary>
    public static void DrawMicrophone(Graphics g, float cx, float cy, float height, Color color)
    {
        float u = height / 20f; // Symbol ist 20 Einheiten hoch
        float top = cy - 10 * u;
        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, 2.2f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using (var capsule = RoundedRect(new RectangleF(cx - 4 * u, top, 8 * u, 13 * u), 4 * u))
            g.FillPath(brush, capsule);
        g.DrawArc(pen, cx - 7 * u, top + 4 * u, 14 * u, 12 * u, 0, 180);
        g.DrawLine(pen, cx, top + 16 * u, cx, top + 20 * u);
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
