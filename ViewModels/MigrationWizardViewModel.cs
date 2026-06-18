using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>
/// Controls the old-launcher configuration migration dialog.
/// Shows detected settings from the previous Electron launcher and
/// lets the user review, adjust, and apply them.
/// </summary>
public partial class MigrationWizardViewModel : ViewModelBase
{
    private readonly LocalizationService localizer;
    private OldLauncherDetectionResult? detectionResult;

    // ── Visibility ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private bool isApplying;

    // ── Detected game path ──────────────────────────────────────────────────

    [ObservableProperty]
    private string detectedGamePath = "";

    [ObservableProperty]
    private bool gamePathFound;

    // ── Proxy mode selection ────────────────────────────────────────────────

    [ObservableProperty]
    private int selectedProxyModeIndex;

    public ObservableCollection<SettingOption> ProxyModeOptions { get; } =
    [
        new() { Code = ProxyModes.Direct },
        new() { Code = ProxyModes.System }
    ];

    // ── Close behavior selection ────────────────────────────────────────────

    [ObservableProperty]
    private int selectedCloseBehaviorIndex;

    public ObservableCollection<SettingOption> CloseBehaviorOptions { get; } =
    [
        new() { Code = CloseBehaviors.Minimize },
        new() { Code = CloseBehaviors.Exit }
    ];

    // ── Status flags ────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool clickCodeFound;

    [ObservableProperty]
    private bool levelDbReadSuccess;

    // ── Coordination delegates — set by parent ──────────────────────────────

    public Func<Task<string?>>? BrowseGamePathAsync { get; set; }

    // ── Events — parent subscribes ──────────────────────────────────────────

    public event Func<LauncherSettings, Task>? MigrationApplied;
    public event Func<Task>? MigrationSkipped;

    public MigrationWizardViewModel(LocalizationService localizer)
    {
        this.localizer = localizer;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>
    /// Loads detection results into the wizard, pre-filling UI controls.
    /// </summary>
    public void Load(OldLauncherDetectionResult result)
    {
        detectionResult = result;

        if (!string.IsNullOrWhiteSpace(result.GamePath))
        {
            DetectedGamePath = result.GamePath;
            GamePathFound = true;
        }

        // Map proxy mode to combo box index
        SelectedProxyModeIndex = result.ProxyMode switch
        {
            ProxyModes.System => 1,
            _ => 0
        };

        // Map close behavior to combo box index
        SelectedCloseBehaviorIndex = result.CloseBehavior switch
        {
            CloseBehaviors.Exit => 1,
            _ => 0
        };

        ClickCodeFound = result.ClickCodeFound;
        LevelDbReadSuccess = result.LevelDbReadSuccess;

        RefreshDisplayNames();
    }

    /// <summary>
    /// Refreshes localized display names on option collections.
    /// Called after language change.
    /// </summary>
    public void RefreshDisplayNames()
    {
        foreach (var option in ProxyModeOptions)
        {
            option.DisplayName = option.Code switch
            {
                ProxyModes.System => localizer.T("proxySystem"),
                _ => localizer.T("proxyDirect")
            };
        }

        foreach (var option in CloseBehaviorOptions)
        {
            option.DisplayName = option.Code switch
            {
                CloseBehaviors.Exit => localizer.T("closeBehaviorExit"),
                _ => localizer.T("closeBehaviorMinimize")
            };
        }
    }

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseGamePath()
    {
        if (BrowseGamePathAsync is null)
            return;

        var path = await BrowseGamePathAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            DetectedGamePath = path;
            GamePathFound = true;
        }
    }

    [RelayCommand]
    private async Task ApplyMigrationAsync()
    {
        if (IsApplying)
            return;

        IsApplying = true;
        try
        {
            var settings = new LauncherSettings
            {
                GamePath = DetectedGamePath
            };

            // Apply proxy mode from combo box
            if (SelectedProxyModeIndex >= 0 && SelectedProxyModeIndex < ProxyModeOptions.Count)
                settings.ProxyMode = ProxyModeOptions[SelectedProxyModeIndex].Code;

            // Apply close behavior from combo box
            if (SelectedCloseBehaviorIndex >= 0 && SelectedCloseBehaviorIndex < CloseBehaviorOptions.Count)
                settings.CloseBehavior = CloseBehaviorOptions[SelectedCloseBehaviorIndex].Code;

            // Copy clickCode from old launcher if found
            if (ClickCodeFound && detectionResult is not null)
                OldLauncherDetectionService.CopyClickCode(detectionResult.OldUserDataPath);

            if (MigrationApplied is not null)
                await MigrationApplied(settings);
        }
        finally
        {
            IsApplying = false;
        }
    }

    [RelayCommand]
    private async Task SkipMigration()
    {
        if (MigrationSkipped is not null)
            await MigrationSkipped.Invoke();
    }
}
