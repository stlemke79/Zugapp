using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Zugapp;

/// <summary>Unsichtbares Fenster, das globale Tastenkürzel (RegisterHotKey) empfängt.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly HashSet<int> _registered = new();

    public event Action<int>? HotkeyPressed;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    public bool Register(int id, uint modifiers, int key)
    {
        Unregister(id);
        if (!Native.RegisterHotKey(Handle, id, modifiers | Native.MOD_NOREPEAT, (uint)key)) return false;
        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id)) Native.UnregisterHotKey(Handle, id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY) HotkeyPressed?.Invoke(m.WParam.ToInt32());
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in new List<int>(_registered)) Unregister(id);
        DestroyHandle();
    }
}

internal static class Hotkey
{
    public static string Describe(uint modifiers, int key)
    {
        var parts = new List<string>();
        if ((modifiers & Native.MOD_CONTROL) != 0) parts.Add("Strg");
        if ((modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & Native.MOD_SHIFT) != 0) parts.Add("Umschalt");
        if ((modifiers & Native.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyName((Keys)key));
        return string.Join(" + ", parts);
    }

    private static string KeyName(Keys key) => key switch
    {
        Keys.Space => "Leertaste",
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        Keys.Oemplus => "+",
        Keys.OemMinus => "-",
        Keys.Pause => "Pause",
        Keys.Scroll => "Rollen",
        Keys.Insert => "Einfg",
        _ => key.ToString(),
    };

    public static uint FromKeys(Keys modifiers)
    {
        uint m = 0;
        if (modifiers.HasFlag(Keys.Control)) m |= Native.MOD_CONTROL;
        if (modifiers.HasFlag(Keys.Alt)) m |= Native.MOD_ALT;
        if (modifiers.HasFlag(Keys.Shift)) m |= Native.MOD_SHIFT;
        return m;
    }
}
