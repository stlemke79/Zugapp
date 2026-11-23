using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace Zugapp;

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
            return new FocusInfo(hwnd, true, app);

        // 2) Moderne Programme (Browser, Electron, UWP): UI Automation fragen – mit Zeitlimit, falls die App hängt.
        var uia = Task.Run(() => CheckUiAutomation());
        bool editable = uia.Wait(TimeSpan.FromMilliseconds(800)) && uia.Result;
        return new FocusInfo(hwnd, editable, app);
    }

    private static bool CheckUiAutomation()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null) return false;
            var current = element.Current;
            if (current.ProcessId == Environment.ProcessId) return false;
            if (!current.IsEnabled) return false;

            var type = current.ControlType;
            bool hasWritableValue = element.TryGetCurrentPattern(ValuePattern.Pattern, out var vp)
                                    && !((ValuePattern)vp).Current.IsReadOnly;

            if (type == ControlType.Edit)
                return !element.TryGetCurrentPattern(ValuePattern.Pattern, out var v) || !((ValuePattern)v).Current.IsReadOnly;

            if (type == ControlType.Document || type == ControlType.ComboBox || type == ControlType.Custom || type == ControlType.Group)
                return hasWritableValue;

            return false;
        }
        catch (Exception ex)
        {
            Log.Info($"UI Automation nicht verfügbar: {ex.Message}");
            return false;
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
