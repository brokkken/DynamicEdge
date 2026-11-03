using System;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DynamicEdge.Core;

/// В MSIX автозапуск — через StartupTask (HKCU\Run там виртуализируется), в обычной установке — HKCU\Run.
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string TaskId = "DynamicEdgeStartup";

    public static async Task SetAsync(bool enable)
    {
        try
        {
            if (Native.IsPackaged())
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync(TaskId);
                if (enable) await task.RequestEnableAsync(); else task.Disable();
            }
            else
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (enable) k.SetValue("DynamicEdge", $"\"{Environment.ProcessPath}\" --autostart");
                else k.DeleteValue("DynamicEdge", false);
            }
        }
        catch (Exception ex) { SettingsService.Log("Autostart: " + ex.Message); }
    }
}
