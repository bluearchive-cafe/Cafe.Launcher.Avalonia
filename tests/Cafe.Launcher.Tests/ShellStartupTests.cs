using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Features.SetupWizard;
using Cafe.Launcher.UI.Features.Shell;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class ShellStartupTests : IDisposable
{
    private readonly SetupWizardViewModel wizard;

    public ShellStartupTests()
    {
        wizard = new SetupWizardViewModel(
            new LocalizationService(),
            new GameInstallationPath(LauncherProfiles.BlueArchiveJapan),
            new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan),
            new LocalDiagnostics(),
            new StubFilePickerService());
    }

    public void Dispose() => wizard.Dispose();

    [Fact]
    public async Task InitializeAsync_WhenCalledTwice_RefreshesOnlyOnce()
    {
        var refreshCount = 0;
        var startup = CreateStartup(refresh: _ =>
        {
            refreshCount++;
            return Task.CompletedTask;
        });

        await startup.InitializeAsync();
        await startup.InitializeAsync();

        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void ApplyFirstLaunchMotionPreference_AppliesDefaultSettings()
    {
        LauncherSettings? applied = null;
        var startup = CreateStartup(applyMotion: settings => applied = settings);

        startup.ApplyFirstLaunchMotionPreference();

        Assert.NotNull(applied);
        Assert.Equal(LauncherSettings.CreateDefaults().MotionMode, applied!.MotionMode);
    }

    [Fact]
    public void ApplyInitialLanguage_AppliesAutoLanguage()
    {
        string? applied = null;
        var startup = CreateStartup(applyLanguage: language => applied = language);

        startup.ApplyInitialLanguage();

        Assert.Equal(LauncherLanguages.Auto, applied);
    }

    [Fact]
    public async Task HandleSetupWizardCompletedAsync_SavesAppliesEffectsHidesThenCompletes()
    {
        var sequence = new List<string>();
        var startup = CreateStartup(
            save: settings =>
            {
                sequence.Add("save");
                return Task.FromResult(settings);
            },
            followThrough: new RecordingFollowThrough(sequence),
            hide: () => sequence.Add("hide"));

        await startup.HandleSetupWizardCompletedAsync(LauncherSettings.CreateDefaults());

        Assert.Equal(new[] { "save", "effects", "hide", "complete" }, sequence);
    }

    [Fact]
    public async Task Wire_WhenWizardCompletes_RunsCompletionFlow()
    {
        var followThrough = new RecordingFollowThrough();
        var startup = CreateStartup(followThrough: followThrough);

        startup.Wire();
        wizard.MarkAgreementRead();
        wizard.IsAgreementAccepted = true;
        wizard.GamePath = @"D:\Games\Path";
        await wizard.CompleteCommand.ExecuteAsync(null);

        Assert.Equal(1, followThrough.ApplyCount);
        Assert.Equal(1, followThrough.CompleteCount);
    }

    [Fact]
    public async Task Unwire_WhenWizardCompletes_DoesNotRunCompletionFlow()
    {
        var followThrough = new RecordingFollowThrough();
        var startup = CreateStartup(followThrough: followThrough);

        startup.Wire();
        startup.Unwire();
        wizard.MarkAgreementRead();
        wizard.IsAgreementAccepted = true;
        wizard.GamePath = @"D:\Games\Path";
        await wizard.CompleteCommand.ExecuteAsync(null);

        Assert.Equal(0, followThrough.ApplyCount);
        Assert.Equal(0, followThrough.CompleteCount);
    }

    private ShellStartup CreateStartup(
        Action<LauncherSettings>? applyMotion = null,
        Action<string>? applyLanguage = null,
        Func<LauncherSettings, Task<LauncherSettings>>? save = null,
        Action? hide = null,
        Func<CancellationToken, Task>? refresh = null,
        Func<bool>? isWizardVisible = null,
        ISavedSettingsFollowThrough? followThrough = null) =>
        new(
            refresh ?? (_ => Task.CompletedTask),
            applyMotion ?? (_ => { }),
            applyLanguage ?? (_ => { }),
            save ?? (settings => Task.FromResult(settings)),
            followThrough ?? new RecordingFollowThrough(),
            hide ?? (() => { }),
            isWizardVisible ?? (() => true),
            wizard);

    private sealed class RecordingFollowThrough(List<string>? sequence = null) : ISavedSettingsFollowThrough
    {
        public int ApplyCount { get; private set; }
        public int CompleteCount { get; private set; }

        public SavedSettingsHandoff Apply(LauncherSettings savedSettings)
        {
            ApplyCount++;
            sequence?.Add("effects");
            return new SavedSettingsHandoff(savedSettings, null);
        }

        public Task CompleteAsync(SavedSettingsHandoff handoff)
        {
            CompleteCount++;
            sequence?.Add("complete");
            return Task.CompletedTask;
        }
    }
}
