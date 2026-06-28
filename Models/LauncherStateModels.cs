using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Models;

public static class LaunchCheckModes
{
    public const string LocalManifest = "localManifest";
    public const string RemoteManifest = "remoteManifest";
    public const string None = "none";
}

public static class ProxyModes
{
    public const string Direct = "direct";
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
    public const string Japanese = "ja";
}

public static class ThemeModes
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";
}

public static class ThemeColorModes
{
    public const string Default = "default";
    public const string System = "system";
    public const string Wallpaper = "wallpaper";
    public const string Custom = "custom";
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

public static class GameOperationKinds
{
    public const string Idle = "idle";
    public const string Download = "download";
    public const string Repair = "repair";
    public const string Uninstall = "uninstall";
}

public sealed partial class LauncherSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("gamePath")]
    private string gamePath = "";

    [ObservableProperty]
    [property: JsonPropertyName("launchCheckMode")]
    private string launchCheckMode = LaunchCheckModes.LocalManifest;

    [ObservableProperty]
    [property: JsonPropertyName("proxyMode")]
    private string proxyMode = ProxyModes.Direct;

    [ObservableProperty]
    [property: JsonPropertyName("closeBehavior")]
    private string closeBehavior = CloseBehaviors.Minimize;

    [ObservableProperty]
    [property: JsonPropertyName("language")]
    private string language = LauncherLanguages.Auto;

    [ObservableProperty]
    [property: JsonPropertyName("themeMode")]
    private string themeMode = ThemeModes.System;

    [ObservableProperty]
    [property: JsonPropertyName("themeColorMode")]
    private string themeColorMode = ThemeColorModes.Default;

    [ObservableProperty]
    [property: JsonPropertyName("customThemeColor")]
    private string customThemeColor = LauncherConstants.DefaultThemeColor;

    [ObservableProperty]
    [property: JsonPropertyName("themeColorPalette")]
    private List<string> themeColorPalette = [];

    [ObservableProperty]
    [property: JsonPropertyName("selectedThemeColorPaletteIndex")]
    private int selectedThemeColorPaletteIndex;

    [ObservableProperty]
    [property: JsonPropertyName("downloadSpeedLimit")]
    private string downloadSpeedLimit = DownloadSpeedLimits.Unlimited;

    [ObservableProperty]
    [property: JsonPropertyName("toastNotificationsEnabled")]
    private bool toastNotificationsEnabled = true;

    [ObservableProperty]
    [property: JsonPropertyName("showRemoteContentCard")]
    private bool showRemoteContentCard = true;

    [ObservableProperty]
    [property: JsonPropertyName("patchUrlGroup")]
    private string patchUrlGroup = PatchUrlGroups.Official;

    [ObservableProperty]
    [property: JsonPropertyName("customBackgroundPath")]
    private string customBackgroundPath = "";

    [ObservableProperty]
    [property: JsonPropertyName("backgroundSource")]
    private string backgroundSource = BackgroundSources.Bundled;

    [ObservableProperty]
    [property: JsonPropertyName("backgroundFit")]
    private string backgroundFit = BackgroundFits.UniformToFill;

    [ObservableProperty]
    [property: JsonPropertyName("backgroundFillColor")]
    private string backgroundFillColor = "#FF000000";

    [ObservableProperty]
    [property: JsonPropertyName("resourcePanelUid")]
    private string resourcePanelUid = "";

    [ObservableProperty]
    [property: JsonPropertyName("updateChannel")]
    private string updateChannel = UpdateChannels.Stable;

    [ObservableProperty]
    [property: JsonPropertyName("logLevel")]
    private string logLevel = LogLevels.Information;

    /// <summary>
    /// Deep-clones this settings object via JSON round-trip.
    /// Shared by <c>LauncherSettingsService.NormalizeSettings</c> and <see cref="Services.SettingsEditor"/>.
    /// </summary>
    public LauncherSettings DeepClone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this, CloneJsonOptions);
        return System.Text.Json.JsonSerializer.Deserialize<LauncherSettings>(json, CloneJsonOptions)
            ?? new LauncherSettings();
    }

    /// <summary>
    /// Creates default settings with pre-release builds defaulting to the beta update channel.
    /// Shared by <see cref="Services.LauncherSettingsService"/> and <see cref="Services.SettingsEditor"/>.
    /// </summary>
    public static LauncherSettings CreateDefaults()
    {
        var settings = new LauncherSettings();

        if (Constants.BuildInfo.LauncherVersion.Contains('-'))
        {
            settings.UpdateChannel = UpdateChannels.Beta;
        }

        return settings;
    }

    private static readonly System.Text.Json.JsonSerializerOptions CloneJsonOptions = JsonDefaults.Strict;
}

/// <summary>
/// Base class for selectable dropdown options with observable Code/DisplayName properties.
/// </summary>
public abstract class SelectableOption : INotifyPropertyChanged
{
    private string code = "";
    private string displayName = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Code
    {
        get => code;
        set => SetField(ref code, value);
    }

