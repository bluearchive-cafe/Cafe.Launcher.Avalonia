using System;
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
    private readonly ISettingsEditor editor;
    private OldLauncherDetectionResult? detectionResult;

    // ── Visibility ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private bool isApplying;

    public ISettingsEditor Editor => editor;
    public SettingsOptionsViewModel Options { get; }

    // ── Status flags ────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool clickCodeFound;

    [ObservableProperty]
    private bool levelDbReadSuccess;

    // ── Coordination delegates — set by parent ──────────────────────────────

    public Func<Task<string?>>? PickGameFolderAsync { get; set; }

    // ── Events — parent subscribes ──────────────────────────────────────────

    public event Func<LauncherSettings, Task>? MigrationApplied;
    public event Func<Task>? MigrationSkipped;

    public MigrationWizardViewModel(
        ISettingsEditor editor,
        SettingsOptionsViewModel options)
    {
        this.editor = editor;
        Options = options;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>
    /// Loads detection results into the wizard, pre-filling UI controls.
    /// </summary>
    public void Load(OldLauncherDetectionResult result)
    {
        detectionResult = result;
        var settings = new LauncherSettings
        {
            GamePath = result.GamePath ?? "",
            ProxyMode = result.ProxyMode ?? ProxyModes.Direct,
            CloseBehavior = result.CloseBehavior ?? CloseBehaviors.Minimize
        };
        editor.ApplySnapshot(settings);

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
        Options.RefreshDisplayNames();
    }

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseGamePathAsync()
    {
        if (PickGameFolderAsync is null)
            return;

        var path = await PickGameFolderAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            editor.Current.GamePath = path;
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
            var settings = editor.GetSnapshot();

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
    private async Task SkipMigrationAsync()
    {
        if (MigrationSkipped is not null)
            await MigrationSkipped.Invoke();
    }
}
