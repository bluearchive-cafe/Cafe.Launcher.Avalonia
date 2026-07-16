using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>
/// Coordinates the first-launch setup wizard state, validation, and settings output.
/// </summary>
public partial class SetupWizardViewModel : ViewModelBase, IModalContentViewModel
{
    private readonly LocalizationService localizer;
    private readonly GameInstallationPath gameInstallationPath;

    /// <summary>
    /// Creates a setup wizard with defaults aligned to <see cref="LauncherSettings.CreateDefaults"/>.
    /// </summary>
    public SetupWizardViewModel(LocalizationService localizer, GameInstallationPath gameInstallationPath)
    {
        this.localizer = localizer;
        this.gameInstallationPath = gameInstallationPath;

        var defaults = LauncherSettings.CreateDefaults();
        language = defaults.Language;
        patchUrlGroup = defaults.PatchUrlGroup;
        gamePath = defaults.GamePath;
        proxyMode = defaults.ProxyMode;
        Steps =
        [
            CreateStep(0),
            CreateStep(1),
            CreateStep(2),
            CreateStep(3),
            CreateStep(4)
        ];
        RefreshSteps();
    }

    /// <summary>Folder picker delegate, set by MainWindowViewModel.WireChildren().</summary>
    public Func<string, Task<string?>>? PickGameFolderAsync { get; set; }

    /// <summary>Gets the five ordered navigation steps and their current states.</summary>
    public ObservableCollection<SetupWizardStepItem> Steps { get; }

    // ── Step state ───────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstStep))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(StepTitle))]
    [NotifyPropertyChangedFor(nameof(StepProgress))]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(SelectedStep))]
    private int step;

    partial void OnStepChanged(int value) => RefreshSteps();

    public bool IsFirstStep => Step == 0;
    public bool IsLastStep => Step == 4;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;

    /// <summary>Gets the current step position for the wizard header.</summary>
    public string StepProgress => $"{Step + 1} / {Steps.Count}";

    /// <summary>Gets or sets the step selected through the navigation list.</summary>
    public int SelectedStep
    {
        get => Step;
        set
        {
            if (value == Step)
            {
                return;
            }

            if (value < 0 || value >= Steps.Count || value > Step)
            {
                OnPropertyChanged();
                return;
            }

            Step = value;
        }
    }

    public bool CanGoNext => Step switch
    {
        1 => !string.IsNullOrWhiteSpace(GamePath),
        _ => true
    };

    public bool CanGoPrevious => Step > 0;

    public string StepTitle => Step switch
    {
        0 => localizer.T("setupWizardLanguage"),
        1 => localizer.T("setupWizardGamePath"),
        2 => localizer.T("setupWizardDownloadSource"),
        3 => localizer.T("setupWizardProxy"),
        4 => localizer.T("setupWizardReview"),
        _ => ""
    };

    // ── Settings ──────────────────────────────────────────────────

    [ObservableProperty]
    private string language;

    partial void OnLanguageChanged(string value) => RefreshSteps();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsPatchUrlGroupCafe))]
    [NotifyPropertyChangedFor(nameof(IsPatchUrlGroupOfficial))]
    private string patchUrlGroup;

    partial void OnPatchUrlGroupChanged(string value) => RefreshSteps();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsGamePathEmpty))]
    private string gamePath;

    partial void OnGamePathChanged(string value) => RefreshSteps();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProxyAuto))]
    [NotifyPropertyChangedFor(nameof(IsProxyDirect))]
    [NotifyPropertyChangedFor(nameof(IsProxySystem))]
    private string proxyMode;

    partial void OnProxyModeChanged(string value) => RefreshSteps();

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
        if (targetStep < 0 || targetStep >= Steps.Count || targetStep > Step)
        {
            return;
        }

        SelectedStep = targetStep;
    }

    [RelayCommand]
    private void SelectCafeDownloadSource() => PatchUrlGroup = PatchUrlGroups.Cafe;

    [RelayCommand]
    private void SelectOfficialDownloadSource() => PatchUrlGroup = PatchUrlGroups.Official;

    [RelayCommand]
    private void SelectProxyAuto() => ProxyMode = ProxyModes.Auto;

    [RelayCommand]
    private void SelectProxyDirect() => ProxyMode = ProxyModes.Direct;

    [RelayCommand]
    private void SelectProxySystem() => ProxyMode = ProxyModes.System;

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

    private SetupWizardStepItem CreateStep(int index) => new()
    {
        Index = index,
        Title = index switch
        {
            0 => localizer.T("setupWizardLanguage"),
            1 => localizer.T("setupWizardGamePath"),
            2 => localizer.T("setupWizardDownloadSource"),
            3 => localizer.T("setupWizardProxy"),
            4 => localizer.T("setupWizardReview"),
            _ => ""
        }
    };

    private void RefreshSteps()
    {
        if (Steps is null)
        {
            return;
        }

        foreach (var item in Steps)
        {
            item.State = item.Index < Step
                ? SetupWizardStepState.Completed
                : item.Index == Step
                    ? SetupWizardStepState.Current
                    : SetupWizardStepState.Locked;
        }
    }

    private string ResolveLanguageDisplayName() => Language switch
    {
        LauncherLanguages.English => "English",
        LauncherLanguages.SimplifiedChinese => "简体中文",
        LauncherLanguages.TraditionalChinese => "繁體中文",
        LauncherLanguages.Japanese => "日本語",
        _ => localizer.T("language") + " (Auto)"
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
}
