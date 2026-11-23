using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Zugapp;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _s;
    private uint _hotMods;
    private int _hotKey;

    private readonly TextBox _hotkey = new() { ReadOnly = true, Width = 260, BackColor = SystemColors.Window };
    private readonly ComboBox _mic = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
    private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
    private readonly ComboBox _lang = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly TextBox _vocab = new() { Width = 340 };
    private readonly CheckBox _ai = new() { Text = "Text nach dem Diktieren mit Claude überarbeiten", AutoSize = true };
    private readonly TextBox _apiKey = new() { Width = 340, UseSystemPasswordChar = true };
    private readonly TextBox _aiModel = new() { Width = 220 };
    private readonly TextBox _aiExtra = new() { Width = 340, Height = 70, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _history = new() { Text = "Verlauf auf diesem PC speichern", AutoSize = true };
    private readonly CheckBox _autostart = new() { Text = "Mit Windows starten", AutoSize = true };

    private static readonly (string Code, string Name)[] Languages =
    {
        ("de", "Deutsch"), ("en", "Englisch"), ("auto", "Automatisch erkennen"),
    };

    public AppSettings Result => _s;

    public SettingsForm(AppSettings current)
    {
        _s = current.Clone();
        _hotMods = _s.HotkeyModifiers;
        _hotKey = _s.HotkeyKey;

        Text = "Zugapp Diktat – Einstellungen";
        Icon = Icons.Microphone(Icons.Idle);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(12);

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void Section(string title)
        {
            var l = new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 14, 0, 4) };
            grid.Controls.Add(l);
            grid.SetColumnSpan(l, 2);
        }
        void Row(string label, Control c, string? hint = null)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 12, 0) });
            if (hint is null)
            {
                grid.Controls.Add(c);
                return;
            }
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = Padding.Empty, WrapContents = false };
            stack.Controls.Add(c);
            stack.Controls.Add(new Label { Text = hint, AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 0, 0, 4) });
            grid.Controls.Add(stack);
        }

        // --- Bedienung ---
        Section("Bedienung");
        _hotkey.Text = Hotkey.Describe(_hotMods, _hotKey);
        _hotkey.KeyDown += OnHotkeyKeyDown;
        Row("Tastenkürzel:", _hotkey, "Ins Feld klicken und neue Kombination drücken. Einmal drücken = Aufnahme starten, nochmal = fertig. Esc bricht ab.");

        // --- Spracherkennung ---
        Section("Spracherkennung (läuft lokal auf diesem PC)");
        _mic.Items.Add("Windows-Standardmikrofon");
        foreach (var d in AudioRecorder.Devices()) _mic.Items.Add(d);
        _mic.SelectedIndex = Math.Clamp(_s.MicrophoneDevice + 1, 0, _mic.Items.Count - 1);
        Row("Mikrofon:", _mic);

        foreach (var m in Transcriber.Models)
            _model.Items.Add(m.Label + (Transcriber.IsDownloaded(m.Id) ? "  ✓" : ""));
        _model.SelectedIndex = Array.FindIndex(Transcriber.Models, m => m.Id == _s.WhisperModel) is var i and >= 0 ? i : 1;
        Row("Modell:", _model);

        foreach (var l in Languages) _lang.Items.Add(l.Name);
        _lang.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, l => l.Code == _s.Language));
        Row("Sprache:", _lang);

        _vocab.Text = _s.Vocabulary;
        Row("Namen / Fachbegriffe:", _vocab, "Kommagetrennt, z. B. „Zugapp, Müller-Lüdenscheidt, Kubernetes“ – hilft der Erkennung.");

        // --- KI ---
        Section("KI-Nachbearbeitung");
        _ai.Checked = _s.AiEnabled;
        grid.Controls.Add(new Label());
        grid.Controls.Add(_ai);

        _apiKey.Text = _s.ApiKey;
        Row("Claude-API-Schlüssel:", _apiKey, "Wird verschlüsselt nur für dein Windows-Konto gespeichert. Schlüssel gibt es unter console.anthropic.com.");
        _aiModel.Text = _s.AiModel;
        Row("Modell:", _aiModel, "Standard: claude-opus-5-5. Schneller und günstiger: claude-haiku-5-5.");
        _aiExtra.Text = _s.AiExtraInstructions;
        Row("Eigene Wünsche:", _aiExtra, "Optional, z. B. „Immer Sie-Form“ oder „Schweizer Rechtschreibung (ss statt ß)“.");
        _ai.CheckedChanged += (_, _) => UpdateAiEnabled();
        UpdateAiEnabled();

        // --- Sonstiges ---
        Section("Sonstiges");
        _history.Checked = _s.SaveHistory;
        _autostart.Checked = AppSettings.Autostart;
        grid.Controls.Add(new Label());
        grid.Controls.Add(_history);
        grid.Controls.Add(new Label());
        grid.Controls.Add(_autostart);

        var ok = new Button { Text = "Speichern", AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
        ok.Click += (_, _) => Save();
        var cancel = new Button { Text = "Abbrechen", AutoSize = true, Padding = new Padding(10, 2, 10, 2), DialogResult = DialogResult.Cancel };
        cancel.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void UpdateAiEnabled() => _apiKey.Enabled = _aiModel.Enabled = _aiExtra.Enabled = _ai.Checked;

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        var key = e.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
        if (key is Keys.Escape or Keys.Tab or Keys.Enter) return;

        uint mods = Hotkey.FromKeys(e.Modifiers);
        bool isFunctionKey = key is >= Keys.F1 and <= Keys.F24 or Keys.Pause or Keys.Scroll;
        if (mods == 0 && !isFunctionKey)
        {
            _hotkey.Text = "Bitte mit Strg, Alt oder Umschalt kombinieren";
            return;
        }
        _hotMods = mods;
        _hotKey = (int)key;
        _hotkey.Text = Hotkey.Describe(_hotMods, _hotKey);
    }

    private void Save()
    {
        if (_ai.Checked && string.IsNullOrWhiteSpace(_apiKey.Text))
        {
            MessageBox.Show(this,
                "Für die KI-Nachbearbeitung wird ein Claude-API-Schlüssel gebraucht.\nOhne Schlüssel wird der erkannte Text unbearbeitet eingefügt.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        _s.HotkeyModifiers = _hotMods;
        _s.HotkeyKey = _hotKey;
        _s.MicrophoneDevice = _mic.SelectedIndex - 1;
        _s.WhisperModel = Transcriber.Models[Math.Max(0, _model.SelectedIndex)].Id;
        _s.Language = Languages[Math.Max(0, _lang.SelectedIndex)].Code;
        _s.Vocabulary = _vocab.Text.Trim();
        _s.AiEnabled = _ai.Checked;
        _s.ApiKey = _apiKey.Text;
        _s.AiModel = string.IsNullOrWhiteSpace(_aiModel.Text) ? AiPostProcessor.DefaultModel : _aiModel.Text.Trim();
        _s.AiExtraInstructions = _aiExtra.Text.Trim();
        _s.SaveHistory = _history.Checked;

        try
        {
            AppSettings.Autostart = _autostart.Checked;
        }
        catch (Exception ex)
        {
            Log.Error("Autostart konnte nicht gesetzt werden", ex);
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
