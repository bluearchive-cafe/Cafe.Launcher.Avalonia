using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ShellStartupTests : IDisposable
{
    private readonly SetupWizardViewModel wizard;

    static ShellStartupTests()
    {
        TestLocalizationHelper.Initialize();
    }

    public ShellStartupTests()
    {
        wizard = new SetupWizardViewModel(
            new LocalizationService(),
            new GameInstallationPath(),
            new LocalInstallationStateStore(),
            new LocalDiagnostics());
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
    public async Task HandleSetupWizardCompletedAsync_SavesAppliesHidesThenRefreshes()
    {
        var sequence = new List<string>();
        var startup = CreateStartup(
            save: _ =>
            {
                sequence.Add("save");
                return Task.CompletedTask;
            },
            applyLanguage: _ => sequence.Add("language"),
            hide: () => sequence.Add("hide"),
            refresh: _ =>
            {
                sequence.Add("refresh");
                return Task.CompletedTask;
            });

        await startup.HandleSetupWizardCompletedAsync(LauncherSettings.CreateDefaults());

        Assert.Equal(new[] { "save", "language", "hide", "refresh" }, sequence);
    }

    [Fact]
    public async Task Wire_WhenWizardCompletes_RunsCompletionFlow()
    {
        var refreshCount = 0;
        var startup = CreateStartup(refresh: _ =>
        {
            refreshCount++;
            return Task.CompletedTask;
        });

        startup.Wire();
        wizard.GamePath = @"D:\Games\Path";
        await wizard.CompleteCommand.ExecuteAsync(null);

        Assert.Equal(1, refreshCount);
        Assert.NotNull(wizard.PickGameFolderAsync);
    }

    [Fact]
    public async Task Unwire_WhenWizardCompletes_DoesNotRunCompletionFlow()
    {
        var refreshCount = 0;
        var startup = CreateStartup(refresh: _ =>
        {
            refreshCount++;
            return Task.CompletedTask;
        });

        startup.Wire();
        startup.Unwire();
        wizard.GamePath = @"D:\Games\Path";
        await wizard.CompleteCommand.ExecuteAsync(null);

        Assert.Equal(0, refreshCount);
        Assert.Null(wizard.PickGameFolderAsync);
    }

    [Fact]
    public void Unwire_WhenWizardPickerNotOwned_LeavesForeignPickerAssigned()
    {
        var foreignPicker = new Func<string, Task<string?>>(_ => Task.FromResult<string?>(null));
        var startup = CreateStartup(picker: _ => Task.FromResult<string?>(null));

        startup.Wire();
        wizard.PickGameFolderAsync = foreignPicker;
        startup.Unwire();

        Assert.Same(foreignPicker, wizard.PickGameFolderAsync);
    }

    private ShellStartup CreateStartup(
        Action<LauncherSettings>? applyMotion = null,
        Action<string>? applyLanguage = null,
        Func<LauncherSettings, Task>? save = null,
        Action? hide = null,
        Func<CancellationToken, Task>? refresh = null,
        Func<string, Task<string?>>? picker = null,
        Func<bool>? isWizardVisible = null) =>
        new(
            refresh ?? (_ => Task.CompletedTask),
            applyMotion ?? (_ => { }),
            applyLanguage ?? (_ => { }),
            save ?? (_ => Task.CompletedTask),
            hide ?? (() => { }),
            picker ?? (_ => Task.FromResult<string?>(null)),
            isWizardVisible ?? (() => true),
            wizard);
}
