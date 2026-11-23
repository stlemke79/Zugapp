using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Diktat;

/// <summary>Fügt Text in das zuvor aktive Fenster ein (über die Zwischenablage + Strg+V) und stellt die Zwischenablage danach wieder her.</summary>
internal static class TextInserter
{
    /// <returns>true, wenn der Text eingefügt wurde; false, wenn das Zielfenster nicht mehr erreichbar war.</returns>
    public static async Task<bool> InsertAsync(string text, IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero || !Native.IsWindow(targetWindow))
        {
            Log.Info("Einfügen: Zielfenster existiert nicht mehr");
            return false;
        }

        await WaitForModifierKeysReleasedAsync();

        bool front = false;
        for (int attempt = 1; attempt <= 5 && !front; attempt++)
        {
            front = BringToFront(targetWindow);
            if (!front) await Task.Delay(80);
        }
        if (!front)
        {
            var fg = Native.GetForegroundWindow();
            Log.Info($"Einfügen: Zielfenster ließ sich nicht nach vorne holen (vorne ist „{Native.WindowTitle(fg)}“ [{Native.ClassName(fg)}])");
            return false;
        }

        var backup = ClipboardBackup.Capture();
        if (!TrySetClipboardText(text))
        {
            Log.Info("Einfügen: Zwischenablage war blockiert");
            return false;
        }
        uint ourSequence = Native.GetClipboardSequenceNumber();

        await Task.Delay(40);
        var inputs = new[]
        {
            Native.Key(Native.VK_CONTROL, false),
            Native.Key(Native.VK_V, false),
            Native.Key(Native.VK_V, true),
            Native.Key(Native.VK_CONTROL, true),
        };
        uint sent = Native.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());
        int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        Log.Info(sent == inputs.Length
            ? $"Einfügen: Strg+V an „{Native.WindowTitle(targetWindow)}“ gesendet"
            : $"Einfügen: Tastendruck wurde von Windows blockiert (SendInput {sent}/{inputs.Length}, Fehler {error}) – läuft das Zielprogramm als Administrator?");
        if (sent != inputs.Length) return false;

        // Dem Zielprogramm Zeit geben, die Zwischenablage zu lesen; dann alten Inhalt zurückholen –
        // aber nur, wenn inzwischen niemand sonst etwas kopiert hat.
        await Task.Delay(800);
        if (Native.GetClipboardSequenceNumber() == ourSequence) backup.Restore();
        return true;
    }

    private static async Task WaitForModifierKeysReleasedAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline &&
               (Native.IsKeyDown(Native.VK_CONTROL) || Native.IsKeyDown(Native.VK_MENU) ||
                Native.IsKeyDown(Native.VK_SHIFT) || Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN)))
        {
            await Task.Delay(30);
        }
    }

    private static bool BringToFront(IntPtr hwnd)
    {
        if (Native.GetForegroundWindow() == hwnd) return true;
        if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, 9 /* SW_RESTORE */);

        uint targetThread = Native.GetWindowThreadProcessId(hwnd, out _);
        uint foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        uint ourThread = Native.GetCurrentThreadId();
        bool attached1 = foregroundThread != ourThread && Native.AttachThreadInput(ourThread, foregroundThread, true);
        bool attached2 = targetThread != ourThread && Native.AttachThreadInput(ourThread, targetThread, true);
        try
        {
            Native.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached1) Native.AttachThreadInput(ourThread, foregroundThread, false);
            if (attached2) Native.AttachThreadInput(ourThread, targetThread, false);
        }
        Thread.Sleep(60);
        return Native.GetForegroundWindow() == hwnd;
    }

    public static bool TrySetClipboardText(string text)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), copy: true);
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(50); // Zwischenablage gerade von einem anderen Programm belegt
            }
        }
        return false;
    }

    private sealed class ClipboardBackup
    {
        private readonly Dictionary<string, object>? _data;

        private ClipboardBackup(Dictionary<string, object>? data) => _data = data;

        public static ClipboardBackup Capture()
        {
            try
            {
                var source = Clipboard.GetDataObject();
                if (source is null) return new ClipboardBackup(null);
                var data = new Dictionary<string, object>();
                foreach (var format in source.GetFormats(autoConvert: false))
                {
                    try
                    {
#pragma warning disable WFDEV005 // Bewusst untypisiert: beliebige Formate 1:1 sichern
                        var value = source.GetData(format, autoConvert: false);
#pragma warning restore WFDEV005
                        if (value is not null) data[format] = value;
                    }
                    catch
                    {
                        // Manche Formate lassen sich nicht auslesen – überspringen.
                    }
                }
                return new ClipboardBackup(data);
            }
            catch
            {
                return new ClipboardBackup(null);
            }
        }

        public void Restore()
        {
            try
            {
                if (_data is null || _data.Count == 0)
                {
                    Clipboard.Clear();
                    return;
                }
                var obj = new DataObject();
                foreach (var (format, value) in _data) obj.SetData(format, false, value);
                Clipboard.SetDataObject(obj, copy: true);
            }
            catch (Exception ex)
            {
                Log.Info($"Zwischenablage konnte nicht wiederhergestellt werden: {ex.Message}");
            }
        }
    }
}
