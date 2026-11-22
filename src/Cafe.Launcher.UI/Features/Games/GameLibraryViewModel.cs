using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.UI.Features.Games;

/// <summary>长期持有游戏呈现族；选择只改变显示，后台任务及其刷新和重试始终绑定原条目。</summary>
internal sealed partial class GameLibraryViewModel : ObservableObject, IGameOperationActivity, IDisposable
{
    private readonly ISavedSettingsWriter writer;
    private readonly IGameFileOperationCoordinator fileOperations;
    private readonly LocalizationService localizer;
    private readonly INotificationService toasts;
    private readonly IErrorHandlingService errors;
    private GameNavigationItem active;
    private bool switching;
    private bool disposed;

    public GameLibraryViewModel(GamePresentationFactory factory, ISavedSettingsWriter writer,
        IGameFileOperationCoordinator fileOperations, LocalizationService localizer,
        INotificationService toasts, IErrorHandlingService errors)
    {
        this.writer = writer;
        this.fileOperations = fileOperations;
        this.localizer = localizer;
        this.toasts = toasts;
        this.errors = errors;
        Entries = LauncherProfiles.SupportedGames.Select(profile =>
            new GameNavigationItem(factory.Create(profile.RuntimeId), localizer)).ToArray();
        active = Entries[0];
        active.IsSelected = true;
        active.Context.Operations.IsPresentationActive = true;
        foreach (var entry in Entries)
        {
            var context = entry.Context;
            context.Operations.IsPresentationActive = ReferenceEquals(entry, active);
            context.Operations.PickInstallPathAsync = start => PickInstallPathAsync?.Invoke(context, start) ?? Task.FromResult<string?>(null);
            context.Operations.CommitInstallPathAsync = path => CommitInstallPathAsync?.Invoke(context, path) ?? Task.FromResult(false);
            context.Operations.RefreshRequested += mode => RefreshGameAsync(context, mode);
            context.Operations.OpenLogViewerRequested += () => OpenLogViewerAsync?.Invoke() ?? Task.CompletedTask;
            context.Operations.MinimizeRequested += () => MinimizeRequested?.Invoke();
            context.Operations.ShowRequested += () => ShowRequested?.Invoke();
            context.Operations.ExitRequested += () => HandleExitAfterLaunch(context.GameId);
            context.Operations.TaskSurfaceRequested += () => TaskSurfaceRequested?.Invoke();
            context.Operations.PropertyChanged += OnOperationChanged;
        }
    }

    public IReadOnlyList<GameNavigationItem> Entries { get; }
    public GamePresentationContext Active => active.Context;

    /// <summary>首页显示的条目身份，与设置编辑会话的固定目标分离。</summary>
    public GameNavigationItem ActiveEntry => active;
    public bool IsSwitching => switching;
    internal long SelectionRevision { get; private set; }
    public GameNavigationItem? BackgroundTask => Entries.FirstOrDefault(entry =>
        !entry.IsSelected && string.Equals(entry.GameId, fileOperations.ActiveOperation?.GameId, StringComparison.Ordinal));
    public bool HasBackgroundTask => BackgroundTask is not null;
    public GameNavigationItem? BackgroundError => Entries.FirstOrDefault(entry =>
        !entry.IsSelected && entry.Context.Operations.IsTaskFailed);
    public bool HasBackgroundError => BackgroundError is not null;
    public bool IsResourcePanelAvailable => LauncherProfiles.GetGameDefinition(Active.GameId).SupportsResourcePanel;
    public Func<bool>? CanSwitch { get; set; }
    public Action<LauncherGameContext>? SetSettingsTarget { get; set; }
    public Func<GamePresentationContext, GameOperationsRefreshMode, Task>? RefreshRequested { get; set; }
    public Func<Task>? OpenLogViewerAsync { get; set; }
    public Func<GamePresentationContext, string, Task<string?>>? PickInstallPathAsync { get; set; }
    public Func<GamePresentationContext, string, Task<bool>>? CommitInstallPathAsync { get; set; }
    public Action? TaskSurfaceRequested { get; set; }
    public event Action? ActiveChanged;
    public event Action? MinimizeRequested;
    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event PropertyChangedEventHandler? ActivityPropertyChanged;

    public bool IsDownloadRunning => Entries.Any(entry => entry.Context.Operations.IsDownloadRunning);
    public bool IsUninstallExecuting => Entries.Any(entry => entry.Context.Operations.IsUninstallExecuting);
    public bool IsPaused => TaskOperations?.IsPaused ?? false;
    public bool CanPauseOperation => TaskOperations?.CanPauseOperation ?? false;
    private IGameOperationActivity? TaskOperations => Entries.FirstOrDefault(entry =>
        string.Equals(entry.GameId, fileOperations.ActiveOperation?.GameId, StringComparison.Ordinal))?.Context.Operations;

