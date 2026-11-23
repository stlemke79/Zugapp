using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Diktat;

/// <summary>
/// Kleine Statusanzeige am unteren Bildschirmrand („Aufnahme läuft …“).
/// Nimmt nie den Fokus weg – sonst würde der Text im falschen Fenster landen.
/// </summary>
internal sealed class OverlayForm : Form
{
    private string _title = "";
    private string _hint = "";
    private Color _dot = Icons.Recording;
    private float _level;
    private bool _showLevel;
    private readonly System.Windows.Forms.Timer _pulse = new() { Interval = 50 };
    private int _tick;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.FromArgb(24, 24, 27);
        Size = new Size(420, 64);
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 10f);
        _pulse.Tick += (_, _) => { _tick++; Invalidate(); };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    public void ShowRecording(string hint, IntPtr nearWindow) =>
        ShowState("Aufnahme läuft …", hint, Icons.Recording, showLevel: true, nearWindow);

    public void ShowBusy(string title, string hint = "Esc = Abbrechen") =>
        ShowState(title, hint, Icons.Busy, showLevel: false, IntPtr.Zero);

    public void SetLevel(float level)
    {
        // Etwas glätten, damit der Balken nicht flackert.
        _level = Math.Max(level, _level * 0.85f);
    }

    private void ShowState(string title, string hint, Color dot, bool showLevel, IntPtr nearWindow)
    {
        _title = title;
        _hint = hint;
        _dot = dot;
        _showLevel = showLevel;
        _level = 0;

        if (!Visible || nearWindow != IntPtr.Zero)
        {
            var screen = nearWindow != IntPtr.Zero ? Screen.FromHandle(nearWindow) : Screen.FromPoint(Cursor.Position);
            var area = screen.WorkingArea;
            float scale = DeviceDpi / 96f;
            Size = new Size((int)(420 * scale), (int)(64 * scale));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 24);
        }

        using (var path = Icons.RoundedRect(new RectangleF(0, 0, Width, Height), 14))
            Region = new Region(path);

        if (!Visible) Show();
        _pulse.Start();
        Invalidate();
    }

    public void HideOverlay()
    {
        _pulse.Stop();
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = DeviceDpi / 96f;

        // pulsierender Punkt
        float pulse = 0.75f + 0.25f * (float)Math.Sin(_tick / 4.0);
        float r = 7 * s * pulse;
        float cx = 26 * s, cy = Height / 2f;
        using (var dot = new SolidBrush(_dot)) g.FillEllipse(dot, cx - r, cy - r, r * 2, r * 2);

        using var titleFont = new Font("Segoe UI Semibold", 11f);
        using var hintFont = new Font("Segoe UI", 8.5f);
        TextRenderer.DrawText(g, _title, titleFont, new Point((int)(46 * s), (int)(10 * s)), Color.White);
        TextRenderer.DrawText(g, _hint, hintFont, new Point((int)(46 * s), (int)(36 * s)), Color.FromArgb(161, 161, 170));

        if (_showLevel)
        {
            var bar = new RectangleF(Width - 110 * s, cy - 4 * s, 90 * s, 8 * s);
            using var track = new SolidBrush(Color.FromArgb(63, 63, 70));
            using var fill = new SolidBrush(Color.FromArgb(74, 222, 128));
            g.FillRectangle(track, bar);
            g.FillRectangle(fill, bar.X, bar.Y, bar.Width * Math.Min(1f, _level * 3f), bar.Height);
            _level *= 0.9f;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _pulse.Dispose();
        base.Dispose(disposing);
    }
}
