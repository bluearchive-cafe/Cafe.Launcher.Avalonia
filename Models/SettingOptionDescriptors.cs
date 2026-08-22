using System;
using System.Collections.Generic;

namespace Cafe.Launcher.Avalonia.Models;

/// <summary>
/// Describes a persisted string setting option with its stable code and display resource key.
/// </summary>
public sealed record SettingOptionDescriptor(string Code, string DisplayResourceKey);

/// <summary>
/// Central registry for persisted enumerated setting codes, option ordering, localization keys, and invalid-value fallbacks.
/// </summary>
public static class SettingOptionDescriptors
{
    public static IReadOnlyList<SettingOptionDescriptor> BackgroundSource { get; } =
    [
        new(BackgroundSources.Bundled, "backgroundSourceBundled"),
        new(BackgroundSources.Remote, "backgroundSourceRemote"),
        new(BackgroundSources.Custom, "backgroundSourceCustom")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> BackgroundFit { get; } =
    [
        new(BackgroundFits.Fill, "backgroundFitFill"),
        new(BackgroundFits.Uniform, "backgroundFitUniform"),
        new(BackgroundFits.UniformToFill, "backgroundFitUniformToFill")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> ThemeColor { get; } =
    [
        new(ThemeColorModes.Default, "themeColorDefault"),
        new(ThemeColorModes.System, "themeColorSystem"),
        new(ThemeColorModes.Wallpaper, "themeColorWallpaper"),
        new(ThemeColorModes.Custom, "themeColorCustom")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> LaunchCheckMode { get; } =
    [
        new(LaunchCheckModes.LocalManifest, "launchCheckLocalManifest"),
        new(LaunchCheckModes.RemoteManifest, "launchCheckRemoteManifest"),
        new(LaunchCheckModes.None, "launchCheckNone")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> ProxyMode { get; } =
    [
        new(ProxyModes.Auto, "proxyAuto"),
        new(ProxyModes.Direct, "proxyDirect"),
        new(ProxyModes.System, "proxySystem")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> PatchUrlGroup { get; } =
    [
        new(PatchUrlGroups.Official, "downloadSourceOfficial"),
        new(PatchUrlGroups.Cafe, "downloadSourceCafe")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> DownloadSpeedLimit { get; } =
    [
        new(DownloadSpeedLimits.Unlimited, "speedUnlimited"),
        new(DownloadSpeedLimits.Speed1MBs, "speed1MBs"),
        new(DownloadSpeedLimits.Speed5MBs, "speed5MBs"),
        new(DownloadSpeedLimits.Speed10MBs, "speed10MBs"),
        new(DownloadSpeedLimits.Speed25MBs, "speed25MBs"),
        new(DownloadSpeedLimits.Speed50MBs, "speed50MBs")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> CloseBehavior { get; } =
    [
        new(CloseBehaviors.Minimize, "closeBehaviorMinimize"),
        new(CloseBehaviors.Exit, "closeBehaviorExit")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> UpdateChannel { get; } =
    [
        new(UpdateChannels.Stable, "launcherUpdateChannelStable"),
        new(UpdateChannels.Beta, "launcherUpdateChannelBeta")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> LogLevel { get; } =
    [
        new(LogLevels.Verbose, "logLevelVerbose"),
        new(LogLevels.Debug, "logLevelDebug"),
        new(LogLevels.Information, "logLevelInformation"),
        new(LogLevels.Warning, "logLevelWarning"),
        new(LogLevels.Error, "logLevelError"),
        new(LogLevels.Fatal, "logLevelFatal")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> Theme { get; } =
    [
        new(ThemeModes.System, "themeSystem"),
        new(ThemeModes.Light, "themeLight"),
        new(ThemeModes.Dark, "themeDark")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> MotionMode { get; } =
    [
        new(MotionModes.System, "motionModeSystem"),
        new(MotionModes.Full, "motionModeFull"),
        new(MotionModes.Reduced, "motionModeReduced")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> StatusDetailMode { get; } =
    [
        new(StatusDetailModes.Hidden, "statusDetailModeHidden"),
        new(StatusDetailModes.Compact, "statusDetailModeCompact")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> Language { get; } =
    [
        new(LauncherLanguages.Auto, "languageAuto"),
        new(LauncherLanguages.English, "languageEnglish"),
        new(LauncherLanguages.SimplifiedChinese, "languageSimplifiedChinese"),
        new(LauncherLanguages.TraditionalChinese, "languageTraditionalChinese"),
        new(LauncherLanguages.Japanese, "languageJapanese")
    ];

    public static IReadOnlyList<SettingOptionDescriptor> ResourcePanelUidSource { get; } =
    [
        new(ResourcePanelUidSources.Auto, ""),
        new(ResourcePanelUidSources.Custom, "")
    ];

    public static bool ContainsCode(IReadOnlyList<SettingOptionDescriptor> descriptors, string code)
        => Find(descriptors, code) is not null;

    public static string ResolveDisplayResourceKey(IReadOnlyList<SettingOptionDescriptor> descriptors, string code)
        => Find(descriptors, code)?.DisplayResourceKey ?? descriptors[0].DisplayResourceKey;

    private static SettingOptionDescriptor? Find(IReadOnlyList<SettingOptionDescriptor> descriptors, string code)
    {
        foreach (var descriptor in descriptors)
        {
            if (string.Equals(descriptor.Code, code, StringComparison.Ordinal))
            {
                return descriptor;
            }
        }

        return null;
    }
}
