using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DynamicEdge.Services;

/// Текущая раскладка клавиатуры активного окна («RU», «EN»…).
public static class KeyboardLayout
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint threadId);

    public static string Current()
    {
        try
        {
            uint tid = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            int lang = (int)(GetKeyboardLayout(tid).ToInt64() & 0xFFFF);
            return CultureInfo.GetCultureInfo(lang).TwoLetterISOLanguageName.ToUpperInvariant();
        }
        catch { return ""; }
    }
}
