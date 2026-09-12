using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.Features.SetupWizard;

/// <summary>
/// Coordinates the first-launch setup wizard state, validation, and settings output.
/// </summary>
public partial class SetupWizardViewModel : ViewModelBase, IModalContentViewModel, IDisposable
{
    private const int StepCount = 5;

    // 路径文本框 TwoWay 绑定下每个字符都会触发刷新；全量状态读取 + 写探测
    // 对 UNC/慢速介质是真实磁盘 IO，按此窗口防抖合并连续击键。
    private static readonly TimeSpan GamePathStatusDebounce = TimeSpan.FromMilliseconds(300);

    private readonly LocalizationService localizer;
    private readonly GameInstallationPath gameInstallationPath;
    private readonly LocalInstallationStateStore localInstallationStateStore;
    private readonly LocalDiagnostics diagnostics;
    private readonly IFilePickerService filePickerService;
    private bool hasInitializedGamePath;
    private bool isDisposed;
    private CancellationTokenSource? gamePathStatusCancellationTokenSource;
    private int gamePathStatusVersion;

    /// <summary>
    /// Creates a setup wizard with defaults aligned to <see cref="LauncherSettings.CreateDefaults"/>.
    /// </summary>
    public SetupWizardViewModel(
        LocalizationService localizer,
        GameInstallationPath gameInstallationPath,
        LocalInstallationStateStore localInstallationStateStore,
        LocalDiagnostics diagnostics,
        IFilePickerService filePickerService)
    {
        this.localizer = localizer;
        this.gameInstallationPath = gameInstallationPath;
        this.localInstallationStateStore = localInstallationStateStore;
        this.diagnostics = diagnostics;
        this.filePickerService = filePickerService;

        var defaults = LauncherSettings.CreateDefaults();
        language = defaults.Language;
        patchUrlGroup = defaults.PatchUrlGroup;
        gamePath = defaults.GamePath;
        proxyMode = defaults.ProxyMode;
        localizer.LanguageChanged += OnLocalizerLanguageChanged;
        RefreshDownloadSources();
    }

    /// <summary>Gets the localized download source choices for the setup wizard.</summary>
    public IReadOnlyList<SetupWizardDownloadSourceItem> DownloadSources { get; private set; } = [];

    // ── Step state ───────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstStep))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(StepProgress))]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    private int step;

    partial void OnStepChanged(int value)
    {
        if (value == 1 && !hasInitializedGamePath)
        {
            hasInitializedGamePath = true;
            if (string.IsNullOrWhiteSpace(GamePath))
            {
                GamePath = gameInstallationPath.GetDefaultGamePath();
            }
        }

        if (value == 1)
        {
            RefreshGamePathStatus();
        }
    }

    public bool IsFirstStep => Step == 0;
    public bool IsLastStep => Step == 4;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;

    /// <summary>Gets the current step position for the wizard header.</summary>
    public string StepProgress => $"{Step + 1} / {StepCount}";

    public bool CanGoNext => Step switch
    {
        1 => IsGamePathReady,
        _ => true
    };

    public bool CanGoPrevious => Step > 0;

    // ── Settings ──────────────────────────────────────────────────

    [ObservableProperty]
    private string language;

