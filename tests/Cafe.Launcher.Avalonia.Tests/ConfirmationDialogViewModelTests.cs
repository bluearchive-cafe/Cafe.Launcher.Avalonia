using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ConfirmationDialogViewModelTests
{
    /// <summary>Upper bound for a wait on an event the test itself gates, so a broken command fails instead of hanging.</summary>
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Show_WithMessage_SetsMessageAndShows()
    {
        var dialog = new ConfirmationDialogViewModel("TestConfirmFailed");

        dialog.Show("message");

        Assert.True(dialog.IsVisible);
        Assert.Equal("message", dialog.Message);
    }

    [Fact]
    public void Show_WithoutMessage_PreservesExistingMessage()
    {
        var dialog = new ConfirmationDialogViewModel("TestConfirmFailed");
        dialog.Message = "existing";

        dialog.Show();

        Assert.True(dialog.IsVisible);
        Assert.Equal("existing", dialog.Message);
    }

    [Fact]
    public void ShowCommand_ShowsDialog()
    {
        var dialog = new ConfirmationDialogViewModel("TestConfirmFailed");

        dialog.ShowCommand.Execute(null);

        Assert.True(dialog.IsVisible);
    }

    [Fact]
    public async Task ConfirmCommand_HidesFirstThenInvokesSubscriber()
    {
        var dialog = new ConfirmationDialogViewModel("TestConfirmFailed");
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
        var dialog = new ConfirmationDialogViewModel("TestConfirmFailed");
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
        var dialog = new ConfirmationDialogViewModel("TestConfirmFailed");
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
    public async Task CancelCommand_HidesWithoutRaisingConfirmed()
    {
        var dialog = new ConfirmationDialogViewModel("TestConfirmFailed");
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
}
