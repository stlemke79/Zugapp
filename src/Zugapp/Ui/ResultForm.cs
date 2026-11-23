using System;
using System.Drawing;
using System.Windows.Forms;

namespace Zugapp;

/// <summary>Eigenes Fenster für den Text, wenn beim Diktieren kein Eingabefeld aktiv war.</summary>
internal sealed class ResultForm : Form
{
    private readonly TextBox _text;
    private readonly string _final;
    private readonly string _raw;

    public ResultForm(string finalText, string rawText, string? note)
    {
        _final = finalText;
        _raw = rawText;

        Text = "Zugapp Diktat";
        Icon = Icons.Microphone(Icons.Idle);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(640, 420);
        MinimumSize = new Size(420, 260);
        Font = new Font("Segoe UI", 10f);
        TopMost = true;

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(12, 12, 12, 0),
            Text = note ?? "Es war kein Eingabefeld aktiv – hier ist dein Text. Du kannst ihn bearbeiten und kopieren.",
            ForeColor = Color.FromArgb(82, 82, 91),
        };

        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = finalText.Replace("\r\n", "\n").Replace("\n", "\r\n"),
            Font = new Font("Segoe UI", 11f),
            BorderStyle = BorderStyle.FixedSingle,
        };
        var textHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4) };
        textHost.Controls.Add(_text);

        var showRaw = new CheckBox
        {
            Text = "Unbearbeitete Erkennung zeigen",
            AutoSize = true,
            Visible = !string.Equals(finalText, rawText, StringComparison.Ordinal),
            Margin = new Padding(0, 8, 0, 0),
        };
        showRaw.CheckedChanged += (_, _) => _text.Text = showRaw.Checked ? _raw : _final.Replace("\r\n", "\n").Replace("\n", "\r\n");

        var copy = new Button { Text = "Kopieren && schließen", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        copy.Click += (_, _) =>
        {
            TextInserter.TrySetClipboardText(_text.Text);
            Close();
        };
        var close = new Button { Text = "Schließen", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };

        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(12, 6, 12, 12),
        };
        buttons.Controls.AddRange(new Control[] { close, copy, showRaw });

        Controls.Add(textHost);
        Controls.Add(buttons);
        Controls.Add(info);

        AcceptButton = copy;
        CancelButton = close;
        Shown += (_, _) =>
        {
            Activate();
            _text.Focus();
            _text.SelectionStart = _text.TextLength;
        };
    }
}
