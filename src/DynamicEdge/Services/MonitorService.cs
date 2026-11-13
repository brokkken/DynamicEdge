using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DynamicEdge.Services;

public readonly record struct MonitorSample(double Cpu, double Ram, double UsedGb, double TotalGb,
                                            double? Gpu, double? Disk, double? UpMbps, double? DownMbps, double? TempC);

/// Обёртка над PDH-счётчиком с подстановочными инстансами (английские имена, не зависят от языка Windows).
public sealed class PdhCounter : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct Item { public IntPtr Name; public uint Status; public double Value; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQuery(string? src, IntPtr user, out IntPtr q);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] static extern uint PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr q);
    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")] static extern uint PdhGetFormattedCounterArray(IntPtr c, uint fmt, ref uint size, out uint count, IntPtr buf);
    [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr q);

    const uint PDH_MORE_DATA = 0x800007D2, PDH_FMT_DOUBLE = 0x200;
    IntPtr _q, _c;

    public static PdhCounter? Create(string englishPath)
    {
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out var q) != 0) return null;
            if (PdhAddEnglishCounter(q, englishPath, IntPtr.Zero, out var c) != 0) { PdhCloseQuery(q); return null; }
            PdhCollectQueryData(q);
            return new PdhCounter { _q = q, _c = c };
        }
        catch { return null; }
    }

    public List<(string Name, double Value)> Read()
    {
        var list = new List<(string, double)>();
        IntPtr buf = IntPtr.Zero;
        try
        {
            if (PdhCollectQueryData(_q) != 0) return list;
            uint size = 0, count;
            if (PdhGetFormattedCounterArray(_c, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero) != PDH_MORE_DATA || size == 0) return list;
            size += size / 4 + 256;                                  // запас на появившиеся инстансы
            buf = Marshal.AllocHGlobal((int)size);
            if (PdhGetFormattedCounterArray(_c, PDH_FMT_DOUBLE, ref size, out count, buf) != 0) return list;
            int stride = Marshal.SizeOf<Item>();
            for (int i = 0; i < count; i++)
            {
                var it = Marshal.PtrToStructure<Item>(buf + i * stride);
                if (it.Status != 0 && it.Status != 1) continue;     // VALID_DATA / NEW_DATA
                list.Add((Marshal.PtrToStringUni(it.Name) ?? "", it.Value));
            }
        }
        catch { }
        finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        return list;
    }

    public void Dispose() { try { if (_q != IntPtr.Zero) PdhCloseQuery(_q); } catch { } _q = IntPtr.Zero; }
}

/// CPU/RAM/температура (SysInfoService) + видеокарта, диск и сеть через счётчики производительности Windows.
public sealed class MonitorService
{
    readonly SysInfoService _sys;
    readonly object _lock = new();
    PdhCounter? _gpu, _disk, _rx, _tx;
    bool _init;

    public MonitorService(SysInfoService sys) { _sys = sys; }

    public Task<MonitorSample> SampleAsync() => Task.Run(Sample);

    static readonly string[] SkipNet = { "isatap", "teredo", "6to4", "loopback", "pseudo" };

    static double? SumNet(PdhCounter? c)
    {
        if (c == null) return null;
        var rows = c.Read();
        if (rows.Count == 0) return null;
        double sum = 0;
        foreach (var (n, v) in rows)
        {
            string l = n.ToLowerInvariant();
            if (SkipNet.Any(l.Contains)) continue;
            sum += v;
        }
        return sum * 8 / 1_000_000.0;                                // байты/с → Мбит/с
    }

    MonitorSample Sample()
    {
        lock (_lock)
        {
            if (!_init)
            {
                _init = true;
                _gpu = PdhCounter.Create(@"\GPU Engine(*)\Utilization Percentage");
                _disk = PdhCounter.Create(@"\PhysicalDisk(_Total)\% Disk Time");
                _rx = PdhCounter.Create(@"\Network Interface(*)\Bytes Received/sec");
                _tx = PdhCounter.Create(@"\Network Interface(*)\Bytes Sent/sec");
            }
            var b = _sys.Sample();

            double? gpu = null;
            if (_gpu != null)
            {
                var groups = new Dictionary<string, double>();
                foreach (var (n, v) in _gpu.Read())
                {
                    int i = n.IndexOf("engtype_", StringComparison.Ordinal);
                    string k = i >= 0 ? n[(i + 8)..] : "x";
                    groups[k] = groups.GetValueOrDefault(k) + v;
                }
                if (groups.Count > 0) gpu = Math.Clamp(groups.Values.Max(), 0, 100);
            }

            double? disk = null;
            if (_disk != null) { var d = _disk.Read(); if (d.Count > 0) disk = Math.Clamp(d[0].Value, 0, 100); }

            return new MonitorSample(b.Cpu, b.Ram, b.UsedGb, b.TotalGb, gpu, disk, SumNet(_tx), SumNet(_rx), b.TempC);
        }
    }
}
