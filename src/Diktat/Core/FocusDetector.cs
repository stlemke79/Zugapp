using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace Diktat;

internal sealed record FocusInfo(IntPtr Window, bool IsEditable, string AppName);

/// <summary>Findet heraus, ob im Vordergrund gerade ein Eingabefeld den Fokus hat.</summary>
internal static class FocusDetector
{
    private static readonly string[] ShellClasses =
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    public static FocusInfo Capture()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return new FocusInfo(IntPtr.Zero, false, "");

        uint threadId = Native.GetWindowThreadProcessId(hwnd, out uint pid);
        string app = ProcessName(pid);

        if (pid == (uint)Environment.ProcessId) return new FocusInfo(hwnd, false, app);
        if (Array.IndexOf(ShellClasses, Native.ClassName(hwnd)) >= 0) return new FocusInfo(hwnd, false, app);

        // 1) Klassische Windows-Programme (Word, Editor, Outlook …) zeigen eine System-Schreibmarke.
        var gti = new Native.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (Native.GetGUIThreadInfo(threadId, ref gti) && gti.hwndCaret != IntPtr.Zero)
        {
            Log.Info($"Fokus: {app} – Schreibmarke vorhanden → Editable");
            return new FocusInfo(hwnd, true, app);
        }

        // 2) Moderne Programme (Browser, Electron, UWP): UI Automation fragen – mit Zeitlimit, falls die App hängt.
        //    Grundsatz: lieber einfügen. Das eigene Fenster gibt es nur, wenn sicher KEIN Eingabefeld aktiv ist.
        //    (Browser und Electron-Apps melden ihre Eingabefelder oft unvollständig.)
        var uia = Task.Run(() => CheckWithRetries(app));
        var (verdict, detail) = uia.Wait(TimeSpan.FromMilliseconds(1200)) ? uia.Result : (Verdict.Unknown, "Zeitlimit");
        Log.Info($"Fokus: {app} – {detail} → {verdict}");
        return new FocusInfo(hwnd, verdict != Verdict.NotEditable, app);
    }

    private enum Verdict { Editable, NotEditable, Unknown }

    // Steuerelemente, in die man sicher nicht schreibt.
    private static readonly ControlType[] NonTextTypes =
    {
        ControlType.Button, ControlType.SplitButton, ControlType.CheckBox, ControlType.RadioButton,
        ControlType.List, ControlType.ListItem, ControlType.Tree, ControlType.TreeItem,
        ControlType.Menu, ControlType.MenuBar, ControlType.MenuItem, ControlType.Tab, ControlType.TabItem,
        ControlType.Hyperlink, ControlType.Image, ControlType.ScrollBar, ControlType.Slider, ControlType.Thumb,
        ControlType.ToolBar, ControlType.TitleBar, ControlType.Header, ControlType.HeaderItem,
        ControlType.DataGrid, ControlType.DataItem, ControlType.Table, ControlType.Calendar,
        ControlType.ProgressBar, ControlType.StatusBar, ControlType.ToolTip, ControlType.Separator,
    };

    // Chromium-Browser und Electron-Apps schalten ihre Barrierefreiheits-Infos erst ein, wenn jemand fragt.
    // Die erste Antwort ist dann oft nur der leere Fensterbereich („ClientView“) – kurz warten und nochmal fragen.
    private static (Verdict, string) CheckWithRetries(string app)
    {
        var result = CheckUiAutomation();
        for (int attempt = 0; attempt < 4 && result.Item1 == Verdict.Unknown; attempt++)
        {
            Thread.Sleep(150);
            result = CheckUiAutomation();
        }

        // Bleibt bei einem Browser nur ein Fensterbereich übrig, ist sicher kein Eingabefeld aktiv
        // (ein fokussiertes Feld meldet Chromium immer als Edit/Dokument mit Wert).
        if (result.Item1 == Verdict.Unknown && result.Item2.StartsWith("Pane", StringComparison.Ordinal) && IsChromiumBased(app))
            return (Verdict.NotEditable, result.Item2 + " (Browser ohne Eingabefeld)");
        return result;
    }

    private static readonly string[] ChromiumApps = { "chrome", "msedge", "vivaldi", "brave", "opera", "opera_gx", "arc", "thorium", "chromium" };

    private static bool IsChromiumBased(string app) =>
        Array.Exists(ChromiumApps, a => string.Equals(a, app, StringComparison.OrdinalIgnoreCase));

    private static (Verdict, string) CheckUiAutomation()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null) return (Verdict.Unknown, "kein Fokus-Element");
            var current = element.Current;
            var type = current.ControlType;
            var detail = $"{type.ProgrammaticName.Replace("ControlType.", "")} \"{current.ClassName}\"";

            if (current.ProcessId == Environment.ProcessId) return (Verdict.NotEditable, detail + " (eigenes Fenster)");

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
            {
                bool readOnly = ((ValuePattern)vp).Current.IsReadOnly;
                return (readOnly ? Verdict.NotEditable : Verdict.Editable, detail + (readOnly ? " schreibgeschützt" : " beschreibbar"));
            }

            if (type == ControlType.Edit) return (Verdict.Editable, detail);
            if (Array.IndexOf(NonTextTypes, type) >= 0) return (Verdict.NotEditable, detail);
            return (Verdict.Unknown, detail);
        }
        catch (Exception ex)
        {
            return (Verdict.Unknown, $"UI Automation nicht verfügbar: {ex.Message}");
        }
    }

    private static string ProcessName(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return "";
        }
    }
}
