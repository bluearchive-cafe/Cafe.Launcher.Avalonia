using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Services.GameRuntime;
using Cafe.Launcher.UI.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.UI.Features.Settings;

/// <summary>
/// 设置页游戏分类的 Linux 运行时可用性流程：状态行、Proton 构建发现与运行器路径选择。
/// 探测失败只降级状态行（启动诊断才是运行时失败的权威上报），因此这里不注入错误管线；
/// 刷新按代次保护忙碌标记，被取消的旧运行不会清掉新运行的忙态。
/// </summary>
internal sealed partial class SettingsGameRuntimeViewModel : ObservableObject, ILanguageAwarePresentation, IDisposable
{
    private readonly SettingsEditor editor;
    private readonly SettingsOptionsViewModel options;
    private readonly LocalizationService localizer;
    private readonly IGameRuntime gameRuntime;
    private readonly IFilePickerService filePickerService;
    private readonly IProtonBuildDiscovery protonBuildDiscovery;

    /// <summary>运行器/路径连续编辑时合并刷新，避免每次按键都探测版本并扫描磁盘。</summary>
    private const int StatusRefreshDebounceMilliseconds = 300;

    private readonly LatestRefresh statusRefresh = new();
    private IReadOnlyList<GameRuntimeStatusEntry>? statusEntries;
    private int statusRefreshGeneration;
    private bool disposed;

    public SettingsGameRuntimeViewModel(
        SettingsEditor editor,
        SettingsOptionsViewModel options,
        LocalizationService localizer,
        IGameRuntime gameRuntime,
        IFilePickerService filePickerService,
        IProtonBuildDiscovery protonBuildDiscovery)
    {
        this.editor = editor;
        this.options = options;
        this.localizer = localizer;
        this.gameRuntime = gameRuntime;
        this.filePickerService = filePickerService;
        this.protonBuildDiscovery = protonBuildDiscovery;
        editor.PropertyChanged += OnEditorPropertyChanged;
        editor.CurrentPropertyChanged += OnCurrentSettingChanged;
    }

    /// <summary>A custom executable path is only applied to an explicitly selected runner.</summary>
    public bool IsRunnerPathEnabled =>
        editor.Current.GameRuntime.Runner is GameRuntimeRunners.Umu or GameRuntimeRunners.Wine;

    /// <summary>
    /// Multi-line per-runner availability summary, e.g.
    /// "UMU / Proton: Available · /usr/bin/umu-run · 1.4.4".
    /// </summary>
    [ObservableProperty]
    private string statusSummary = string.Empty;

    [ObservableProperty]
    private bool isStatusRefreshing;

    [ObservableProperty]
    private bool buildDiscoveryFailed;

    internal Task PendingStatusRefresh => statusRefresh.Pending;

    /// <summary>Installed builds are suggestions only; discovery never changes the draft.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBuilds))]
    [NotifyPropertyChangedFor(nameof(SelectedBuild))]
    private IReadOnlyList<SettingOption> builds = [];

    public bool HasBuilds => Builds.Count > 0;

    /// <summary>Selecting a discovered build edits the same path as manual input.</summary>
    public string? SelectedBuild
    {
        get
        {
            var currentPath = NormalizePath(editor.Current.GameRuntime.ProtonPath);
            if (currentPath.Length == 0)
            {
                return null;
            }

            return Builds.FirstOrDefault(build =>
                string.Equals(NormalizePath(build.Code), currentPath, GamePathValidator.PathComparison))?.Code;
        }
        set
        {
            // 候选刷新后若手输路径不在列表内，绑定会把 null 写回；此时必须保留
            // 自定义路径，只有用户选中具体候选才改写草稿。清空请使用路径输入框。
            if (value is not null)
            {
                editor.Current.GameRuntime.ProtonPath = value;
            }
        }
    }

    /// <inheritdoc/>
    public void RefreshLocalizedText()
    {
        if (statusEntries is not null)
        {
            StatusSummary = BuildStatusSummary(statusEntries);
        }
    }

    /// <summary>
    /// Kicks off a fire-and-forget availability refresh for the runtime status row.
    /// Runs the real version probes, so it must stay off the save/open critical path.
    /// </summary>
    [RelayCommand]
    public void RefreshStatus() => QueueStatusRefresh(debounce: null);

    [RelayCommand]
    private Task ChoosePrefixPathAsync() => PickRuntimeFolderAsync(
        LocalizationKeys.GameRuntimePrefixPath,
        () => editor.Current.GameRuntime.PrefixPath,
        path => editor.Current.GameRuntime.PrefixPath = path);

    [RelayCommand]
    private Task ChooseProtonPathAsync() => PickRuntimeFolderAsync(
        LocalizationKeys.GameRuntimeProtonPath,
        () => editor.Current.GameRuntime.ProtonPath,
        path => editor.Current.GameRuntime.ProtonPath = path);

