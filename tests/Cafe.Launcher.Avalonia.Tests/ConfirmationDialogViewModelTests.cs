using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ConfirmationDialogViewModelTests
{
    /// <summary>Upper bound for a wait on an event the test itself gates, so a broken command fails instead of hanging.</summary>
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Show_WithMessage_SetsMessageAndShows()
    {
        var dialog = CreateDialog();

        dialog.Show("message");

        Assert.True(dialog.IsVisible);
        Assert.Equal("message", dialog.Message);
    }

    [Fact]
    public void Show_WithoutMessage_PreservesExistingMessage()
    {
        var dialog = CreateDialog();
        dialog.Message = "existing";

        dialog.Show();

        Assert.True(dialog.IsVisible);
        Assert.Equal("existing", dialog.Message);
    }

    [Fact]
    public void ShowCommand_WhenExecuted_ShowsDialog()
    {
        var dialog = CreateDialog();

        dialog.ShowCommand.Execute(null);

        Assert.True(dialog.IsVisible);
    }

    [Fact]
    public async Task ConfirmCommand_WhenExecuted_HidesFirstThenInvokesSubscriber()
    {
        var dialog = CreateDialog();
        var visibleAtSubscriber = true;
        dialog.Confirmed += () =>
        {
            visibleAtSubscriber = dialog.IsVisible;
            return Task.CompletedTask;
        };
        dialog.Show("message");

        await dialog.ConfirmCommand.ExecuteAsync(null);

        Assert.False(visibleAtSubscriber);
        Assert.False(dialog.IsVisible);
    }

    [Fact]
    public async Task ConfirmCommand_WithMultipleAsyncSubscribers_AwaitsEverySubscriberInOrder()
    {
        var dialog = CreateDialog();
        var firstInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Confirmed += async () =>
        {
            firstInvoked.SetResult();
            await firstRelease.Task;
        };
        dialog.Confirmed += () =>
        {
            secondInvoked.SetResult();
            return Task.CompletedTask;
        };
        dialog.Show();

        var confirmTask = dialog.ConfirmCommand.ExecuteAsync(null);
        await firstInvoked.Task.WaitAsync(GateTimeout);

        Assert.False(confirmTask.IsCompleted);
        Assert.False(secondInvoked.Task.IsCompleted);
        firstRelease.SetResult();
        await secondInvoked.Task.WaitAsync(GateTimeout);
        await confirmTask.WaitAsync(GateTimeout);
    }

    [Fact]
    public async Task ConfirmCommand_WhenSubscriberThrows_HidesDialogAndSkipsRemainingSubscribers()
    {
        var dialog = CreateDialog();
        var secondInvoked = false;
        dialog.Confirmed += () => throw new InvalidOperationException("subscriber broke");
        dialog.Confirmed += () =>
        {
            secondInvoked = true;
            return Task.CompletedTask;
        };
        dialog.Show();

        await dialog.ConfirmCommand.ExecuteAsync(null);

        Assert.False(dialog.IsVisible);
        Assert.False(secondInvoked);
    }

    [Fact]
    public async Task ConfirmCommand_WhenSubscriberThrows_UsesInjectedDiagnosticsAndStableModuleTag()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logger = new UnifiedLogger(tempDir);
        try
        {
            var dialog = new ConfirmationDialogViewModel(
                new LocalDiagnostics(logger),
                "TestOperation");
            dialog.Confirmed += () => throw new InvalidOperationException("subscriber broke");

            await dialog.ConfirmCommand.ExecuteAsync(null);
            logger.Dispose();

            var text = File.ReadAllText(logger.LogFilePath);
            Assert.Contains("[ConfirmationDialog]", text, StringComparison.Ordinal);
            Assert.Contains("TestOperation confirmation handler failed.", text, StringComparison.Ordinal);
            Assert.Contains("subscriber broke", text, StringComparison.Ordinal);
        }
        finally
        {
            logger.Dispose();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CancelCommand_WhenExecuted_HidesWithoutRaisingConfirmed()
    {
        var dialog = CreateDialog();
        var confirmed = false;
        dialog.Confirmed += () =>
        {
            confirmed = true;
            return Task.CompletedTask;
        };
        dialog.Show();

        dialog.CancelCommand.Execute(null);

        Assert.False(dialog.IsVisible);
        Assert.False(confirmed);
    }

    private static ConfirmationDialogViewModel CreateDialog() =>
        new(new LocalDiagnostics(), "Test");
}
