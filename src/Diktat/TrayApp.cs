using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Diktat;

/// <summary>
/// Hauptsteuerung: Symbol im Infobereich + globales Tastenkürzel.
/// Ablauf: Kürzel → Aufnahme → Kürzel → Whisper → (Claude) → Einfügen ins aktive Feld oder eigenes Fenster.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const int HotkeyDictate = 1;
    private const int HotkeyCancel = 2;
    private static readonly TimeSpan MaxRecording = TimeSpan.FromMinutes(10);

    private enum State { Idle, Recording, Processing }

    private AppSettings _settings = AppSettings.Load();
    private readonly History _history = History.Load();
    private readonly HotkeyWindow _hotkeys = new();
    private readonly NotifyIcon _tray;
    private readonly OverlayForm _overlay = new();
    private readonly Transcriber _transcriber = new();
    private readonly AudioRecorder _recorder = new();
    private readonly System.Windows.Forms.Timer _recordingWatch = new() { Interval = 500 };
    private readonly ToolStripMenuItem _lastItem;
    private readonly SynchronizationContext _ui;

    private readonly Icon _iconIdle = Icons.Microphone(Icons.Idle);
    private readonly Icon _iconRecording = Icons.Microphone(Icons.Recording);
    private readonly Icon _iconBusy = Icons.Microphone(Icons.Busy);

    private State _state = State.Idle;
    private FocusInfo? _target;
    private CancellationTokenSource? _processingCts;
    private Form? _openDialog;
    private Task<bool>? _download;

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _overlay.CreateControl();
        _ = _overlay.Handle; // Fenster-Handle jetzt erzeugen (UI-Thread)

        _lastItem = new ToolStripMenuItem("Letztes Diktat kopieren", null, (_, _) => CopyLast()) { Enabled = _history.Entries.Count > 0 };
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Diktat starten / beenden", null, (_, _) => Toggle()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(_lastItem);
        menu.Items.Add("Verlauf …", null, (_, _) => ShowDialog(new HistoryForm(_history)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Einstellungen …", null, (_, _) => OpenSettings());
        menu.Items.Add("Protokolldatei öffnen", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = _iconIdle,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowDialog(new HistoryForm(_history)); };

        _hotkeys.HotkeyPressed += OnHotkey;
        _recorder.Level += level => _ui.Post(_ => _overlay.SetLevel(level), null);
        _recordingWatch.Tick += (_, _) => { if (_state == State.Recording && _recorder.Duration > MaxRecording) Toggle(); };

        ApplyHotkey(showBalloon: true);
        _ = StartupAsync();
    }

    // ------------------------------------------------------------------ Start

    private async Task StartupAsync()
    {
        await Task.Delay(300); // Nachrichtenschleife erst anlaufen lassen
        if (!await EnsureModelAsync()) return;
        try
        {
            await _transcriber.WarmUpAsync(_settings.WhisperModel);
            if (_settings.AiEnabled && string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                Balloon("KI-Nachbearbeitung einrichten",
                    "Für die Überarbeitung mit Claude einen API-Schlüssel hinterlegen: Rechtsklick aufs Symbol → Einstellungen. Bis dahin wird der Text unbearbeitet eingefügt.",
                    ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Modell konnte nicht geladen werden", ex);
            Balloon("Spracherkennung konnte nicht geladen werden", ex.Message, ToolTipIcon.Error);
        }
    }

    private Task<bool> EnsureModelAsync()
    {
        if (Transcriber.IsDownloaded(_settings.WhisperModel)) return Task.FromResult(true);
        if (_download is { IsCompleted: false })
        {
            _openDialog?.Activate();
            return _download;
        }
        return _download = DownloadModelAsync();
    }

    private async Task<bool> DownloadModelAsync()
    {
        using var dlg = new DownloadForm(_settings.WhisperModel);
        var result = await ShowDialogAsync(dlg);
        if (result == DialogResult.OK) return true;
        if (dlg.Error is not null)
            Balloon("Download fehlgeschlagen", "Das Spracherkennungs-Modell konnte nicht geladen werden. Internetverbindung prüfen und erneut diktieren.", ToolTipIcon.Error);
        return false;
    }

    private void ApplyHotkey(bool showBalloon)
    {
        bool ok = _hotkeys.Register(HotkeyDictate, _settings.HotkeyModifiers, _settings.HotkeyKey);
        _tray.Text = Truncate($"Diktat – {_settings.HotkeyText}", 63);
        if (!ok)
        {
            Balloon("Tastenkürzel belegt",
                $"{_settings.HotkeyText} wird schon von einem anderen Programm benutzt. Bitte in den Einstellungen ein anderes wählen.",
                ToolTipIcon.Warning);
        }
        else if (showBalloon)
        {
            Balloon("Diktat ist bereit", $"{_settings.HotkeyText} drücken, sprechen, nochmal {_settings.HotkeyText} drücken.", ToolTipIcon.Info);
        }
    }

    // ------------------------------------------------------------- Ablauf

    private void OnHotkey(int id)
    {
        if (id == HotkeyDictate) Toggle();
        else if (id == HotkeyCancel) Cancel();
    }

    private void Toggle()
    {
        switch (_state)
        {
            case State.Idle:
                StartRecording();
                break;
            case State.Recording:
                _ = FinishAsync();
                break;
            case State.Processing:
                // Läuft noch – Kürzel ignorieren.
                break;
        }
    }

    private void StartRecording()
    {
        if (!Transcriber.IsDownloaded(_settings.WhisperModel))
        {
            _ = EnsureModelAsync();
            return;
        }

        // Ziel merken, BEVOR irgendein eigenes Fenster erscheint.
        _target = FocusDetector.Capture();
        Log.Info($"Aufnahme gestartet – Ziel: {_target.AppName}, Eingabefeld: {_target.IsEditable}");

        try
        {
            _recorder.Start(_settings.MicrophoneDevice);
        }
        catch (Exception ex)
        {
            Log.Error("Mikrofon konnte nicht geöffnet werden", ex);
            Balloon("Kein Mikrofon", "Das Mikrofon konnte nicht geöffnet werden. Ist eins angeschlossen und in Windows freigegeben (Datenschutz → Mikrofon)?", ToolTipIcon.Error);
            return;
        }

        _state = State.Recording;
        _tray.Icon = _iconRecording;
        _hotkeys.Register(HotkeyCancel, 0, Native.VK_ESCAPE);
        _recordingWatch.Start();
        var where = _target.IsEditable ? "wird ins aktive Fenster eingefügt" : "erscheint in eigenem Fenster";
        _overlay.ShowRecording($"{_settings.HotkeyText} = fertig  ·  Esc = abbrechen  ·  Text {where}", _target.Window);
    }

    private async Task FinishAsync()
    {
        _state = State.Processing;
        _recordingWatch.Stop();
        _tray.Icon = _iconBusy;
        _overlay.ShowBusy("Wird erkannt …");
        _processingCts = new CancellationTokenSource();
        var ct = _processingCts.Token;
        var target = _target;

        try
        {
            var samples = await _recorder.StopAsync();
            if (!AudioRecorder.ContainsSpeech(samples))
            {
                Balloon("Nichts gehört", "Es wurde keine Sprache erkannt. Mikrofon in den Einstellungen prüfen.", ToolTipIcon.Info);
                return;
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            string raw = await _transcriber.TranscribeAsync(samples, _settings, ct);
            var whisperTime = clock.Elapsed;
            Log.Info($"Erkannt: {raw.Length} Zeichen aus {samples.Length / (double)AudioRecorder.SampleRate:F1} s Audio in {whisperTime.TotalSeconds:F1} s (Modell {_settings.WhisperModel})");
            if (string.IsNullOrWhiteSpace(raw))
            {
                Balloon("Nichts verstanden", "Die Aufnahme enthielt keinen erkennbaren Text.", ToolTipIcon.Info);
                return;
            }

            string final = raw;
            string? note = null;
            if (_settings.AiEnabled)
            {
                if (string.IsNullOrWhiteSpace(_settings.ApiKey))
                {
                    note = "KI-Nachbearbeitung ist an, aber es ist kein API-Schlüssel hinterlegt – Text ist unbearbeitet.";
                }
                else
                {
                    _overlay.ShowBusy("KI überarbeitet den Text …");
                    try
                    {
                        clock.Restart();
                        final = await AiPostProcessor.ProcessAsync(raw, _settings, target?.AppName, ct);
                        Log.Info($"KI-Überarbeitung in {clock.Elapsed.TotalSeconds:F1} s (Modell {_settings.AiModel})");
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("KI-Nachbearbeitung fehlgeschlagen", ex);
                        note = "KI-Nachbearbeitung nicht möglich (" + Truncate(ex.Message, 120) + ") – Text ist unbearbeitet.";
                    }
                }
            }

            ct.ThrowIfCancellationRequested();
            _history.Add(new HistoryEntry(DateTime.Now, target?.AppName ?? "", raw, final), _settings.SaveHistory);
            _lastItem.Enabled = true;
            _overlay.HideOverlay();
            await DeliverAsync(final, raw, target, note);
        }
        catch (OperationCanceledException)
        {
            Log.Info("Diktat abgebrochen");
        }
        catch (Exception ex)
        {
            Log.Error("Diktat fehlgeschlagen", ex);
            Balloon("Diktat fehlgeschlagen", Truncate(ex.Message, 200), ToolTipIcon.Error);
        }
        finally
        {
            ResetToIdle();
        }
    }

    private async Task DeliverAsync(string text, string raw, FocusInfo? target, string? note)
    {
        if (target is { IsEditable: true } && await TextInserter.InsertAsync(text, target.Window))
        {
            if (note is not null) Balloon("Hinweis", note, ToolTipIcon.Warning);
            return;
        }

        Log.Info(target is { IsEditable: true } ? "Text im eigenen Fenster angezeigt (Einfügen nicht möglich)" : "Text im eigenen Fenster angezeigt (kein Eingabefeld aktiv)");
        var reason = target is { IsEditable: true }
            ? "Das ursprüngliche Fenster war nicht mehr erreichbar – hier ist dein Text."
            : null;
        var combined = string.Join(" ", new[] { reason, note }.Where(s => s is not null));
        ShowDialog(new ResultForm(text, raw, combined.Length > 0 ? combined : null));
    }

    private void Cancel()
    {
        if (_state == State.Recording)
        {
            _ = _recorder.StopAsync();
            ResetToIdle();
        }
        else if (_state == State.Processing)
        {
            _processingCts?.Cancel();
        }
    }

    private void ResetToIdle()
    {
        _state = State.Idle;
        _recordingWatch.Stop();
        _hotkeys.Unregister(HotkeyCancel);
        _overlay.HideOverlay();
        _tray.Icon = _iconIdle;
        _processingCts?.Dispose();
        _processingCts = null;
    }

    // ---------------------------------------------------------- Menüpunkte

    private void CopyLast()
    {
        if (_history.Entries.Count == 0) return;
        TextInserter.TrySetClipboardText(_history.Entries[0].FinalText);
        Balloon("Kopiert", "Das letzte Diktat ist in der Zwischenablage.", ToolTipIcon.None);
    }

    private async void OpenSettings()
    {
        if (_openDialog is SettingsForm) { _openDialog.Activate(); return; }
        using var form = new SettingsForm(_settings);
        if (await ShowDialogAsync(form) != DialogResult.OK) return;

        var oldModel = _settings.WhisperModel;
        _settings = form.Result;
        _settings.Save();
        ApplyHotkey(showBalloon: false);

        if (oldModel != _settings.WhisperModel && await EnsureModelAsync())
            _ = _transcriber.WarmUpAsync(_settings.WhisperModel);
    }

    private static void OpenLog()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true });
        }
        catch
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.Config) { UseShellExecute = true });
        }
    }

    // ------------------------------------------------------------- Hilfen

    private static void ShowDialog(Form form)
    {
        form.FormClosed += (_, _) => form.Dispose();
        form.Show();
        form.Activate();
    }

    private Task<DialogResult> ShowDialogAsync(Form form)
    {
        var tcs = new TaskCompletionSource<DialogResult>();
        _openDialog = form;
        form.FormClosed += (_, _) =>
        {
            _openDialog = null;
            tcs.TrySetResult(form.DialogResult);
        };
        form.Show();
        form.Activate();
        return tcs.Task;
    }

    private void Balloon(string title, string text, ToolTipIcon icon) =>
        _tray.ShowBalloonTip(4000, title, text, icon);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkeys.Dispose();
            _recorder.Dispose();
            _transcriber.Dispose();
            _recordingWatch.Dispose();
            _overlay.Dispose();
            _tray.Dispose();
            _iconIdle.Dispose();
            _iconRecording.Dispose();
            _iconBusy.Dispose();
        }
        base.Dispose(disposing);
    }
}