    partial void OnLanguageChanged(string value)
    {
        LanguagePreviewRequested?.Invoke(value);
        RefreshDownloadSources();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsPatchUrlGroupCafe))]
    [NotifyPropertyChangedFor(nameof(IsPatchUrlGroupOfficial))]
    private string patchUrlGroup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsGamePathEmpty))]
    private string gamePath;

    partial void OnGamePathChanged(string value)
    {
        // 击键驱动的变更走防抖；进入步骤等程序性刷新仍为立即（见 OnStepChanged）。
        _ = RefreshGamePathStatusDebouncedAsync();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GamePathStatusText))]
    [NotifyPropertyChangedFor(nameof(GamePathPresentation))]
    [NotifyPropertyChangedFor(nameof(IsGamePathChecking))]
    [NotifyPropertyChangedFor(nameof(IsGamePathReady))]
    [NotifyPropertyChangedFor(nameof(IsGamePathAvailableForInstallation))]
    [NotifyPropertyChangedFor(nameof(IsGamePathValidInstallation))]
    [NotifyPropertyChangedFor(nameof(IsGamePathCorruptedInstallation))]
    [NotifyPropertyChangedFor(nameof(IsGamePathInaccessible))]
    [NotifyPropertyChangedFor(nameof(IsGamePathNotWritable))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private SetupWizardGamePathStatus gamePathStatus;

    public string GamePathStatusText => GamePathStatus switch
    {
        SetupWizardGamePathStatus.Checking => localizer.T(LocalizationKeys.SetupWizardGamePathChecking),
        SetupWizardGamePathStatus.AvailableForInstallation => localizer.T(LocalizationKeys.SetupWizardGamePathAvailable),
        SetupWizardGamePathStatus.ValidInstallation => localizer.T(LocalizationKeys.SetupWizardGamePathInstalled),
        SetupWizardGamePathStatus.CorruptedInstallation => localizer.T(LocalizationKeys.SetupWizardGamePathCorrupted),
        SetupWizardGamePathStatus.Inaccessible => localizer.T(LocalizationKeys.SetupWizardGamePathInaccessible),
        SetupWizardGamePathStatus.NotWritable => localizer.T(LocalizationKeys.SetupWizardGamePathNotWritable),
        _ => string.Empty
    };

    /// <summary>Gets the localized title and description for the current game path status.</summary>
    public SetupWizardGamePathPresentation GamePathPresentation => new(
        localizer.T(LocalizationKeys.SetupWizardGamePathStatusTitle),
        ResolveGamePathPresentationDescription());

    public bool IsGamePathChecking => GamePathStatus == SetupWizardGamePathStatus.Checking;

    public bool IsGamePathReady => GamePathStatus is SetupWizardGamePathStatus.AvailableForInstallation
        or SetupWizardGamePathStatus.ValidInstallation;

    public bool IsGamePathAvailableForInstallation =>
        GamePathStatus == SetupWizardGamePathStatus.AvailableForInstallation;

    public bool IsGamePathValidInstallation =>
        GamePathStatus == SetupWizardGamePathStatus.ValidInstallation;

    public bool IsGamePathCorruptedInstallation =>
        GamePathStatus == SetupWizardGamePathStatus.CorruptedInstallation;

    public bool IsGamePathInaccessible => GamePathStatus == SetupWizardGamePathStatus.Inaccessible;

    public bool IsGamePathNotWritable => GamePathStatus == SetupWizardGamePathStatus.NotWritable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProxyAuto))]
    [NotifyPropertyChangedFor(nameof(IsProxyDirect))]
    [NotifyPropertyChangedFor(nameof(IsProxySystem))]
    private string proxyMode;

    // ── RadioButton helpers ───────────────────────────────────────

    public bool IsPatchUrlGroupCafe
    {
        get => PatchUrlGroup == PatchUrlGroups.Cafe;
        set { if (value) PatchUrlGroup = PatchUrlGroups.Cafe; }
    }

    public bool IsPatchUrlGroupOfficial
    {
        get => PatchUrlGroup == PatchUrlGroups.Official;
        set { if (value) PatchUrlGroup = PatchUrlGroups.Official; }
    }

    public bool IsProxyAuto
    {
        get => ProxyMode == ProxyModes.Auto;
        set { if (value) ProxyMode = ProxyModes.Auto; }
    }

    public bool IsProxyDirect
    {
        get => ProxyMode == ProxyModes.Direct;
        set { if (value) ProxyMode = ProxyModes.Direct; }
    }

    public bool IsProxySystem
    {
        get => ProxyMode == ProxyModes.System;
        set { if (value) ProxyMode = ProxyModes.System; }
    }

    public bool IsGamePathEmpty => string.IsNullOrWhiteSpace(GamePath);

    // ── Summary display names (computed on last step) ─────────────

    public string? LanguageDisplayName { get; private set; }
    public string? DownloadSourceDisplayName { get; private set; }
    public string? ProxyDisplayName { get; private set; }

    // ── Events ────────────────────────────────────────────────────

    public event Func<LauncherSettings, Task>? SettingsApplied;

    /// <summary>Raised when the selected language should be previewed before settings are saved.</summary>
    public event Action<string>? LanguagePreviewRequested;

    // ── Commands ───────────────────────────────────────────────────

    [RelayCommand]
    private void Next()
    {
        if (Step >= 4) return;
        if (!CanGoNext) return;
        Step++;
        if (IsLastStep)
        {
            RefreshSummaryDisplayNames();
        }
    }

    [RelayCommand]
    private void Previous()
    {
        if (Step <= 0) return;
        Step--;
    }

    [RelayCommand]
    private async Task CompleteAsync()
    {
        await AsyncEvent.InvokeSequentiallyAsync(SettingsApplied, BuildSettings());
    }

    [RelayCommand]
    private void GoToStep(int targetStep)
    {
        if (targetStep < 0 || targetStep >= StepCount || targetStep > Step)
        {
            return;
        }

        Step = targetStep;
    }

    [RelayCommand]
    private async Task SkipAsync()
    {
        await AsyncEvent.InvokeSequentiallyAsync(SettingsApplied, LauncherSettings.CreateDefaults());
    }

    [RelayCommand]
    private async Task BrowseGamePathAsync()
    {
        var picked = await filePickerService.PickFolderAsync(
            localizer.T(LocalizationKeys.ChooseInstallFolder),
            GamePath);
        if (string.IsNullOrWhiteSpace(picked)) return;
        GamePath = gameInstallationPath.NormalizeGamePath(picked);
    }

    // ── Internal ──────────────────────────────────────────────────

    private async Task RefreshGamePathStatusDebouncedAsync()
    {
        var version = ++gamePathStatusVersion;
        CancelPendingGamePathStatusRefresh();

        if (string.IsNullOrWhiteSpace(GamePath))
        {
            GamePathStatus = SetupWizardGamePathStatus.NotSelected;
            return;
        }

        GamePathStatus = SetupWizardGamePathStatus.Checking;

        try
        {
            await Task.Delay(GamePathStatusDebounce);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // 防抖窗口内又有击键/刷新，或向导已释放则放弃本轮。
        if (version != gamePathStatusVersion || isDisposed)
        {
            return;
        }

        RefreshGamePathStatus(version);
    }

    private void RefreshGamePathStatus()
    {
        RefreshGamePathStatus(++gamePathStatusVersion);
    }

    private void RefreshGamePathStatus(int version)
    {
        CancelPendingGamePathStatusRefresh();

        if (string.IsNullOrWhiteSpace(GamePath))
        {
            GamePathStatus = SetupWizardGamePathStatus.NotSelected;
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        gamePathStatusCancellationTokenSource = cancellationTokenSource;
        _ = RefreshGamePathStatusAsync(GamePath, version, cancellationTokenSource);
    }

    private void CancelPendingGamePathStatusRefresh()
    {
        var previous = gamePathStatusCancellationTokenSource;
        gamePathStatusCancellationTokenSource = null;
        if (previous is not null)
        {
            // 在飞的旧代刷新已在任何 await 之前捕获 Token，Cancel+Dispose 不会
            // 再触碰源本身，可安全立即回收。
            previous.Cancel();
            previous.Dispose();
        }
    }

    private async Task RefreshGamePathStatusAsync(
        string path,
        int version,
        CancellationTokenSource cancellationTokenSource)
    {
        // 在任何 await 之前取 Token，避免旧代源被回收后访问 .Token 抛 ObjectDisposedException。
        var cancellationToken = cancellationTokenSource.Token;

        string normalizedPath;
        try
        {
            normalizedPath = gameInstallationPath.NormalizeGamePath(path);
        }
        catch (Exception ex)
        {
            _ = diagnostics.WarningAsync("SetupWizardGamePathNormalize", ex.Message, CancellationToken.None);
            SetGamePathStatusIfCurrent(
                SetupWizardGamePathStatus.Inaccessible,
                version,
                cancellationToken);
            return;
        }

        SetGamePathStatusIfCurrent(
            SetupWizardGamePathStatus.Checking,
            version,
            cancellationToken);

        try
        {
            var state = await localInstallationStateStore.ReadAsync(
                normalizedPath,
                cancellationToken);
            var status = state.Kind switch
            {
                LocalInstallationStateKind.NotInstalled => SetupWizardGamePathStatus.AvailableForInstallation,
                LocalInstallationStateKind.Valid => SetupWizardGamePathStatus.ValidInstallation,
                LocalInstallationStateKind.Corrupted => SetupWizardGamePathStatus.CorruptedInstallation,
                LocalInstallationStateKind.IoFailure => SetupWizardGamePathStatus.Inaccessible,
                _ => SetupWizardGamePathStatus.Inaccessible
            };

            // 全新安装要创建目录链、修复损坏安装要重写状态文件——两者都需要写权限。
            // 在选择阶段就探测（Program Files 等受保护位置此处即被拦下），已有效的
            // 安装只读即可运行，不做探测以免误伤非提权会话。探测是真实文件创建，
            // 移出 UI 线程避免慢速介质上阻塞输入。
            if (status is SetupWizardGamePathStatus.AvailableForInstallation
                or SetupWizardGamePathStatus.CorruptedInstallation)
            {
                var writable = await Task.Run(
                    () => DirectoryWriteProbe.CanCreate(normalizedPath),
                    cancellationToken);
                if (!writable)
                {
                    status = SetupWizardGamePathStatus.NotWritable;
                }
            }

            SetGamePathStatusIfCurrent(status, version, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _ = diagnostics.WarningAsync("SetupWizardGamePathRead", ex.Message, cancellationToken);
            SetGamePathStatusIfCurrent(
                SetupWizardGamePathStatus.Inaccessible,
                version,
                cancellationToken);
        }
    }

    private void SetGamePathStatusIfCurrent(
        SetupWizardGamePathStatus status,
        int version,
        CancellationToken cancellationToken)
    {
        if (version != gamePathStatusVersion
            || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        GamePathStatus = status;
    }

    private LauncherSettings BuildSettings()
    {
        var normalizedPath = gameInstallationPath.NormalizeGamePath(GamePath);
        var settings = LauncherSettings.CreateDefaults();
        settings.Language = Language;
        settings.PatchUrlGroup = PatchUrlGroup;
        settings.GamePath = normalizedPath;
        settings.ProxyMode = ProxyMode;
        return settings;
    }

    private void RefreshSummaryDisplayNames()
    {
        LanguageDisplayName = ResolveLanguageDisplayName();
        DownloadSourceDisplayName = ResolveDownloadSourceDisplayName();
        ProxyDisplayName = ResolveProxyDisplayName();
        OnPropertyChanged(nameof(LanguageDisplayName));
        OnPropertyChanged(nameof(DownloadSourceDisplayName));
        OnPropertyChanged(nameof(ProxyDisplayName));
    }

    private void RefreshDownloadSources()
    {
        var isCafeRecommended = Language is LauncherLanguages.SimplifiedChinese
            or LauncherLanguages.TraditionalChinese;
        DownloadSources =
        [
            new SetupWizardDownloadSourceItem(
                PatchUrlGroups.Cafe,
                localizer.T(LocalizationKeys.DownloadSourceCafe),
                isCafeRecommended,
                isCafeRecommended
                    ? localizer.T(LocalizationKeys.SetupWizardDownloadSourceCafeRecommendationReason)
                    : string.Empty),
            new SetupWizardDownloadSourceItem(
                PatchUrlGroups.Official,
                localizer.T(LocalizationKeys.DownloadSourceOfficial),
                false,
                string.Empty)
        ];
        OnPropertyChanged(nameof(DownloadSources));
    }

    private string ResolveGamePathPresentationDescription() => GamePathStatus switch
    {
        SetupWizardGamePathStatus.NotSelected => localizer.T(LocalizationKeys.SetupWizardGamePathEmpty),
        _ => GamePathStatusText
    };

    private void OnLocalizerLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(GamePathStatusText));
        OnPropertyChanged(nameof(GamePathPresentation));
        RefreshDownloadSources();
        if (IsLastStep)
        {
            RefreshSummaryDisplayNames();
        }
    }

    private string ResolveLanguageDisplayName() => Language switch
    {
        LauncherLanguages.English => "English",
        LauncherLanguages.SimplifiedChinese => "简体中文",
        LauncherLanguages.TraditionalChinese => "繁體中文",
        LauncherLanguages.Japanese => "日本語",
        _ => localizer.T(LocalizationKeys.LanguageAuto)
    };

    private string ResolveDownloadSourceDisplayName() => PatchUrlGroup switch
    {
        PatchUrlGroups.Cafe => localizer.T(LocalizationKeys.DownloadSourceCafe),
        _ => localizer.T(LocalizationKeys.DownloadSourceOfficial)
    };

    private string ResolveProxyDisplayName() => ProxyMode switch
    {
        ProxyModes.Direct => localizer.T(LocalizationKeys.ProxyDirect),
        ProxyModes.Auto => localizer.T(LocalizationKeys.ProxyAuto),
        ProxyModes.System => localizer.T(LocalizationKeys.ProxySystem),
        _ => ProxyMode
    };

    public void Dispose()
    {
        isDisposed = true;
        localizer.LanguageChanged -= OnLocalizerLanguageChanged;
        gamePathStatusCancellationTokenSource?.Dispose();
    }
}
