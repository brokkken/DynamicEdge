using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Core = DynamicEdge.Core;

namespace DynamicEdge.Services;

public readonly record struct SysSample(double Cpu, double Ram, double UsedGb, double TotalGb, double? TempC);

public sealed class SysInfoService
{
    Core.Native.FILETIME _i0, _k0, _u0;
    bool _have;
    readonly ThermalReader _therm = new();

    public SysSample Sample()
    {
        double cpu = 0;
        if (Core.Native.GetSystemTimes(out var i, out var k, out var u))
        {
            if (_have)
            {
                double di = i.Value - _i0.Value, dk = k.Value - _k0.Value, du = u.Value - _u0.Value;
                double tot = dk + du;
                if (tot > 0) cpu = Math.Clamp((tot - di) / tot * 100.0, 0, 100);
            }
            _i0 = i; _k0 = k; _u0 = u; _have = true;
        }
        var m = new Core.Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Core.Native.MEMORYSTATUSEX>() };
        double ram = 0, used = 0, total = 0;
        if (Core.Native.GlobalMemoryStatusEx(ref m))
        {
            ram = m.dwMemoryLoad;
            total = m.ullTotalPhys / 1073741824.0;
            used = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        }
        return new SysSample(cpu, ram, used, total, _therm.Read());
    }
}

/// Температура через PDH-счётчик «Thermal Zone Information» (английский путь — не зависит от языка Windows).
/// Доступен не на всех ПК: зависит от ACPI/BIOS. Причина недоступности пишется в log.txt.
sealed class ThermalReader
{
    [StructLayout(LayoutKind.Sequential)]
    struct PdhItem { public IntPtr Name; public uint Status; public double Value; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQuery(string? src, IntPtr user, out IntPtr q);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] static extern uint PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr q);
    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")] static extern uint PdhGetFormattedCounterArray(IntPtr c, uint fmt, ref uint size, out uint count, IntPtr buf);

    const uint PDH_MORE_DATA = 0x800007D2;
    const uint PDH_FMT_DOUBLE = 0x200;
    IntPtr _q, _c;
    bool _init, _ok, _loggedFail;

    void Init()
    {
        _init = true;
        try
        {
            uint r = PdhOpenQuery(null, IntPtr.Zero, out _q);
            if (r != 0) { Core.SettingsService.Log($"Thermal: PdhOpenQuery 0x{r:X}"); return; }
            r = PdhAddEnglishCounter(_q, @"\Thermal Zone Information(*)\Temperature", IntPtr.Zero, out _c);
            if (r != 0) { Core.SettingsService.Log($"Thermal: счётчик недоступен (0x{r:X}) — Windows не предоставляет тепловые зоны на этом ПК"); return; }
            PdhCollectQueryData(_q);
            _ok = true;
        }
        catch (Exception ex) { Core.SettingsService.Log("Thermal init: " + ex.Message); }
    }

    public double? Read()
    {
        if (!_init) Init();
        if (!_ok) return null;
        IntPtr buf = IntPtr.Zero;
        try
        {
            if (PdhCollectQueryData(_q) != 0) return null;
            uint size = 0, count;
            uint r = PdhGetFormattedCounterArray(_c, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero);
            if (r != PDH_MORE_DATA || size == 0) return null;
            buf = Marshal.AllocHGlobal((int)size);
            r = PdhGetFormattedCounterArray(_c, PDH_FMT_DOUBLE, ref size, out count, buf);
            if (r != 0) return null;
            int stride = Marshal.SizeOf<PdhItem>();
            double best = double.MinValue;
            for (int i = 0; i < count; i++)
            {
                var it = Marshal.PtrToStructure<PdhItem>(buf + i * stride);
                if (it.Status != 0 && it.Status != 0x00000001) continue;   // PDH_CSTATUS_VALID_DATA / NEW_DATA
                double t = it.Value;                                       // Кельвины
                if (t > 200) t -= 273.15;
                if (t > 0 && t < 130 && t > best) best = t;
            }
            if (best == double.MinValue && !_loggedFail) { _loggedFail = true; Core.SettingsService.Log("Thermal: счётчик есть, но значений нет"); }
            return best > double.MinValue ? best : null;
        }
        catch (Exception ex) { if (!_loggedFail) { _loggedFail = true; Core.SettingsService.Log("Thermal read: " + ex.Message); } return null; }
        finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
    }
}
