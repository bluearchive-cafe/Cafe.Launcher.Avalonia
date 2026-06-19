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
        var vm = CreateViewModel();

        Assert.Equal(2, vm.Options.ProxyMode.Count);
        Assert.Equal(ProxyModes.Direct, vm.Options.ProxyMode[0].Code);
        Assert.Equal(ProxyModes.System, vm.Options.ProxyMode[1].Code);

        Assert.Equal(2, vm.Options.CloseBehavior.Count);
        Assert.Equal(CloseBehaviors.Minimize, vm.Options.CloseBehavior[0].Code);
        Assert.Equal(CloseBehaviors.Exit, vm.Options.CloseBehavior[1].Code);

        Assert.False(vm.IsVisible);
        Assert.False(vm.IsApplying);
        Assert.Equal("", vm.Editor.Current.GamePath);
    }

    [Fact]
    public void Load_WithFullDetection_PopulatesAllFields()
    {
        var vm = CreateViewModel();

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

        Assert.Equal(
            @"C:\YostarGames\BlueArchive_JP",
            vm.Editor.Current.GamePath);
        Assert.Equal(ProxyModes.System, vm.Editor.Current.ProxyMode);
        Assert.Equal(CloseBehaviors.Exit, vm.Editor.Current.CloseBehavior);
        Assert.True(vm.ClickCodeFound);
        Assert.True(vm.LevelDbReadSuccess);
    }

    [Fact]
    public void Load_WithPartialDetection_UsesDefaults()
    {
        var vm = CreateViewModel();

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

        Assert.Equal("", vm.Editor.Current.GamePath);
        Assert.Equal(ProxyModes.Direct, vm.Editor.Current.ProxyMode);
        Assert.Equal(CloseBehaviors.Minimize, vm.Editor.Current.CloseBehavior);
        Assert.False(vm.ClickCodeFound);
        Assert.False(vm.LevelDbReadSuccess);
    }

    [Fact]
    public void Load_DefaultProxyMode_MapsToDirect()
    {
        var vm = CreateViewModel();

        var result = new OldLauncherDetectionResult
        {
            ProxyMode = ProxyModes.Direct
        };

        vm.Load(result);

        Assert.Equal(ProxyModes.Direct, vm.Editor.Current.ProxyMode);
    }

    [Fact]
    public void Load_DefaultCloseBehavior_MapsToMinimize()
    {
        var vm = CreateViewModel();

        var result = new OldLauncherDetectionResult
        {
            CloseBehavior = CloseBehaviors.Minimize
        };

        vm.Load(result);

        Assert.Equal(CloseBehaviors.Minimize, vm.Editor.Current.CloseBehavior);
    }

    [Fact]
    public async Task ApplyMigration_FiresEventWithCorrectSettings()
    {
        var vm = CreateViewModel();

        var result = new OldLauncherDetectionResult
        {
            GamePath = @"C:\YostarGames\BlueArchive_JP",
            ProxyMode = ProxyModes.System,
            CloseBehavior = CloseBehaviors.Exit,
            ClickCodeFound = false,
            OldUserDataPath = "/tmp"
        };
        vm.Load(result);
        vm.Editor.Current.ProxyMode = ProxyModes.Direct;
        vm.Editor.Current.CloseBehavior = CloseBehaviors.Minimize;

        LauncherSettings? receivedSettings = null;
        vm.MigrationApplied += s =>
        {
            receivedSettings = s;
            return Task.CompletedTask;
        };

        await vm.ApplyMigrationCommand.ExecuteAsync(null);

        Assert.NotNull(receivedSettings);
        Assert.Equal(@"C:\YostarGames\BlueArchive_JP", receivedSettings!.GamePath);
        Assert.Equal(ProxyModes.Direct, receivedSettings.ProxyMode);
        Assert.Equal(CloseBehaviors.Minimize, receivedSettings.CloseBehavior);
    }

    [Fact]
    public async Task SkipMigration_FiresSkippedEvent()
    {
        var vm = CreateViewModel();

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
        var vm = CreateViewModel();

        vm.RefreshDisplayNames();

        Assert.Equal("Direct", vm.Options.ProxyMode[0].DisplayName);
        Assert.Equal("System Proxy", vm.Options.ProxyMode[1].DisplayName);
        Assert.Equal("Minimize to Tray", vm.Options.CloseBehavior[0].DisplayName);
        Assert.Equal("Exit", vm.Options.CloseBehavior[1].DisplayName);
    }

    [Fact]
    public void ApplyMigration_WhenAlreadyApplying_Skips()
    {
        var vm = CreateViewModel();
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

    private static MigrationWizardViewModel CreateViewModel()
    {
        var localizer = CreateLocalizer();
        return new MigrationWizardViewModel(
            new SettingsEditor(),
            new SettingsOptionsViewModel(localizer, new DiskSpaceService()));
    }
}
