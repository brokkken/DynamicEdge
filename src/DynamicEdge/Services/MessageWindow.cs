using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;

namespace DynamicEdge.Services;

/// Невидимое message-only окно: слушает буфер обмена и глобальные хоткеи.
public sealed class MessageWindow : IDisposable
{
    const int WM_CLIPBOARDUPDATE = 0x031D, WM_HOTKEY = 0x0312;
    readonly HwndSource _src;
    readonly List<int> _ids = new();
    public event Action? ClipboardUpdated;
    public event Action<int>? HotkeyPressed;

    public MessageWindow()
    {
        var p = new HwndSourceParameters("DynamicEdgeMsg") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 };
        _src = new HwndSource(p);
        _src.AddHook(Hook);
        Core.Native.AddClipboardFormatListener(_src.Handle);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) { ClipboardUpdated?.Invoke(); handled = true; }
        else if (msg == WM_HOTKEY) { HotkeyPressed?.Invoke(w.ToInt32()); handled = true; }
        return IntPtr.Zero;
    }

    public void UnregisterAll() { foreach (var id in _ids) Core.Native.UnregisterHotKey(_src.Handle, id); _ids.Clear(); }

    /// "Ctrl+Alt+D" → регистрация. Возвращает false, если сочетание занято/некорректно.
    public bool Register(int id, string combo)
    {
        if (!TryParse(combo, out uint mods, out uint vk)) return false;
        bool ok = Core.Native.RegisterHotKey(_src.Handle, id, mods | 0x4000, vk);
        if (ok) _ids.Add(id);
        return ok;
    }

    public static bool TryParse(string combo, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;
        foreach (var raw in combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= 2; break;
                case "alt": mods |= 1; break;
                case "shift": mods |= 4; break;
                case "win": mods |= 8; break;
                default:
                    if (!Enum.TryParse<Key>(raw, true, out var key)) return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0 && mods != 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        Core.Native.RemoveClipboardFormatListener(_src.Handle);
        _src.Dispose();
    }
}
