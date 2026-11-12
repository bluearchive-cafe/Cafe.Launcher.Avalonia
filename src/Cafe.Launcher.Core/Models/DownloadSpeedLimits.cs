namespace Cafe.Launcher.Core.Models;

public static class DownloadSpeedLimits
{
    public const string Unlimited = "unlimited";
    public const string Speed1MBs = "1MB/s";
    public const string Speed5MBs = "5MB/s";
    public const string Speed10MBs = "10MB/s";
    public const string Speed25MBs = "25MB/s";
    public const string Speed50MBs = "50MB/s";
    public static int ToBytesPerSecond(string limit) => limit switch
    {
        Speed1MBs => 1024 * 1024,
        Speed5MBs => 5 * 1024 * 1024,
        Speed10MBs => 10 * 1024 * 1024,
        Speed25MBs => 25 * 1024 * 1024,
        Speed50MBs => 50 * 1024 * 1024,
        _ => 0
    };
}
