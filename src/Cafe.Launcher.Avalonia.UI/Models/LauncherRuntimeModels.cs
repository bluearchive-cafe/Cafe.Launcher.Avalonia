using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Models;

/// <summary>
/// Base class for selectable dropdown options with observable Code/DisplayName properties.
/// </summary>
public abstract class SelectableOption : ObservableObject
{
    private string code = "";
    private string displayName = "";
    private string description = "";

    public string Code
    {
        get => code;
        set => SetProperty(ref code, value);
    }

    public string DisplayName
    {
        get => displayName;
        set => SetProperty(ref displayName, value);
    }

    public string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }
}

public sealed class SettingOption : SelectableOption
{
    public string IconKind { get; init; } = "CogOutline";

    public string SelectedIconKind { get; init; } = "Cog";
}

public sealed class LanguageOption : SelectableOption
{
}

public sealed class ThemeOption : SelectableOption
{
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

public sealed class RemoteContentItem : ObservableObject
{
    private string title = "";
    private string subtitle = "";
    private string url = "";
    private string imageUrl = "";
    private string socialIconKind = "Link";
    private global::Avalonia.Media.Imaging.Bitmap? bannerBitmap;
    private bool isImageLoading = true;
    private bool isImageLoadFailed;

    public string Title { get => title; set => SetProperty(ref title, value); }
    public string Subtitle { get => subtitle; set => SetProperty(ref subtitle, value); }
    public string Url { get => url; set => SetProperty(ref url, value); }
    public string ImageUrl { get => imageUrl; set => SetProperty(ref imageUrl, value); }
    public string SocialIconKind { get => socialIconKind; set => SetProperty(ref socialIconKind, value); }

    public global::Avalonia.Media.Imaging.Bitmap? BannerBitmap
    {
        get => bannerBitmap;
        set => SetProperty(ref bannerBitmap, value);
    }

    public bool IsImageLoading
    {
        get => isImageLoading;
        private set => SetProperty(ref isImageLoading, value);
    }

    public bool IsImageLoadFailed
    {
        get => isImageLoadFailed;
        private set => SetProperty(ref isImageLoadFailed, value);
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
}

public sealed class NewsCategory : ObservableObject
{
    private string label = "";
    private readonly ObservableCollection<RemoteContentItem> items = [];

    public string Label
    {
        get => label;
        set => SetProperty(ref label, value);
    }

    public ObservableCollection<RemoteContentItem> Items { get => items; }
}
