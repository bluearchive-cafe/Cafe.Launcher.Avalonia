using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ServiceConfigurationTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ServiceConfigurationTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public async Task MainWindowViewModel_BackgroundUpdateUsesExplicitSettings()
    {
        var services = new ServiceCollection();
        services.AddLauncherServices();
        await using var provider = services.BuildServiceProvider();
        using var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        var previewSettings = viewModel.Settings.Editor.GetSnapshot();
        previewSettings.BackgroundSource = BackgroundSources.Bundled;
        previewSettings.BackgroundFit = BackgroundFits.Fill;

        await viewModel.Background.UpdateBackgroundImageAsync(
            previewSettings,
            null,
            CancellationToken.None);

        Assert.Equal(global::Avalonia.Media.Stretch.Fill, viewModel.Background.BackgroundStretch);
    }

    [Fact]
    public async Task MainWindowViewModel_RequestRepairOpensItsRepairDialog()
    {
        var services = new ServiceCollection();
        services.AddLauncherServices();
        await using var provider = services.BuildServiceProvider();
        using var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        viewModel.Operations.GetSnapshot = () => new LauncherStatusSnapshot
        {
            IsInstalled = true,
            BelowLowestVersion = false
        };

        await viewModel.Operations.RequestRepairCommand.ExecuteAsync(null);

        Assert.True(viewModel.Dialogs.IsRepairConfirmVisible);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Dialogs.RepairConfirmText));
    }

    [Fact]
    public async Task MainWindowViewModel_ConfirmRepairStartsRepairOperation()
    {
        var services = new ServiceCollection();
        services.AddLauncherServices();
        await using var provider = services.BuildServiceProvider();
        using var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        viewModel.Operations.GetSnapshot = () => new LauncherStatusSnapshot
        {
            IsInstalled = true,
            BelowLowestVersion = false,
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse()
            }
        };
        viewModel.Operations.RequestRefreshAsync = null;
        viewModel.Operations.ApplySnapshotAsync = null;
        viewModel.Shell.IsBusy = false;
        viewModel.Dialogs.ShowRepairConfirm("repair");

        await viewModel.Dialogs.ConfirmRepairCommand.ExecuteAsync(null);

        Assert.False(viewModel.Dialogs.IsRepairConfirmVisible);
        Assert.Equal(
            provider.GetRequiredService<LocalizationService>().T("downloadRemoteConfigIncomplete"),
            viewModel.Shell.OperationNote);
    }

    [Fact]
    public async Task MainWindowViewModel_UsesSharedSingleWindowStateViewModels()
    {
        var services = new ServiceCollection();
        services.AddLauncherServices();
        await using var provider = services.BuildServiceProvider();
        using var viewModel = provider.GetRequiredService<MainWindowViewModel>();

        Assert.Same(provider.GetRequiredService<ShellViewModel>(), viewModel.Shell);
        Assert.Same(provider.GetRequiredService<RemoteContentViewModel>(), viewModel.RemoteContent);
        Assert.Same(provider.GetRequiredService<GameOperationsViewModel>(), viewModel.Operations);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
