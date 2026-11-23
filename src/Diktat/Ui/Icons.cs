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

            float s = size / 32f;
            using var white = new SolidBrush(Color.White);
            using var pen = new Pen(Color.White, 2.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using (var capsule = RoundedRect(new RectangleF(12 * s, 6 * s, 8 * s, 13 * s), 4 * s))
                g.FillPath(white, capsule);
            g.DrawArc(pen, 9 * s, 10 * s, 14 * s, 12 * s, 0, 180);
            g.DrawLine(pen, 16 * s, 22 * s, 16 * s, 26 * s);
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
