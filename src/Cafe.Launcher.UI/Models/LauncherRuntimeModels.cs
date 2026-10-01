using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.UI.Models;

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

internal sealed class SettingOption : SelectableOption
{
    public string IconKind { get; init; } = "CogOutline";

    public string SelectedIconKind { get; init; } = "Cog";
}

public sealed class LanguageOption : SelectableOption
{
}

internal sealed class ThemeOption : SelectableOption
{
}

internal sealed class GameOperationProgress
{
    /// <summary>卸载已处理的文件、链接与目录项数；失败条目也计为已处理。</summary>
    public int ProcessedEntryCount { get; set; }

    /// <summary>卸载扫描得到的条目总数；扫描阶段尚未确定时为零。</summary>
    public int TotalEntryCount { get; set; }

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

internal sealed class GameOperationResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public GameOperationErrorCode ErrorCode { get; set; } = GameOperationErrorCode.None;

    public int AffectedFileCount { get; set; }

    /// <summary>
    /// 本次操作涉及的文件合计字节数。卸载执行结果是实际删除的普通文件字节数，
    /// 包含清单外资源与安装状态；确认框的目录大小另由测量结果提供。
    /// </summary>
    public long AffectedBytes { get; set; }

    public int FailedFileCount { get; set; }

    /// <summary>仅卸载设置；完整残留与保留路径供结果表面直接绑定。</summary>
    public UninstallResultDetails? UninstallDetails { get; set; }
}

internal sealed class RemoteContentItem : ObservableObject
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

internal sealed class NewsCategory : ObservableObject
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
