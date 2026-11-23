using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Diktat;

/// <summary>
/// Kleine runde Statusanzeige rechts oben: schwarzer Kreis mit pulsierendem roten Mikrofon (Aufnahme)
/// bzw. grauem Mikrofon mit drehendem Ring (Verarbeitung).
/// Als „layered window“ mit echter Transparenz, klickdurchlässig, und es nimmt nie den Fokus –
/// sonst würde der Text im falschen Fenster landen.
/// </summary>
internal sealed class OverlayForm : Form
{
    private const int BaseDiameter = 52; // bei 100 % Skalierung
    private const int BaseMargin = 16;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private bool _recording;
    private float _level;
    private int _tick;
    private int _size = BaseDiameter;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        _timer.Tick += (_, _) => { _tick++; Render(); };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST
                          | WS_EX_LAYERED | WS_EX_TRANSPARENT;
            return cp;
        }
    }

    public void ShowRecording(IntPtr nearWindow)
    {
        _recording = true;
        _level = 0;
        Place(nearWindow);
        Appear();
    }

    public void ShowBusy()
    {
        _recording = false;
        Appear();
    }

    public void SetLevel(float level) => _level = Math.Max(level, _level * 0.85f);

    public void HideOverlay()
    {
        _timer.Stop();
        Hide();
    }

    private void Place(IntPtr nearWindow)
    {
        var screen = nearWindow != IntPtr.Zero ? Screen.FromHandle(nearWindow) : Screen.FromPoint(Cursor.Position);
        uint dpi = nearWindow != IntPtr.Zero ? GetDpiForWindow(nearWindow) : 96;
        float scale = (dpi == 0 ? 96 : dpi) / 96f;
        _size = (int)Math.Round(BaseDiameter * scale);
        int margin = (int)Math.Round(BaseMargin * scale);
        var area = screen.WorkingArea;
        Bounds = new Rectangle(area.Right - _size - margin, area.Top + margin, _size, _size);
    }

    private void Appear()
    {
        Render();
        if (!Visible)
        {
            // Bewusst NICHT Form.TopMost/Show(): beides kann das Fenster aktivieren und dem Zielprogramm den Fokus nehmen.
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, Left, Top, _size, _size,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            Visible = true;
        }
        _timer.Start();
    }

    // ------------------------------------------------------------ Zeichnen

    private void Render()
    {
        int n = _size;
        using var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float s = n / (float)BaseDiameter;
            float c = n / 2f;

            using (var bg = new SolidBrush(Color.FromArgb(235, 0, 0, 0)))
                g.FillEllipse(bg, 1, 1, n - 2, n - 2);

            if (_recording)
            {
                // Pulsierender roter Ring; mit Stimme etwas kräftiger.
                double phase = (Math.Sin(_tick / 5.0) + 1) / 2; // 0..1, ca. 1 Puls pro Sekunde
                float voice = Math.Min(1f, _level * 3f);
                int alpha = (int)(70 + 120 * phase + 60 * voice);
                float width = (2.0f + 1.5f * (float)phase + 1.5f * voice) * s;
                using var ring = new Pen(Color.FromArgb(Math.Min(255, alpha), 239, 68, 68), width);
                float inset = 2 * s + width / 2;
                g.DrawEllipse(ring, inset, inset, n - 2 * inset, n - 2 * inset);

                float micScale = 1f + 0.06f * (float)phase;
                Icons.DrawMicrophone(g, c, c, 22 * s * micScale, Color.FromArgb(239, 68, 68));
                _level *= 0.9f;
            }
            else
            {
                // Verarbeitung: graues Mikrofon, drehender orangefarbener Bogen.
                using var arc = new Pen(Color.FromArgb(245, 158, 11), 3 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                float inset = 4.5f * s;
                g.DrawArc(arc, inset, inset, n - 2 * inset, n - 2 * inset, (_tick * 12) % 360, 100);
                Icons.DrawMicrophone(g, c, c, 22 * s, Color.FromArgb(161, 161, 170));
            }
        }
        Present(bmp);
    }

    private void Present(Bitmap bmp)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memDc, hBitmap);
        try
        {
            var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
            var src = new POINT();
            var dst = new POINT { x = Left, y = Top };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------ Win32

    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
