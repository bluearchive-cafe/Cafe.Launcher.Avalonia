using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ToastHostViewModelTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    static ToastHostViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public async Task ToastRaised_WhenNotificationsAreEnabled_AddsThenExpiresToast()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
            ToastNotificationsEnabled = true
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var delay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
            (_, cancellationToken) => delay.Task.WaitAsync(cancellationToken));

        toastService.ShowSuccess("saved");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);

        Assert.Equal(ToastSeverity.Success, viewModel.ActiveToasts[0].Severity);
        Assert.NotEmpty(viewModel.ActiveToasts[0].SeverityLabel);

        delay.TrySetResult();
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);
    }

    [Fact]
    public async Task ToastRaised_WhenNotificationsAreDisabled_DoesNotAddToast()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
            ToastNotificationsEnabled = false
        });
        var toastService = provider.GetRequiredService<ToastService>();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
            static (_, _) => Task.CompletedTask);

        toastService.Show("hidden");
        await Task.Delay(20);

        Assert.Empty(viewModel.ActiveToasts);
    }

    [Fact]
    public async Task DismissToastCommand_RemovesMatchingToast()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
            ToastNotificationsEnabled = true
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var delay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
            (_, cancellationToken) => delay.Task.WaitAsync(cancellationToken));
        toastService.ShowWarning("warning");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var id = viewModel.ActiveToasts[0].Id;

        viewModel.DismissToastCommand.Execute(id);

        Assert.Empty(viewModel.ActiveToasts);
        delay.TrySetResult();
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromToastService()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        var toastService = provider.GetRequiredService<ToastService>();
        var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
            static (_, _) => Task.CompletedTask);
        viewModel.Dispose();

        toastService.Show("after-dispose");
        await Task.Delay(20);

        Assert.Empty(viewModel.ActiveToasts);
    }

    private ServiceProvider CreateProvider()
    {
        Directory.CreateDirectory(tempDir);
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        return services.BuildServiceProvider();
    }

    private static Task InvokeImmediately(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("Condition was not reached.");
            }

            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
