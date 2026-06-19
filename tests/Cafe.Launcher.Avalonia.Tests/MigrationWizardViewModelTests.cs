using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class MigrationWizardViewModelTests
{
    static MigrationWizardViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void Constructor_InitializesOptionCollections()
    {
        var localizer = CreateLocalizer();
        var vm = new MigrationWizardViewModel(localizer);

        Assert.Equal(2, vm.ProxyModeOptions.Count);
        Assert.Equal(ProxyModes.Direct, vm.ProxyModeOptions[0].Code);
        Assert.Equal(ProxyModes.System, vm.ProxyModeOptions[1].Code);

        Assert.Equal(2, vm.CloseBehaviorOptions.Count);
        Assert.Equal(CloseBehaviors.Minimize, vm.CloseBehaviorOptions[0].Code);
        Assert.Equal(CloseBehaviors.Exit, vm.CloseBehaviorOptions[1].Code);

        Assert.False(vm.IsVisible);
        Assert.False(vm.IsApplying);
        Assert.Equal("", vm.DetectedGamePath);
    }

    [Fact]
    public void Load_WithFullDetection_PopulatesAllFields()
    {
        var localizer = CreateLocalizer();
        var vm = new MigrationWizardViewModel(localizer);

        var result = new OldLauncherDetectionResult
        {
            GamePath = @"C:\YostarGames\BlueArchive_JP",
            ProxyMode = ProxyModes.System,
            CloseBehavior = CloseBehaviors.Exit,
            ClickCodeFound = true,
            LevelDbReadSuccess = true,
            OldUserDataPath = @"C:\Users\Test\AppData\Roaming\BlueArchive_JP_Gamelauncher"
        };

        vm.Load(result);

        Assert.True(vm.GamePathFound);
        Assert.Equal(@"C:\YostarGames\BlueArchive_JP", vm.DetectedGamePath);
        Assert.Equal(1, vm.SelectedProxyModeIndex);  // "system" = index 1
        Assert.Equal(1, vm.SelectedCloseBehaviorIndex);  // "exit" = index 1
        Assert.True(vm.ClickCodeFound);
        Assert.True(vm.LevelDbReadSuccess);
    }

    [Fact]
    public void Load_WithPartialDetection_UsesDefaults()
    {
        var localizer = CreateLocalizer();
        var vm = new MigrationWizardViewModel(localizer);

        var result = new OldLauncherDetectionResult
        {
            GamePath = null,  // no game path found
            ProxyMode = null,  // no proxy found
            CloseBehavior = null,
            ClickCodeFound = false,
            LevelDbReadSuccess = false,
            OldUserDataPath = "/some/path"
        };

        vm.Load(result);

        Assert.False(vm.GamePathFound);
        Assert.Equal("", vm.DetectedGamePath);
        Assert.Equal(0, vm.SelectedProxyModeIndex);  // defaults to direct (index 0)
        Assert.Equal(0, vm.SelectedCloseBehaviorIndex);  // defaults to minimize (index 0)
        Assert.False(vm.ClickCodeFound);
        Assert.False(vm.LevelDbReadSuccess);
    }

    [Fact]
    public void Load_DefaultProxyMode_MapsToDirect()
    {
        var localizer = CreateLocalizer();
        var vm = new MigrationWizardViewModel(localizer);

        var result = new OldLauncherDetectionResult
        {
            ProxyMode = ProxyModes.Direct
        };

        vm.Load(result);

        Assert.Equal(0, vm.SelectedProxyModeIndex);
    }

    [Fact]
    public void Load_DefaultCloseBehavior_MapsToMinimize()
    {
        var localizer = CreateLocalizer();
        var vm = new MigrationWizardViewModel(localizer);

        var result = new OldLauncherDetectionResult
        {
            CloseBehavior = CloseBehaviors.Minimize
        };

        vm.Load(result);

        Assert.Equal(0, vm.SelectedCloseBehaviorIndex);
    }

    [Fact]
    public async Task ApplyMigration_FiresEventWithCorrectSettings()
    {
        var localizer = CreateLocalizer();
        var vm = new MigrationWizardViewModel(localizer);

        var result = new OldLauncherDetectionResult
        {
            GamePath = @"C:\YostarGames\BlueArchive_JP",
            ProxyMode = ProxyModes.System,
            CloseBehavior = CloseBehaviors.Exit,
            ClickCodeFound = false,
            OldUserDataPath = "/tmp"
        };
        vm.Load(result);

        LauncherSettings? receivedSettings = null;
        vm.MigrationApplied += s =>
        {
            receivedSettings = s;
            return Task.CompletedTask;
        };

        await vm.ApplyMigrationCommand.ExecuteAsync(null);

        Assert.NotNull(receivedSettings);
        Assert.Equal(@"C:\YostarGames\BlueArchive_JP", receivedSettings!.GamePath);
        Assert.Equal(ProxyModes.System, receivedSettings.ProxyMode);
        Assert.Equal(CloseBehaviors.Exit, receivedSettings.CloseBehavior);
    }

    [Fact]
    public async Task SkipMigration_FiresSkippedEvent()
    {
        var localizer = CreateLocalizer();
        var vm = new MigrationWizardViewModel(localizer);

        var skipped = false;
        vm.MigrationSkipped += () =>
        {
            skipped = true;
            return Task.CompletedTask;
        };

        await vm.SkipMigrationCommand.ExecuteAsync(null);

        Assert.True(skipped);
    }

    [Fact]
    public void RefreshDisplayNames_PopulatesOptionDisplayNames()
    {
        var localizer = CreateLocalizer();
        var vm = new MigrationWizardViewModel(localizer);

        vm.RefreshDisplayNames();

        Assert.Equal("Direct", vm.ProxyModeOptions[0].DisplayName);
        Assert.Equal("System Proxy", vm.ProxyModeOptions[1].DisplayName);
        Assert.Equal("Minimize to Tray", vm.CloseBehaviorOptions[0].DisplayName);
        Assert.Equal("Exit", vm.CloseBehaviorOptions[1].DisplayName);
    }

    [Fact]
    public void ApplyMigration_WhenAlreadyApplying_Skips()
    {
        var localizer = CreateLocalizer();
        var vm = new MigrationWizardViewModel(localizer);
        vm.Load(new OldLauncherDetectionResult());

        var callCount = 0;
        vm.MigrationApplied += _ =>
        {
            callCount++;
            return Task.CompletedTask;
        };

        // Simulate already applying
        vm.IsApplying = true;

        vm.ApplyMigrationCommand.Execute(null);

        Assert.Equal(0, callCount);
    }

    private static LocalizationService CreateLocalizer()
    {
        return new LocalizationService();
    }
}
