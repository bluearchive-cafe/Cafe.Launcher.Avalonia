namespace Cafe.Launcher.Avalonia.Models;

public enum GameOperationsRefreshMode
{
    Normal,
    SkipPersistedResume
}

public static class LaunchCheckModes
{
    public const string LocalManifest = "localManifest";
    public const string RemoteManifest = "remoteManifest";
    public const string None = "none";
}

public static class ProxyModes
{
    public const string Direct = "direct";
    public const string Auto = "auto";
    public const string System = "system";
}

public static class CloseBehaviors
{
    public const string Minimize = "minimize";
    public const string Exit = "exit";
}

public static class LauncherLanguages
{
    public const string Auto = "auto";
    public const string English = "en";
    public const string SimplifiedChinese = "zh-Hans";
    public const string TraditionalChinese = "zh-Hant";
    public const string Japanese = "ja";
}

public static class ThemeModes
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";
}

public static class MotionModes
{
    public const string System = "system";
    public const string Full = "full";
    public const string Reduced = "reduced";
}

public static class ThemeColorModes
{
    public const string Default = "default";
    public const string System = "system";
    public const string Wallpaper = "wallpaper";
    public const string Custom = "custom";
}

public static class ThemeColorExtractionAlgorithms
{
    public const string Octree = "octree";
    public const string CelebiScore = "celebiScore";
    public const string Wu = "wu";
    public const string Wsmeans = "wsmeans";
}

public static class ThemeColorVariants
{
    public const string TonalSpot = "tonalSpot";
    public const string Vibrant = "vibrant";
    public const string Expressive = "expressive";
    public const string Fidelity = "fidelity";
    public const string Content = "content";
    public const string Monochrome = "monochrome";
    public const string Neutral = "neutral";
    public const string Rainbow = "rainbow";
}

public static class NeutralColorStrategies
{
    public const string BrandBlue = "brandBlue";
    public const string SeedFollowing = "seedFollowing";
}

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

public static class PatchUrlGroups
{
    public const string Official = "official";
    public const string Cafe = "cafe";
}

public static class BackgroundSources
{
    public const string Bundled = "bundled";
    public const string Remote = "remote";
    public const string Custom = "custom";
}

public static class BackgroundFits
{
    public const string Fill = "fill";
    public const string Uniform = "uniform";
    public const string UniformToFill = "uniformToFill";
}

public static class UpdateChannels
{
    public const string Stable = "stable";
    public const string Beta = "beta";
}

public static class LogLevels
{
    public const string Verbose = "verbose";
    public const string Debug = "debug";
    public const string Information = "information";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Fatal = "fatal";
}

public static class ResourcePanelUidSources
{
    public const string Auto = "auto";
    public const string Custom = "custom";
}

public static class StatusDetailModes
{
    public const string Hidden = "hidden";
    public const string Compact = "compact";
}
