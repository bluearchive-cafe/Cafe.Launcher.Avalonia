using System;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class SettingsChangeFanoutTests
{
    [Fact]
    public async Task ApplySavedChangesAsync_WhenDownloadRunning_RefreshesSnapshotOnly()
    {
        var refreshCount = 0;
        var snapshot = new LauncherStatusSnapshot { Settings = LauncherSettings.CreateDefaults() };
        var refreshedSettings = LauncherSettings.CreateDefaults();
        refreshedSettings.PatchUrlGroup = PatchUrlGroups.Cafe;
        var fanout = CreateFanout(
            getSavedSettings: () => snapshot.Settings,
            getCurrentSnapshot: () => snapshot,
            isDownloadRunning: () => true,
            readSettings: () => Task.FromResult(refreshedSettings),
            refresh: () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            });

        await fanout.ApplySavedChangesAsync();

        Assert.Equal(0, refreshCount);
        Assert.Equal(PatchUrlGroups.Cafe, snapshot.Settings.PatchUrlGroup);
    }

    [Fact]
    public async Task ApplySavedChangesAsync_WhenPatchSourceChangedAndReady_ShowsRepairConfirmation()
    {
        var refreshCount = 0;
        string? shownPrompt = null;
        var fanout = CreateFanout(
            getSavedSettings: () => new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Cafe },
            getPreviousPatchUrlGroup: () => PatchUrlGroups.Official,
            getCurrentSnapshot: () => new LauncherStatusSnapshot
            {
                RuntimeState = LauncherRuntimeState.Ready
            },
            refresh: () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            },
            showRepair: prompt => shownPrompt = prompt);

        await fanout.ApplySavedChangesAsync();

        Assert.Equal(1, refreshCount);
        Assert.Equal("repair-prompt", shownPrompt);
    }

    [Fact]
    public async Task ApplySavedChangesAsync_WhenPatchSourceUnchanged_DoesNotShowRepair()
    {
        var showRepairCalled = false;
        var fanout = CreateFanout(
            getSavedSettings: () => new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Official },
            getPreviousPatchUrlGroup: () => PatchUrlGroups.Official,
            getCurrentSnapshot: () => new LauncherStatusSnapshot
            {
                RuntimeState = LauncherRuntimeState.Ready
            },
            showRepair: _ => showRepairCalled = true);

        await fanout.ApplySavedChangesAsync();

        Assert.False(showRepairCalled);
    }

    [Fact]
    public async Task ApplySavedChangesAsync_WhenRuntimeNotReady_DoesNotShowRepair()
    {
        var showRepairCalled = false;
        var fanout = CreateFanout(
            getPreviousPatchUrlGroup: () => PatchUrlGroups.Official,
            getCurrentSnapshot: () => new LauncherStatusSnapshot
            {
                RuntimeState = LauncherRuntimeState.Corrupted
            },
            showRepair: _ => showRepairCalled = true);

        await fanout.ApplySavedChangesAsync();

        Assert.False(showRepairCalled);
    }

    [Fact]
    public async Task ApplySavedChangesAsync_WhenIdle_AppliesImmediatePresentationBeforeRefresh()
    {
        var sequence = new System.Collections.Generic.List<string>();
        var fanout = CreateFanout(
            applyPresentation: _ => sequence.Add("presentation"),
            refresh: () =>
            {
                sequence.Add("refresh");
                return Task.CompletedTask;
            });

        await fanout.ApplySavedChangesAsync();

        Assert.Equal(new[] { "presentation", "refresh" }, sequence);
    }

    private static SettingsChangeFanout CreateFanout(
        Func<LauncherSettings>? getSavedSettings = null,
        Func<string?>? getPreviousPatchUrlGroup = null,
        Func<LauncherStatusSnapshot?>? getCurrentSnapshot = null,
        Action<LauncherSettings>? applyPresentation = null,
        Func<bool>? isDownloadRunning = null,
        Func<Task<LauncherSettings>>? readSettings = null,
        Func<Task>? refresh = null,
        Action<string>? showRepair = null) =>
        new(
            getSavedSettings ?? (() => LauncherSettings.CreateDefaults()),
            getPreviousPatchUrlGroup ?? (() => null),
            getCurrentSnapshot ?? (() => null),
            applyPresentation ?? (_ => { }),
            isDownloadRunning ?? (() => false),
            readSettings ?? (() => Task.FromResult(LauncherSettings.CreateDefaults())),
            refresh ?? (() => Task.CompletedTask),
            () => "repair-prompt",
            showRepair ?? (_ => { }));
}
