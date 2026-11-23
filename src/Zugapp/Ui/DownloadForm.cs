using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Zugapp;

/// <summary>Lädt ein Whisper-Modell mit Fortschrittsanzeige herunter.</summary>
internal sealed class DownloadForm : Form
{
    private readonly ProgressBar _bar;
    private readonly Label _label;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _modelId;

    public Exception? Error { get; private set; }

    public DownloadForm(string modelId)
    {
        _modelId = modelId;
        var info = Transcriber.Model(modelId);
        Text = "Zugapp Diktat – Spracherkennung einrichten";
        Icon = Icons.Microphone(Icons.Idle);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(480, 150);
        Font = new Font("Segoe UI", 9.5f);

        _label = new Label
        {
            Location = new Point(16, 16),
            Size = new Size(448, 44),
            Text = $"Das Spracherkennungs-Modell wird einmalig heruntergeladen:\n{info.Label}",
        };
        _bar = new ProgressBar { Location = new Point(16, 68), Size = new Size(448, 22), Maximum = 1000 };
        var cancel = new Button { Text = "Abbrechen", Location = new Point(374, 104), Size = new Size(90, 30) };
        cancel.Click += (_, _) => _cts.Cancel();
        Controls.AddRange(new Control[] { _label, _bar, cancel });
        CancelButton = cancel;

        Shown += async (_, _) =>
        {
            var progress = new Progress<double>(p =>
            {
                _bar.Value = (int)(p * 1000);
                Text = $"Zugapp Diktat – Download {p:P0}";
            });
            try
            {
                await Transcriber.DownloadAsync(_modelId, progress, _cts.Token);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            catch (Exception ex)
            {
                Error = ex;
                Log.Error("Modell-Download fehlgeschlagen", ex);
                DialogResult = DialogResult.Abort;
                Close();
            }
        };
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.None) { _cts.Cancel(); e.Cancel = true; }
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}
