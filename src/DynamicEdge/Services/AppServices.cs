namespace DynamicEdge.Services;

public static class AppServices
{
    public static MediaService Media { get; } = new();
    public static NotificationService Notifications { get; } = new();
    public static BatteryService Battery { get; } = new();
    public static SysInfoService SysInfo { get; } = new();
    public static TimerService Timer { get; } = new();
    public static MicUsageService MicUsage { get; } = new();
    public static MessageWindow Msg { get; set; } = null!;
    public static AudioEndpoint Output { get; set; } = null!;
    public static AudioEndpoint Input { get; set; } = null!;
}
