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

/// Температура через PDH-счётчик «Thermal Zone Information». Доступен не на всех ПК (зависит от ACPI/BIOS).
sealed class ThermalReader
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    struct PdhValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Double; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQuery(string? src, IntPtr user, out IntPtr q);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] static extern uint PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr q);
    [DllImport("pdh.dll")] static extern uint PdhGetFormattedCounterValue(IntPtr c, uint fmt, out uint type, out PdhValue v);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhExpandWildCardPathW")] static extern uint PdhExpand(string? src, string wild, IntPtr buf, ref uint size, uint flags);

    IntPtr _q;
    readonly List<IntPtr> _counters = new();
    bool _init, _ok;

    void Init()
    {
        _init = true;
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _q) != 0) return;
            uint size = 0;
            const string wild = @"\Thermal Zone Information(*)\Temperature";
            PdhExpand(null, wild, IntPtr.Zero, ref size, 0);      // PDH_MORE_DATA → size
            if (size == 0) return;
            var buf = Marshal.AllocHGlobal((int)size * 2);
            try
            {
                if (PdhExpand(null, wild, buf, ref size, 0) != 0) return;
                var all = Marshal.PtrToStringUni(buf, (int)size) ?? "";
                foreach (var path in all.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                    if (PdhAddEnglishCounter(_q, path, IntPtr.Zero, out var c) == 0) _counters.Add(c);
            }
            finally { Marshal.FreeHGlobal(buf); }
            _ok = _counters.Count > 0;
            if (_ok) PdhCollectQueryData(_q);
        }
        catch { _ok = false; }
    }

    public double? Read()
    {
        if (!_init) Init();
        if (!_ok) return null;
        try
        {
            if (PdhCollectQueryData(_q) != 0) return null;
            double best = double.MinValue;
            foreach (var c in _counters)
            {
                if (PdhGetFormattedCounterValue(c, 0x200, out _, out var v) != 0) continue;
                double t = v.Double;                      // Кельвины
                if (t > 200) t -= 273.15;
                if (t > 0 && t < 130 && t > best) best = t;
            }
            return best > double.MinValue ? best : null;
        }
        catch { return null; }
    }
}