    internal string BuildStatusSummary(IReadOnlyList<GameRuntimeStatusEntry> entries)
    {
        var visibleRunnerIds = options.GameRuntimeRunner.Select(option => option.Code).ToHashSet();
        return string.Join(
            Environment.NewLine,
            entries.Where(entry => visibleRunnerIds.Contains(entry.RunnerId))
                .Select(FormatStatusEntry));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        statusRefresh.Cancel();
        editor.PropertyChanged -= OnEditorPropertyChanged;
        editor.CurrentPropertyChanged -= OnCurrentSettingChanged;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsEditor.Current))
        {
            return;
        }

        // Current 实例会被整体替换（保存后草稿换成归一化值），展示值须随之重算。
        OnPropertyChanged(nameof(IsRunnerPathEnabled));
        OnPropertyChanged(nameof(SelectedBuild));
    }

    private void OnCurrentSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameRuntimeSettings.Runner))
        {
            OnPropertyChanged(nameof(IsRunnerPathEnabled));
        }

        if (e.PropertyName is nameof(GameRuntimeSettings.Runner) or nameof(GameRuntimeSettings.RunnerPath))
        {
            QueueStatusRefresh(TimeSpan.FromMilliseconds(StatusRefreshDebounceMilliseconds));
        }

        if (e.PropertyName == nameof(GameRuntimeSettings.ProtonPath))
        {
            OnPropertyChanged(nameof(SelectedBuild));
        }
    }

    private async Task PickRuntimeFolderAsync(
        LocalizationKeys titleKey,
        Func<string?> currentPath,
        Action<string> applyPath)
    {
        var path = await filePickerService.PickFolderAsync(localizer.T(titleKey), currentPath());
        if (!string.IsNullOrWhiteSpace(path))
        {
            applyPath(path);
        }
    }

    /// <summary>
    /// Starts the latest refresh and marks the row busy. The generation guards the
    /// busy flag so a cancelled run cannot clear it while a newer run is in flight.
    /// </summary>
    private void QueueStatusRefresh(TimeSpan? debounce)
    {
        if (disposed)
        {
            return;
        }

        var generation = ++statusRefreshGeneration;
        IsStatusRefreshing = true;
        statusRefresh.Run(
            debounce,
            cancellationToken => RefreshStatusAsync(generation, cancellationToken));
    }

    private async Task RefreshStatusAsync(int generation, CancellationToken cancellationToken)
    {
        statusEntries = null;
        BuildDiscoveryFailed = false;
        StatusSummary = localizer.T(LocalizationKeys.GameRuntimeStatusChecking);
        try
        {
            try
            {
                var runtimeConfiguration = GameRuntimeConfiguration.FromSettings(editor.GetSnapshot().GameRuntime);
                var entries = await gameRuntime
                    .GetStatusesAsync(runtimeConfiguration, cancellationToken)
                    .ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                statusEntries = entries;
                StatusSummary = BuildStatusSummary(entries);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                // Status is informational; launch diagnostics remain the authoritative
                // runtime failure report, so a refresh failure only degrades this row.
                // 豁免：状态行是纯信息性展示，运行时失败另有权威上报通道；
                // 此处降级不注入诊断，避免为低价值路径扩构造面（见审计 7.2）。
                if (!cancellationToken.IsCancellationRequested)
                {
                    StatusSummary = localizer.T(LocalizationKeys.GameRuntimeStatusFailed);
                }
                Debug.WriteLine($"Settings: game runtime status refresh failed: {exception.Message}");
            }

            try
            {
                var builds = await Task.Run(protonBuildDiscovery.Discover, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                Builds = builds.Select(build => new SettingOption
                {
                    Code = build.Path,
                    DisplayName = build.Name,
                    Description = build.Path
                }).ToArray();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                // Discovery is best effort, but the row must say it can be retried
                // instead of leaving a faulted fire-and-forget task behind.
                if (!cancellationToken.IsCancellationRequested)
                {
                    Builds = [];
                    BuildDiscoveryFailed = true;
                }
                Debug.WriteLine($"Settings: Proton build discovery failed: {exception.Message}");
            }
        }
        finally
        {
            if (generation == statusRefreshGeneration)
            {
                IsStatusRefreshing = false;
            }
        }
    }

    private string FormatStatusEntry(GameRuntimeStatusEntry entry)
    {
        var name = GameRuntimeRunnerDisplay.RunnerName(localizer, entry.RunnerId);
        var status = GameRuntimeRunnerDisplay.Status(localizer, entry.Availability.Status);

        var path = entry.Availability.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return localizer.F(LocalizationKeys.GameRuntimeStatusEntryFormat, name, status);
        }

        var detail = string.IsNullOrWhiteSpace(entry.Availability.Version)
            ? path
            : localizer.F(LocalizationKeys.GameRuntimeStatusDetailFormat, path, entry.Availability.Version);
        return localizer.F(LocalizationKeys.GameRuntimeStatusEntryDetailFormat, name, status, detail);
    }

    private static string NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : Path.TrimEndingDirectorySeparator(path.Trim());
}