    public GamePresentationContext Get(string gameId) => Entries.FirstOrDefault(entry =>
        string.Equals(entry.GameId, gameId, StringComparison.Ordinal))?.Context
        ?? throw new ArgumentException("Unsupported game identity.", nameof(gameId));

    /// <summary>尝试显示明确目标；受模态与草稿门禁约束，不延后或排队处理受阻请求。</summary>
    public async Task<bool> TrySelectGameAsync(string gameId)
    {
        var entry = Entries.Single(item => string.Equals(item.GameId, Get(gameId).GameId, StringComparison.Ordinal));
        return await SelectEntryAsync(entry);
    }

    public void RestoreSelection(LauncherSettings settings)
    {
        var entry = Entries.FirstOrDefault(item => string.Equals(item.GameId, settings.LastViewedGameId, StringComparison.Ordinal))
            ?? Entries[0];
        Activate(entry);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SelectGameAsync(GameNavigationItem? entry)
    {
        await SelectEntryAsync(entry);
    }

    private async Task<bool> SelectEntryAsync(GameNavigationItem? entry)
    {
        if (entry is null || switching) return false;
        if (ReferenceEquals(active, entry)) return true;

        if (CanSwitch?.Invoke() == false)
        {
            toasts.ShowWarning(localizer.T(LocalizationKeys.GameSwitchBlocked));
            return false;
        }

        var previousGameId = Active.GameId;
        SelectionRevision++;
        SetSwitching(true);
        try
        {
            await writer.UpdateAsync(settings => settings.LastViewedGameId = entry.GameId);
            // A notice or another modal can appear while disk I/O is in flight. Recheck at
            // the display boundary and restore the persisted selection when it now blocks us.
            if (CanSwitch?.Invoke() == false)
            {
                await writer.UpdateAsync(settings => settings.LastViewedGameId = previousGameId);
                toasts.ShowWarning(localizer.T(LocalizationKeys.GameSwitchBlocked));
                return false;
            }
            Activate(entry);
            SetSwitching(false);
            if (entry.Context.Snapshot is null)
            {
                await RefreshGameAsync(entry.Context, GameOperationsRefreshMode.SkipPersistedResume);
            }
            return true;
        }
        catch (Exception exception)
        {
            await errors.HandleErrorAsync("Game selection failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.T(LocalizationKeys.GameSwitchBlocked) });
            return false;
        }
        finally
        {
            SetSwitching(false);
        }
    }

    [RelayCommand]
    private Task ShowBackgroundTaskAsync() => SelectGameAsync(BackgroundTask);

    [RelayCommand]
    private async Task ShowBackgroundErrorAsync()
    {
        if (BackgroundError is { } entry && await SelectEntryAsync(entry))
        {
            entry.Context.Operations.ShowTaskCommand.Execute(null);
        }
    }

    private void SetSwitching(bool value) => SetProperty(ref switching, value, nameof(IsSwitching));

    private void Activate(GameNavigationItem entry)
    {
        SetSettingsTarget?.Invoke(entry.Context.Core);
        active.Context.RemoteContent.StopCarouselTimer();
        active.Context.Operations.IsPresentationActive = false;
        active.IsSelected = false;
        active = entry;
        active.IsSelected = true;
        active.Context.Operations.IsPresentationActive = true;
        ActiveChanged?.Invoke();
        OnPropertyChanged(nameof(Active));
        OnPropertyChanged(nameof(ActiveEntry));
        OnPropertyChanged(nameof(IsResourcePanelAvailable));
        NotifyActivity();
    }

    private Task RefreshGameAsync(GamePresentationContext context, GameOperationsRefreshMode mode) =>
        RefreshRequested?.Invoke(context, mode) ?? Task.CompletedTask;

    private void HandleExitAfterLaunch(string gameId)
    {
        if (fileOperations.ActiveOperation is { } operation
            && !string.Equals(operation.GameId, gameId, StringComparison.Ordinal))
        {
            toasts.ShowWarning(localizer.T(LocalizationKeys.AfterLaunchExitDeferred));
            MinimizeRequested?.Invoke();
            return;
        }

        ExitRequested?.Invoke();
    }

    private void OnOperationChanged(object? sender, PropertyChangedEventArgs args) => NotifyActivity(args.PropertyName);

    private void NotifyActivity(string? propertyName = nameof(IsDownloadRunning))
    {
        foreach (var entry in Entries)
        {
            entry.RefreshPresentation();
        }

        OnPropertyChanged(nameof(BackgroundTask));
        OnPropertyChanged(nameof(HasBackgroundTask));
        OnPropertyChanged(nameof(BackgroundError));
        OnPropertyChanged(nameof(HasBackgroundError));
        ActivityPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void RefreshLocalizedText()
    {
        foreach (var entry in Entries)
        {
            entry.Context.Operations.RefreshLocalizedText();
            entry.Context.RemoteContent.RefreshLocalizedText();
            entry.RefreshPresentation();
        }
    }

    public void PauseResume() => TaskOperations?.PauseResume();

    public void RequestStop()
    {
        if (ReferenceEquals(TaskOperations, Active.Operations)) TaskOperations?.RequestStop();
        else if (TaskOperations is not null) toasts.ShowWarning(localizer.T(LocalizationKeys.GameSwitchBlocked));
    }

    public void StopOperation(GameOperationStopIntent intent)
    {
        foreach (var entry in Entries)
        {
            entry.Context.Operations.StopOperation(intent);
        }
    }

    public async Task PrepareForShutdownAsync()
    {
        await Task.WhenAll(Entries.Select(entry => entry.Context.Operations.PrepareForShutdownAsync()));
        await Task.WhenAll(Entries.Select(entry => entry.Context.Operations.WhenIdle));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var entry in Entries)
        {
            entry.Context.Operations.PropertyChanged -= OnOperationChanged;
            entry.DisposeIcon();
            entry.Context.Dispose();
        }
    }
}

/// <summary>游戏栏条目的固定身份、名称与非颜色任务/运行标记。</summary>
internal sealed class GameNavigationItem(GamePresentationContext context, LocalizationService localizer) : ObservableObject
{
    private bool isSelected;
    public GamePresentationContext Context { get; } = context;
    public string GameId => Context.GameId;
    public string Title => GameDisplayName.Resolve(GameId, localizer);
    public string BrandTitle => GameId == LauncherProfiles.BlueArchiveJapan.RuntimeId ? "Blue Archive" : "Stella Sora";
    /// <summary>Blue Archive ships a wordmark; other titles use the brand text in the same slot.</summary>
    public bool HasWordmark => GameId == LauncherProfiles.BlueArchiveJapan.RuntimeId;
    private Bitmap? iconImage;
    private bool iconDisposed;
    public Bitmap? IconImage => iconDisposed ? null : iconImage ??= LoadIcon();

    private Bitmap LoadIcon()
    {
        using var stream = AssetLoader.Open(new Uri(GameVisualAssets.Icon(GameId)));
        return new Bitmap(stream);
    }

    public void DisposeIcon()
    {
        iconDisposed = true;
        var previous = iconImage;
        iconImage = null;
        // Clear live Image.Source bindings before their bitmap becomes invalid.
        OnPropertyChanged(nameof(IconImage));
        previous?.Dispose();
    }

    public string IconKind => GameId == LauncherProfiles.BlueArchiveJapan.RuntimeId ? "SchoolOutline" : "StarFourPoints";

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public string StatusText => Context.Operations.IsTaskCardVisible
        ? Context.Operations.TaskTitle
        : Context.Operations.IsTaskFailed ? Context.Operations.TaskTitle
        : !string.IsNullOrEmpty(Context.Operations.GameSessionStateText) ? Context.Operations.GameSessionStateText
        : Context.Operations.IsControlPanelVisible ? localizer.T(LocalizationKeys.GameReady)
        : Context.Snapshot?.RuntimeState switch
        {
            LauncherRuntimeState.NotInstalled => localizer.T(LocalizationKeys.GameNotInstalledStatus),
            LauncherRuntimeState.Corrupted => localizer.T(LocalizationKeys.Repair),
            LauncherRuntimeState.IoFailure or LauncherRuntimeState.RemoteUnavailable => localizer.T(LocalizationKeys.Refresh),
            null => "",
            _ => localizer.T(LocalizationKeys.GameUpdateAvailableStatus)
        };

    public string TaskSummary => localizer.F(LocalizationKeys.BackgroundGameTask, Title, StatusText);
    public string RailTooltip => string.IsNullOrEmpty(StatusText) ? Title : $"{Title}{Environment.NewLine}{StatusText}";
    public bool HasTaskBadge => Context.Operations.IsTaskCardVisible && !Context.Operations.IsTaskFailed;
    public bool HasErrorBadge => Context.Operations.IsTaskFailed;
    public int ProgressValue => Context.Operations.ProgressValue;
    public bool IsProgressIndeterminate => Context.Operations.IsProgressIndeterminate;

    public void RefreshPresentation()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(TaskSummary));
        OnPropertyChanged(nameof(RailTooltip));
        OnPropertyChanged(nameof(HasTaskBadge));
        OnPropertyChanged(nameof(HasErrorBadge));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
    }
}
