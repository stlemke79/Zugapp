using System;
using System.Drawing;
using System.Windows.Forms;

namespace Diktat;

internal sealed class HistoryForm : Form
{
    private readonly History _history;
    private readonly ListView _list;
    private readonly TextBox _detail;

    public HistoryForm(History history)
    {
        _history = history;
        Text = "Diktat – Verlauf";
        Icon = Icons.Microphone(Icons.Idle);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(760, 520);
        Font = new Font("Segoe UI", 9.5f);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
        };
        _list.Columns.Add("Zeit", 130);
        _list.Columns.Add("Programm", 110);
        _list.Columns.Add("Text", 480);
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _list.DoubleClick += (_, _) => CopySelected();

        _detail = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 10.5f),
        };

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 260 };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(_detail);

        var copy = new Button { Text = "Kopieren", AutoSize = true };
        copy.Click += (_, _) => CopySelected();
        var clear = new Button { Text = "Verlauf löschen", AutoSize = true };
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Den gesamten Verlauf löschen?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _history.Clear();
                Fill();
            }
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.AddRange(new Control[] { copy, clear });

        Controls.Add(split);
        Controls.Add(buttons);
        Fill();
    }

    private void Fill()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var e in _history.Entries)
        {
            var item = new ListViewItem(new[] { e.Time.ToString("dd.MM.yyyy HH:mm"), e.App, e.FinalText.Replace('\n', ' ') }) { Tag = e };
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        _detail.Text = "";
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
    }

    private HistoryEntry? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as HistoryEntry : null;

    private void ShowSelected()
    {
        var e = Selected;
        if (e is null) return;
        var text = e.FinalText;
        if (!string.Equals(e.FinalText, e.RawText, StringComparison.Ordinal))
            text += "\n\n――― Unbearbeitete Erkennung ―――\n" + e.RawText;
        _detail.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    private void CopySelected()
    {
        if (Selected is { } e) TextInserter.TrySetClipboardText(e.FinalText);
    }
}
