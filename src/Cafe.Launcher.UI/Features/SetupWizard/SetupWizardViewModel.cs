using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using System.Linq;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.UI.Features.SetupWizard;

/// <summary>
/// Coordinates the first-launch setup wizard state, validation, and settings output.
/// </summary>
internal partial class SetupWizardViewModel : ViewModelBase, IModalContentViewModel, IDisposable, ILanguageAwarePresentation
{
    private const int StepCount = 7;
    private const int AgreementStep = 2;
    private const int GamePathStep = 3;
    private const int ReviewStep = 6;

    // 路径文本框 TwoWay 绑定下每个字符都会触发刷新；全量状态读取 + 写探测
    // 对 UNC/慢速介质是真实磁盘 IO，按此窗口防抖合并连续击键。
    private static readonly TimeSpan GamePathStatusDebounce = TimeSpan.FromMilliseconds(300);

    private readonly LocalizationService localizer;
    private readonly IGameInstallationPath gameInstallationPath;
    private readonly ILocalInstallationStateStore localInstallationStateStore;
    private readonly ILauncherDiagnostics diagnostics;
    private readonly IFilePickerService filePickerService;
    private readonly LauncherBuildIdentity? buildIdentity;
    private readonly Func<AgreementDocuments.Content> loadAgreement;
    private bool hasInitializedGamePath;
    private bool isDisposed;
    private CancellationTokenSource? gamePathStatusCancellationTokenSource;
    private readonly LatestRefresh gamePathStatusDebounce = new();
    private int gamePathStatusVersion;
    private LauncherSettings? baselineSettings;
    private AgreementDocuments.Content? agreement;

