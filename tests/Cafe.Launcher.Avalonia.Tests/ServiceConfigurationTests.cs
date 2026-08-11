using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
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
        var services = CreateServices();
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
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        using var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        viewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.Ready
        });

        await viewModel.Operations.RequestRepairCommand.ExecuteAsync(null);

        Assert.True(viewModel.Dialogs.IsRepairConfirmVisible);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Dialogs.RepairConfirmText));
    }

    [Fact]
    public async Task MainWindowViewModel_ConfirmRepairStartsRepairOperation()
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        using var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        viewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        {
            Settings = new LauncherSettings
            {
                GamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "YostarGames", "BlueArchive_JP")
            },
            RuntimeState = LauncherRuntimeState.Ready,
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse()
            }
        });
        viewModel.Shell.IsBusy = false;
        viewModel.Dialogs.ShowRepairConfirm("repair");

        Assert.NotNull(viewModel.ModalHost.Top);
        Assert.Equal(ModalKind.RepairConfirmation, viewModel.ModalHost.Top!.Kind);
        Assert.True(viewModel.ModalHost.HasEntries);

        await viewModel.Dialogs.ConfirmRepairCommand.ExecuteAsync(null);

        Assert.False(viewModel.Dialogs.IsRepairConfirmVisible);
        Assert.Null(viewModel.ModalHost.Top);
    }

    [Fact]
    public async Task MainWindowViewModel_UsesSharedSingleWindowStateViewModels()
    {
        var services = CreateServices();
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

    private ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        return services;
    }
}
