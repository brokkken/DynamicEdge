using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DynamicEdge.Services;

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
public class MMDeviceEnumeratorCom { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    [PreserveSig] int OpenPropertyStore(int access, out IntPtr props);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback client);
    [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback client);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid ctx);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
    [PreserveSig] int GetMasterVolumeLevel(out float level);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint ch, float level, ref Guid ctx);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
    [PreserveSig] int GetChannelVolumeLevel(uint ch, out float level);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
    [PreserveSig] int VolumeStepUp(ref Guid ctx);
    [PreserveSig] int VolumeStepDown(ref Guid ctx);
    [PreserveSig] int QueryHardwareSupport(out uint mask);
    [PreserveSig] int GetVolumeRange(out float min, out float max, out float inc);
}

[ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(IntPtr notifyData);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioMeterInformation
{
    [PreserveSig] int GetPeakValue(out float peak);
    [PreserveSig] int GetMeteringChannelCount(out uint count);
    [PreserveSig] int GetChannelsPeakValues(uint count, [MarshalAs(UnmanagedType.LPArray)] float[] peaks);
    [PreserveSig] int QueryHardwareSupport(out uint mask);
}

[StructLayout(LayoutKind.Sequential)]
public struct AudioVolumeNotifyData { public Guid EventContext; public int Muted; public float MasterVolume; public uint Channels; }

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class VolumeCallback : IAudioEndpointVolumeCallback
{
    public Action<float, bool>? Handler;
    public int OnNotify(IntPtr data)
    {
        try
        {
            var d = Marshal.PtrToStructure<AudioVolumeNotifyData>(data);
            Handler?.Invoke(d.MasterVolume, d.Muted != 0);
        }
        catch { }
        return 0;
    }
}

/// <summary>Громкость/mute устройства по умолчанию (render = 0, capture = 1). Событие приходит из потока COM.</summary>
public sealed class AudioEndpoint : IDisposable
{
    readonly int _flow;
    IAudioEndpointVolume? _vol;
    VolumeCallback? _cb;
    string? _id;
    IAudioMeterInformation? _meter;
    public event Action<float, bool>? Changed;
    public bool Available => _vol != null;

    public AudioEndpoint(int flow) { _flow = flow; Attach(); }

    static string? DefaultId(int flow)
    {
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (en.GetDefaultAudioEndpoint(flow, 0, out var dev) != 0 || dev == null) return null;
            dev.GetId(out var id);
            return id;
        }
        catch { return null; }
    }

    void Detach()
    {
        try { if (_vol != null && _cb != null) _vol.UnregisterControlChangeNotify(_cb); } catch { }
        _vol = null; _cb = null; _meter = null;
    }

    void Attach()
    {
        Detach();
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (en.GetDefaultAudioEndpoint(_flow, 0, out var dev) != 0 || dev == null) return;
            dev.GetId(out _id);
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out var o) != 0) return;
            _vol = (IAudioEndpointVolume)o;
            var iidM = typeof(IAudioMeterInformation).GUID;
            if (dev.Activate(ref iidM, 23, IntPtr.Zero, out var om) == 0) _meter = (IAudioMeterInformation)om;
            _cb = new VolumeCallback { Handler = (v, m) => Changed?.Invoke(v, m) };
            _vol.RegisterControlChangeNotify(_cb);
        }
        catch (Exception ex) { Core.SettingsService.Log("Audio attach: " + ex.Message); }
    }

    /// Вызывать периодически: если устройство по умолчанию сменилось — переподключиться.
    public bool RecheckDevice()
    {
        var id = DefaultId(_flow);
        if (id == _id) return false;
        Attach();
        return true;
    }

    /// Проверка смены устройства по умолчанию: COM-запрос выполняется в фоне, переподключение — в потоке UI.
    public async void RecheckDeviceAsync()
    {
        try
        {
            var id = await Task.Run(() => DefaultId(_flow));
            if (id != _id) Attach();
        }
        catch { }
    }

    /// Пиковый уровень воспроизводимого звука 0..1 (для «танца» глазок).
    public float GetPeak()
    {
        try { if (_meter != null && _meter.GetPeakValue(out var p) == 0) return p; } catch { }
        return 0;
    }

    public (float volume, bool muted)? Read()
    {
        try
        {
            if (_vol == null) return null;
            _vol.GetMasterVolumeLevelScalar(out var v);
            _vol.GetMute(out var m);
            return (v, m);
        }
        catch { return null; }
    }

    public void Dispose() => Detach();
}
