using System;
using System.Diagnostics;
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
        var uia = Task.Run(CheckUiAutomation);
        var (verdict, detail) = uia.Wait(TimeSpan.FromMilliseconds(800)) ? uia.Result : (Verdict.Unknown, "Zeitlimit");
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
