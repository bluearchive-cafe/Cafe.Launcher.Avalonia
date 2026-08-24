using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cafe.Launcher.Avalonia.Models;

/// <summary>
/// Base class for selectable dropdown options with observable Code/DisplayName properties.
/// </summary>
public abstract class SelectableOption : INotifyPropertyChanged
{
    private string code = "";
    private string displayName = "";
    private string description = "";

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

    public string Description
    {
        get => description;
        set => SetField(ref description, value);
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
    public GameOperationKind OperationKind { get; set; } = GameOperationKind.Idle;

    public GameOperationStage Stage { get; set; } = GameOperationStage.Idle;

    public int Progress { get; set; }

    public long BytesPerSecond { get; set; }

    public TimeSpan? EstimatedRemaining { get; set; }

    public long DownloadedSize { get; set; }

    public long TotalSize { get; set; }

    public GameOperationErrorCode ErrorCode { get; set; } = GameOperationErrorCode.None;

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

    public GameOperationErrorCode ErrorCode { get; set; } = GameOperationErrorCode.None;

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

    public string Title { get => title; set => SetField(ref title, value); }
    public string Subtitle { get => subtitle; set => SetField(ref subtitle, value); }
    public string Url { get => url; set => SetField(ref url, value); }
    public string ImageUrl { get => imageUrl; set => SetField(ref imageUrl, value); }
    public string SocialIconKind { get => socialIconKind; set => SetField(ref socialIconKind, value); }

    public global::Avalonia.Media.Imaging.Bitmap? BannerBitmap
    {
        get => bannerBitmap;
        set => SetField(ref bannerBitmap, value);
    }

    public bool IsImageLoading
    {
        get => isImageLoading;
        private set => SetField(ref isImageLoading, value);
    }

    public bool IsImageLoadFailed
    {
        get => isImageLoadFailed;
        private set => SetField(ref isImageLoadFailed, value);
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

    private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class NewsCategory : INotifyPropertyChanged
{
    private string label = "";
    private bool isActive;
    private readonly ObservableCollection<RemoteContentItem> items = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label
    {
        get => label;
        set => SetField(ref label, value);
    }

    public bool IsActive
    {
        get => isActive;
        set => SetField(ref isActive, value);
    }

    public ObservableCollection<RemoteContentItem> Items { get => items; }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
