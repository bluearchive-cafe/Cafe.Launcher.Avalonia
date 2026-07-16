using System;
using System.Threading.Tasks;
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
    }

    /// <summary>Folder picker delegate, set by MainWindowViewModel.WireChildren().</summary>
    public Func<string, Task<string?>>? PickGameFolderAsync { get; set; }

    // ── Step state ───────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstStep))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(StepTitle))]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    private int step;

    public bool IsFirstStep => Step == 0;
    public bool IsLastStep => Step == 4;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;

    public bool CanGoNext => Step switch
    {
        2 => !string.IsNullOrWhiteSpace(GamePath),
        _ => true
    };

    public bool CanGoPrevious => Step > 0;

    public string StepTitle => Step switch
    {
        0 => localizer.T("setupWizardStep0Title"),
        1 => localizer.T("setupWizardStep1Title"),
        2 => localizer.T("setupWizardStep2Title"),
        3 => localizer.T("setupWizardStep3Title"),
        4 => localizer.T("setupWizardStep4Title"),
        _ => ""
    };

    // ── Settings ──────────────────────────────────────────────────

    [ObservableProperty]
    private string language;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsPatchUrlGroupCafe))]
    [NotifyPropertyChangedFor(nameof(IsPatchUrlGroupOfficial))]
    private string patchUrlGroup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsGamePathEmpty))]
    private string gamePath;

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
        LanguageDisplayName = Language switch
        {
            LauncherLanguages.English => "English",
            LauncherLanguages.SimplifiedChinese => "简体中文",
            LauncherLanguages.TraditionalChinese => "繁體中文",
            LauncherLanguages.Japanese => "日本語",
            _ => localizer.T("language") + " (Auto)"
        };
        DownloadSourceDisplayName = PatchUrlGroup switch
        {
            PatchUrlGroups.Cafe => localizer.T("downloadSourceCafe"),
            _ => localizer.T("downloadSourceOfficial")
        };
        ProxyDisplayName = ProxyMode switch
        {
            ProxyModes.Direct => localizer.T("proxyDirect"),
            ProxyModes.Auto => localizer.T("proxyAuto"),
            ProxyModes.System => localizer.T("proxySystem"),
            _ => ProxyMode
        };
        OnPropertyChanged(nameof(LanguageDisplayName));
        OnPropertyChanged(nameof(DownloadSourceDisplayName));
        OnPropertyChanged(nameof(ProxyDisplayName));
    }
}
