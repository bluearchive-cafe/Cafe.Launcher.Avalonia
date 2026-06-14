using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Models;

public sealed class LocalGameState
{
    public string GamePath { get; set; } = "";

    public string ConfigPath { get; set; } = "";

    public string ManifestPath { get; set; } = "";

    public bool ConfigExists { get; set; }

    public bool ManifestExists { get; set; }

    public GameLauncherConfig? GameConfig { get; set; }

    public LocalManifest? Manifest { get; set; }

    public string? Error { get; set; }
}

public static class LaunchCheckModes
{
    public const string LocalManifest = "LocalManifest";
    public const string RemoteManifest = "RemoteManifest";
    public const string None = "None";
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
    public const string _1MBs = "1MB/s";
    public const string _5MBs = "5MB/s";
    public const string _10MBs = "10MB/s";
    public const string _25MBs = "25MB/s";
    public const string _50MBs = "50MB/s";
    public static int ToBytesPerSecond(string limit) => limit switch
    {
        _1MBs => 1024 * 1024,
        _5MBs => 5 * 1024 * 1024,
        _10MBs => 10 * 1024 * 1024,
        _25MBs => 25 * 1024 * 1024,
        _50MBs => 50 * 1024 * 1024,
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

public static class GameOperationKinds
{
    public const string Idle = "idle";
    public const string Download = "download";
    public const string Repair = "repair";
    public const string Uninstall = "uninstall";
}

public sealed class LauncherSettings
{
    [JsonPropertyName("gamePath")]
    public string GamePath { get; set; } = "";

    [JsonPropertyName("launchCheckMode")]
    public string LaunchCheckMode { get; set; } = LaunchCheckModes.LocalManifest;

    [JsonPropertyName("proxyMode")]
    public string ProxyMode { get; set; } = ProxyModes.Direct;

    [JsonPropertyName("closeBehavior")]
    public string CloseBehavior { get; set; } = CloseBehaviors.Minimize;

    [JsonPropertyName("language")]
    public string Language { get; set; } = LauncherLanguages.Auto;

    [JsonPropertyName("themeMode")]
    public string ThemeMode { get; set; } = ThemeModes.System;

    [JsonPropertyName("themeColorMode")]
    public string ThemeColorMode { get; set; } = ThemeColorModes.Default;

    [JsonPropertyName("customThemeColor")]
    public string CustomThemeColor { get; set; } = LauncherConstants.DefaultThemeColor;

    [JsonPropertyName("themeColorPalette")]
    public List<string> ThemeColorPalette { get; set; } = [];

    [JsonPropertyName("selectedThemeColorPaletteIndex")]
    public int SelectedThemeColorPaletteIndex { get; set; }

    [JsonPropertyName("downloadSpeedLimit")]
    public string DownloadSpeedLimit { get; set; } = DownloadSpeedLimits.Unlimited;

    [JsonPropertyName("toastNotificationsEnabled")]
    public bool ToastNotificationsEnabled { get; set; } = true;

    [JsonPropertyName("showRemoteContentCard")]
    public bool ShowRemoteContentCard { get; set; } = true;

    [JsonPropertyName("patchUrlGroup")]
    public string PatchUrlGroup { get; set; } = PatchUrlGroups.Official;

    [JsonPropertyName("customBackgroundPath")]
    public string CustomBackgroundPath { get; set; } = "";

    [JsonPropertyName("backgroundSource")]
    public string BackgroundSource { get; set; } = BackgroundSources.Bundled;

    [JsonPropertyName("resourcePanelUid")]
    public string ResourcePanelUid { get; set; } = "";
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

public sealed class LauncherStatusSnapshot
{
    public LauncherSettings Settings { get; set; } = new();

    public LocalGameState LocalGame { get; set; } = new();

    public LauncherRemoteState Remote { get; set; } = new();

    public bool IsInstalled { get; set; }

    public bool NeedsUpdate { get; set; }

    public bool BelowLowestVersion { get; set; }

    public string UserStatus { get; set; } = "";

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