    public string DisplayName
    {
        get => displayName;
        set => SetField(ref displayName, value);
    }

    protected void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SettingOption : SelectableOption
{
}

public sealed class LanguageOption : SelectableOption
{
}

public sealed class ThemeOption : SelectableOption
{
}

public sealed class ManifestValidationResult
{
    public bool Success { get; set; }

    public int DamagedFileCount { get; set; }

    public int MissingFileCount { get; set; }

    public int SizeMismatchFileCount { get; set; }

    public string Message { get; set; } = "";
}

public sealed class GameLaunchResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public ManifestValidationResult Validation { get; set; } = new();
}

public sealed class GameOperationProgress
{
    public string OperationKind { get; set; } = GameOperationKinds.Idle;

    public string Stage { get; set; } = "";

    public int Progress { get; set; }

    public string Speed { get; set; } = "";

    public string Estimated { get; set; } = "";

    public long DownloadedSize { get; set; }

    public long TotalSize { get; set; }

    public string ErrorType { get; set; } = "";

    public int AffectedFileCount { get; set; }

    public long RequiredDiskBytes { get; set; }

    public long? AvailableDiskBytes { get; set; }

    public int FailedFileCount { get; set; }

    public int RetryAttempt { get; set; }

    public int RetryLimit { get; set; }

    public bool IsRunning { get; set; }

    public bool CanStop { get; set; }

    public bool CanPause { get; set; }

    public bool IsPaused { get; set; }
}

public sealed class GameOperationResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public string ErrorType { get; set; } = "";

    public int AffectedFileCount { get; set; }

    public int FailedFileCount { get; set; }
}

public sealed class LauncherRemoteState
{
    public GameConfigResponse? GameConfig { get; set; }

    public BaseConfigResponse? BaseConfig { get; set; }

    public CdnConfigResponse? CdnConfig { get; set; }

    public OperationsResourceResponse? OperationsResource { get; set; }

    public SocialMediaResourceResponse? SocialMediaResource { get; set; }

    public InstallationConfigResponse? InstallationConfig { get; set; }
}

public enum LauncherRuntimeState
{
    NotInstalled,
    Corrupted,
    IoFailure,
    RemoteUnavailable,
    BelowLowestVersion,
    UpdateAvailable,
    Ready
}

public sealed class LauncherStatusSnapshot
{
    public LauncherSettings Settings { get; set; } = new();

    public LocalInstallationState LocalGame { get; set; } = new();

    public LauncherRemoteState Remote { get; set; } = new();

    public LauncherRuntimeState RuntimeState { get; set; }

    public DateTimeOffset CheckedAt { get; set; }
}

public sealed class RemoteContentItem : INotifyPropertyChanged
{
    private string title = "";
    private string subtitle = "";
    private string url = "";
    private string imageUrl = "";
    private string socialIconKind = "Link";
    private global::Avalonia.Media.Imaging.Bitmap? bannerBitmap;
    private bool isImageLoading = true;
    private bool isImageLoadFailed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get => title; set { if (title != value) { title = value; Notify(nameof(Title)); } } }
    public string Subtitle { get => subtitle; set { if (subtitle != value) { subtitle = value; Notify(nameof(Subtitle)); } } }
    public string Url { get => url; set { if (url != value) { url = value; Notify(nameof(Url)); } } }
    public string ImageUrl { get => imageUrl; set { if (imageUrl != value) { imageUrl = value; Notify(nameof(ImageUrl)); } } }
    public string SocialIconKind { get => socialIconKind; set { if (socialIconKind != value) { socialIconKind = value; Notify(nameof(SocialIconKind)); } } }

    public global::Avalonia.Media.Imaging.Bitmap? BannerBitmap
    {
        get => bannerBitmap;
        set { if (bannerBitmap != value) { bannerBitmap = value; Notify(nameof(BannerBitmap)); } }
    }

    public bool IsImageLoading
    {
        get => isImageLoading;
        private set
        {
            if (isImageLoading != value)
            {
                isImageLoading = value;
                Notify(nameof(IsImageLoading));
            }
        }
    }

    public bool IsImageLoadFailed
    {
        get => isImageLoadFailed;
        private set
        {
            if (isImageLoadFailed != value)
            {
                isImageLoadFailed = value;
                Notify(nameof(IsImageLoadFailed));
            }
        }
    }

    public void MarkImageLoading()
    {
        IsImageLoading = true;
        IsImageLoadFailed = false;
    }

    public void MarkImageLoaded()
    {
        IsImageLoading = false;
        IsImageLoadFailed = false;
    }

    public void MarkImageLoadFailed()
    {
        IsImageLoading = false;
        IsImageLoadFailed = true;
    }

    private void Notify(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class NewsCategory : INotifyPropertyChanged
{
    private string label = "";
    private bool isActive;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label
    {
        get => label;
        set { if (label != value) { label = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label))); } }
    }

    public bool IsActive
    {
        get => isActive;
        set
        {
            if (isActive != value)
            {
                isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            }
        }
    }

    public ObservableCollection<RemoteContentItem> Items { get; set; } = [];
}
