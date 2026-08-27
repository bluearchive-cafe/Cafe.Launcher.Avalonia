using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Features.Shell;
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

    private readonly LocalizationService localizer;
    private readonly GameInstallationPath gameInstallationPath;
    private readonly LocalInstallationStateStore localInstallationStateStore;
    private readonly LocalDiagnostics diagnostics;
    private bool hasInitializedGamePath;
    private CancellationTokenSource? gamePathStatusCancellationTokenSource;
    private int gamePathStatusVersion;

    /// <summary>
    /// Creates a setup wizard with defaults aligned to <see cref="LauncherSettings.CreateDefaults"/>.
    /// </summary>
    public SetupWizardViewModel(
        LocalizationService localizer,
        GameInstallationPath gameInstallationPath,
        LocalInstallationStateStore localInstallationStateStore,
        LocalDiagnostics diagnostics)
    {
        this.localizer = localizer;
        this.gameInstallationPath = gameInstallationPath;
        this.localInstallationStateStore = localInstallationStateStore;
        this.diagnostics = diagnostics;

        var defaults = LauncherSettings.CreateDefaults();
        language = defaults.Language;
        patchUrlGroup = defaults.PatchUrlGroup;
        gamePath = defaults.GamePath;
        proxyMode = defaults.ProxyMode;
        localizer.LanguageChanged += OnLocalizerLanguageChanged;
        RefreshDownloadSources();
    }

    /// <summary>Folder picker delegate, set by MainWindowViewModel.WireChildren().</summary>
    public Func<string, Task<string?>>? PickGameFolderAsync { get; set; }

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
        RefreshGamePathStatus();
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
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private SetupWizardGamePathStatus gamePathStatus;

    public string GamePathStatusText => GamePathStatus switch
    {
        SetupWizardGamePathStatus.Checking => localizer.T("setupWizardGamePathChecking"),
        SetupWizardGamePathStatus.AvailableForInstallation => localizer.T("setupWizardGamePathAvailable"),
        SetupWizardGamePathStatus.ValidInstallation => localizer.T("setupWizardGamePathInstalled"),
        SetupWizardGamePathStatus.CorruptedInstallation => localizer.T("setupWizardGamePathCorrupted"),
        SetupWizardGamePathStatus.Inaccessible => localizer.T("setupWizardGamePathInaccessible"),
        _ => string.Empty
    };

    /// <summary>Gets the localized title and description for the current game path status.</summary>
    public SetupWizardGamePathPresentation GamePathPresentation => new(
        localizer.T("setupWizardGamePathStatusTitle"),
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
        if (PickGameFolderAsync is null) return;
        var picked = await PickGameFolderAsync(GamePath);
        if (string.IsNullOrWhiteSpace(picked)) return;
        GamePath = gameInstallationPath.NormalizeGamePath(picked);
    }

    // ── Internal ──────────────────────────────────────────────────

    private void RefreshGamePathStatus()
    {
        gamePathStatusCancellationTokenSource?.Cancel();
        gamePathStatusCancellationTokenSource?.Dispose();
        var cancellationTokenSource = new CancellationTokenSource();
        gamePathStatusCancellationTokenSource = cancellationTokenSource;
        var version = ++gamePathStatusVersion;

        if (string.IsNullOrWhiteSpace(GamePath))
        {
            GamePathStatus = SetupWizardGamePathStatus.NotSelected;
            return;
        }

        _ = RefreshGamePathStatusAsync(GamePath, version, cancellationTokenSource);
    }

    private async Task RefreshGamePathStatusAsync(
        string path,
        int version,
        CancellationTokenSource cancellationTokenSource)
    {
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
                cancellationTokenSource);
            return;
        }

        SetGamePathStatusIfCurrent(
            SetupWizardGamePathStatus.Checking,
            version,
            cancellationTokenSource);

        try
        {
            var state = await localInstallationStateStore.ReadAsync(
                normalizedPath,
                cancellationTokenSource.Token);
            var status = state.Kind switch
            {
                LocalInstallationStateKind.NotInstalled => SetupWizardGamePathStatus.AvailableForInstallation,
                LocalInstallationStateKind.Valid => SetupWizardGamePathStatus.ValidInstallation,
                LocalInstallationStateKind.Corrupted => SetupWizardGamePathStatus.CorruptedInstallation,
                LocalInstallationStateKind.IoFailure => SetupWizardGamePathStatus.Inaccessible,
                _ => SetupWizardGamePathStatus.Inaccessible
            };
            SetGamePathStatusIfCurrent(status, version, cancellationTokenSource);
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _ = diagnostics.WarningAsync("SetupWizardGamePathRead", ex.Message, cancellationTokenSource.Token);
            SetGamePathStatusIfCurrent(
                SetupWizardGamePathStatus.Inaccessible,
                version,
                cancellationTokenSource);
        }
    }

    private void SetGamePathStatusIfCurrent(
        SetupWizardGamePathStatus status,
        int version,
        CancellationTokenSource cancellationTokenSource)
    {
        if (version != gamePathStatusVersion
            || cancellationTokenSource.IsCancellationRequested
            || !ReferenceEquals(cancellationTokenSource, gamePathStatusCancellationTokenSource))
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
                localizer.T("downloadSourceCafe"),
                isCafeRecommended,
                isCafeRecommended
                    ? localizer.T("setupWizardDownloadSourceCafeRecommendationReason")
                    : string.Empty),
            new SetupWizardDownloadSourceItem(
                PatchUrlGroups.Official,
                localizer.T("downloadSourceOfficial"),
                false,
                string.Empty)
        ];
        OnPropertyChanged(nameof(DownloadSources));
    }

    private string ResolveGamePathPresentationDescription() => GamePathStatus switch
    {
        SetupWizardGamePathStatus.NotSelected => localizer.T("setupWizardGamePathEmpty"),
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
        _ => localizer.T("languageAuto")
    };

    private string ResolveDownloadSourceDisplayName() => PatchUrlGroup switch
    {
        PatchUrlGroups.Cafe => localizer.T("downloadSourceCafe"),
        _ => localizer.T("downloadSourceOfficial")
    };

    private string ResolveProxyDisplayName() => ProxyMode switch
    {
        ProxyModes.Direct => localizer.T("proxyDirect"),
        ProxyModes.Auto => localizer.T("proxyAuto"),
        ProxyModes.System => localizer.T("proxySystem"),
        _ => ProxyMode
    };

    public void Dispose()
    {
        localizer.LanguageChanged -= OnLocalizerLanguageChanged;
        gamePathStatusCancellationTokenSource?.Dispose();
    }
}