    /// <summary>
    /// Creates a setup wizard with defaults aligned to <see cref="LauncherSettings.CreateDefaults"/>.
    /// <paramref name="buildIdentity"/> carries the host's release channel so a pre-release build
    /// defaults the first-run settings to the beta update channel.
    /// <paramref name="agreementLoader"/> is injectable so tests can drive a document-load
    /// failure; production reads the embedded assets.
    /// </summary>
    public SetupWizardViewModel(
        LocalizationService localizer,
        IGameInstallationPath gameInstallationPath,
        ILocalInstallationStateStore localInstallationStateStore,
        ILauncherDiagnostics diagnostics,
        IFilePickerService filePickerService,
        LauncherBuildIdentity? buildIdentity = null,
        Func<AgreementDocuments.Content>? agreementLoader = null)
    {
        this.localizer = localizer;
        this.gameInstallationPath = gameInstallationPath;
        this.localInstallationStateStore = localInstallationStateStore;
        this.diagnostics = diagnostics;
        this.filePickerService = filePickerService;
        this.buildIdentity = buildIdentity;
        loadAgreement = agreementLoader ?? AgreementDocuments.Load;

        var defaults = LauncherSettings.CreateDefaults(buildIdentity);
        language = defaults.Language;
        patchUrlGroup = defaults.PatchUrlGroup;
        gamePath = defaults.GamePath;
        proxyMode = defaults.ProxyMode;
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
    [NotifyPropertyChangedFor(nameof(IsAgreementStep))]
    private int step;

    partial void OnStepChanged(int value)
    {
        if (value == GamePathStep && !hasInitializedGamePath)
        {
            hasInitializedGamePath = true;
            if (string.IsNullOrWhiteSpace(GamePath))
            {
                GamePath = gameInstallationPath.GetDefaultGamePath();
            }
        }

        if (value == GamePathStep)
        {
            RefreshGamePathStatus();
        }
    }

    public bool IsFirstStep => Step == 0;
    public bool IsLastStep => IsConsentUpdateMode ? Step == AgreementStep : Step == ReviewStep;
    public bool IsAgreementStep => Step == AgreementStep;

    /// <summary>
    /// 协议更新后重新征询时只保留协议步骤，完成即写回原设置；
    /// 此时不显示进度、上一步与跳过。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanSkip))]
    [NotifyPropertyChangedFor(nameof(StepProgress))]
    private bool isConsentUpdateMode;

    /// <summary>Gets the current step position for the wizard header.</summary>
    public string StepProgress => IsConsentUpdateMode
        ? localizer.F(LocalizationKeys.SetupWizardStepProgress, 1, 1)
        : localizer.F(LocalizationKeys.SetupWizardStepProgress, Step + 1, StepCount);

    public bool CanGoNext => Step switch
    {
        AgreementStep => IsAgreementAccepted,
        GamePathStep => IsGamePathReady,
        _ => true
    };

    public bool CanGoPrevious => !IsConsentUpdateMode && Step > 0;

    /// <summary>Gets whether the skip command may be used; consent must be accepted first.</summary>
    public bool CanSkip => IsAgreementAccepted && !IsConsentUpdateMode;

    // ── Agreement ──────────────────────────────────────────────────

    [ObservableProperty]
    private bool hasReadAgreement;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CanSkip))]
    private bool isAgreementAccepted;

    /// <summary>Gets whether the embedded agreement text loaded; false keeps the consent gate locked.</summary>
    public bool IsAgreementAvailable => AgreementDocumentContent.IsAvailable;

    /// <summary>Gets the embedded agreement and privacy text shown in the wizard.</summary>
    public string AgreementMarkdown
    {
        get
        {
            var content = AgreementDocumentContent;
            return content.IsAvailable
                ? content.Markdown
                : $"# {localizer.T(LocalizationKeys.AgreementUnavailable)}\n\n{content.LoadError}";
        }
    }

    private AgreementDocuments.Content AgreementDocumentContent =>
        agreement ??= loadAgreement();

    /// <summary>Gets the accepted agreement version caption.</summary>
    public string AgreementVersionText =>
        localizer.F(LocalizationKeys.AgreementVersion, LauncherAgreement.CurrentVersion);

    /// <summary>
    /// Enables the agreement checkbox once the user has reached the end of the documents.
    /// A failed document load keeps the gate locked: the short fallback text would
    /// otherwise satisfy the read geometry immediately (AUD-UI-005).
    /// </summary>
    public void MarkAgreementRead()
    {
        if (IsAgreementAvailable && !HasReadAgreement)
        {
            HasReadAgreement = true;
        }
    }

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
    [NotifyPropertyChangedFor(nameof(GamePathDisplay))]
    private string gamePath;

    partial void OnGamePathChanged(string value)
    {
        // 击键驱动的变更走防抖；进入步骤等程序性刷新仍为立即（见 OnStepChanged）。
        RefreshGamePathStatusDebounced();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GamePathStatusText))]
    [NotifyPropertyChangedFor(nameof(GamePathPresentation))]
    [NotifyPropertyChangedFor(nameof(IsGamePathChecking))]
    [NotifyPropertyChangedFor(nameof(IsGamePathReady))]
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

    /// <summary>
    /// 复核页路径展示值：段感知中间省略（见 <see cref="PathMiddleEllipsis"/>）。
    /// 仅作展示；设置输出的真值仍是 <see cref="GamePath"/>（BuildSettings 内再归一化）。
    /// </summary>
    public string GamePathDisplay => PathMiddleEllipsis.MiddleEllipsize(GamePath);

    // ── Summary display names (computed on last step) ─────────────

    public string? LanguageDisplayName { get; private set; }
    public string? DownloadSourceDisplayName { get; private set; }
    public string? ProxyDisplayName { get; private set; }

    /// <summary>Gets the review-step summary rows in edit order.</summary>
    public IReadOnlyList<SetupWizardSummaryItem> SummaryItems { get; private set; } = [];

    // ── Events ────────────────────────────────────────────────────

    public event Func<LauncherSettings, Task>? SettingsApplied;

    /// <summary>Raised when the selected language should be previewed before settings are saved.</summary>
    public event Action<string>? LanguagePreviewRequested;

    // ── Commands ───────────────────────────────────────────────────

    /// <summary>Raised when the user closes the wizard before accepting the agreement; the host must exit.</summary>
    public event Action? AgreementExitRequested;

    [RelayCommand]
    private void Next()
    {
        if (IsConsentUpdateMode || Step >= ReviewStep) return;
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
        if (IsConsentUpdateMode || Step <= 0) return;
        Step--;
    }

    [RelayCommand]
    private async Task CompleteAsync()
    {
        // 协议是完成向导的前置条件；重新征询模式下协议步骤同时是末步，也必须先勾选。
        if (!IsAgreementAccepted)
        {
            return;
        }

        await AsyncEvent.InvokeSequentiallyAsync(SettingsApplied, BuildSettings());
    }

    [RelayCommand]
    private void GoToStep(int targetStep)
    {
        if (IsConsentUpdateMode || targetStep < 0 || targetStep >= StepCount || targetStep > Step)
        {
            return;
        }

        Step = targetStep;
    }

    [RelayCommand]
    private async Task SkipAsync()
    {
        // 未同意协议时跳过等于绕过门禁：直接退出，绝不以默认设置继续。
        if (!IsAgreementAccepted || IsConsentUpdateMode)
        {
            AgreementExitRequested?.Invoke();
            return;
        }

        var settings = baselineSettings?.DeepClone() ?? LauncherSettings.CreateDefaults(buildIdentity);
        settings.AcceptedAgreementVersion = LauncherAgreement.CurrentVersion;
        await AsyncEvent.InvokeSequentiallyAsync(SettingsApplied, settings);
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

    private void RefreshGamePathStatusDebounced()
    {
        var version = ++gamePathStatusVersion;
        CancelPendingGamePathStatusRefresh();

        if (string.IsNullOrWhiteSpace(GamePath))
        {
            GamePathStatus = SetupWizardGamePathStatus.NotSelected;
            return;
        }

        GamePathStatus = SetupWizardGamePathStatus.Checking;

        gamePathStatusDebounce.Run(GamePathStatusDebounce, _ =>
        {
            // 防抖窗口内又有击键/刷新（版本号已前进），或向导已释放，则放弃本轮。
            if (version != gamePathStatusVersion || isDisposed)
            {
                return Task.CompletedTask;
            }

            RefreshGamePathStatus(version);
            return Task.CompletedTask;
        });
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
        if (IsConsentUpdateMode)
        {
            // 重新征询只更新协议版本；其余设置保持落盘原值，不经过任何步骤字段的归一化。
            var consentSettings = baselineSettings!.DeepClone();
            consentSettings.AcceptedAgreementVersion = LauncherAgreement.CurrentVersion;
            return consentSettings;
        }

        var normalizedPath = gameInstallationPath.NormalizeGamePath(GamePath);
        var settings = baselineSettings?.DeepClone() ?? LauncherSettings.CreateDefaults(buildIdentity);
        settings.Language = Language;
        settings.PatchUrlGroup = PatchUrlGroup;
        settings.GamePath = normalizedPath;
        settings.ProxyMode = ProxyMode;
        settings.AcceptedAgreementVersion = LauncherAgreement.CurrentVersion;
        return settings;
    }

    /// <summary>Prefills the wizard from saved settings instead of defaults.</summary>
    public void LoadFrom(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        baselineSettings = settings.DeepClone();
        Language = settings.Language;
        PatchUrlGroup = settings.PatchUrlGroup;
        GamePath = settings.GamePath;
        ProxyMode = settings.ProxyMode;
    }

    /// <summary>
    /// Prepares the wizard for re-accepting an updated agreement: saved settings are kept as the
    /// baseline, only the agreement step is shown, and completing writes the new version back.
    /// </summary>
    public void PrepareForAgreementUpdate(LauncherSettings settings)
    {
        LoadFrom(settings);
        IsAgreementAccepted = false;
        HasReadAgreement = false;
        IsConsentUpdateMode = true;
        Step = AgreementStep;
        RefreshDownloadSources();
    }

    private void RefreshSummaryDisplayNames()
    {
        LanguageDisplayName = ResolveLanguageDisplayName();
        DownloadSourceDisplayName = ResolveDownloadSourceDisplayName();
        ProxyDisplayName = ResolveProxyDisplayName();
        OnPropertyChanged(nameof(LanguageDisplayName));
        OnPropertyChanged(nameof(DownloadSourceDisplayName));
        OnPropertyChanged(nameof(ProxyDisplayName));

        var editLabel = localizer.T(LocalizationKeys.SetupWizardEditStep);
        SummaryItems =
        [
            new(MaterialIconKind.Translate, localizer.T(LocalizationKeys.SetupWizardLanguage),
                LanguageDisplayName ?? string.Empty, 1, GoToStepCommand, editLabel, HasDivider: false),
            new(MaterialIconKind.CloudDownload, localizer.T(LocalizationKeys.SetupWizardDownloadSource),
                DownloadSourceDisplayName ?? string.Empty, 4, GoToStepCommand, editLabel, HasDivider: true),
            new(MaterialIconKind.FolderSearch, localizer.T(LocalizationKeys.SetupWizardGamePath),
                GamePathDisplay, 3, GoToStepCommand, editLabel, HasDivider: true),
            new(MaterialIconKind.SwapHorizontal, localizer.T(LocalizationKeys.SetupWizardProxy),
                ProxyDisplayName ?? string.Empty, 5, GoToStepCommand, editLabel, HasDivider: true),
        ];
        OnPropertyChanged(nameof(SummaryItems));
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

    /// <inheritdoc cref="ILanguageAwarePresentation.RefreshLocalizedText"/>
    /// <remarks>
    /// 此前靠自订阅 <c>localizer.LanguageChanged</c> 触发（D10 移除）：语言变化统一由
    /// Shell 遍历呈现族分发，向导经 <see cref="DialogsViewModel"/>（它的宿主）被刷新。
    /// </remarks>
    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(GamePathStatusText));
        OnPropertyChanged(nameof(GamePathPresentation));
        OnPropertyChanged(nameof(AgreementVersionText));
        OnPropertyChanged(nameof(StepProgress));
        RefreshDownloadSources();
        if (IsLastStep)
        {
            RefreshSummaryDisplayNames();
        }
    }

    /// <summary>
    /// 语言显示名取自 <see cref="LocalizationService.GetLanguageOptions"/> 这一处词表：
    /// 本方法原先把四个语言名各手抄一遍（含「自动」的兜底），与设置页那份是两份独立声明。
    /// </summary>
    private string ResolveLanguageDisplayName() =>
        LocalizationService.GetLanguageOptions(localizer)
            .FirstOrDefault(option => option.Code == Language)?.DisplayName
        ?? localizer.T(LocalizationKeys.LanguageAuto);

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
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        gamePathStatusDebounce.Cancel();
        CancelPendingGamePathStatusRefresh();
    }
}
