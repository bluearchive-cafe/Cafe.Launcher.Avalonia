using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void DownloadRunningChanged_FromWorkerThread_NotifiesOnUiThread()
    {
        var executor = new ThreadAwareGameOperationExecutor();
        using var context = CreateContext(executor);
        bool? notificationHasUiAccess = null;
        context.ViewModel.Operations.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GameOperationsViewModel.IsDownloadRunning))
            {
                notificationHasUiAccess = Dispatcher.UIThread.CheckAccess();
            }
        };

        var worker = Task.Run(() => executor.SetDownloadRunning(true));
        Assert.True(worker.Wait(TimeSpan.FromSeconds(5)));

        Assert.Null(notificationHasUiAccess);
        Dispatcher.UIThread.RunJobs();
        Assert.True(notificationHasUiAccess);
        Assert.True(context.ViewModel.Operations.IsDownloadRunning);
    }

    [AvaloniaFact]
    public void DownloadRunningChanged_WhenNewerStateArrives_DropsStaleWorkerNotification()
    {
        var executor = new ThreadAwareGameOperationExecutor();
        using var context = CreateContext(executor);
        using var workerRaisedNotification = new ManualResetEventSlim();
        using var completeWorker = new ManualResetEventSlim();
        var notificationCount = 0;
        context.ViewModel.Operations.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GameOperationsViewModel.IsDownloadRunning))
            {
                notificationCount++;
            }
        };

        var worker = Task.Run(() =>
        {
            executor.SetDownloadRunning(true);
            workerRaisedNotification.Set();
            completeWorker.Wait();
        });
        Assert.True(workerRaisedNotification.Wait(TimeSpan.FromSeconds(5)));
        executor.SetDownloadRunning(false);
        completeWorker.Set();
        Assert.True(worker.Wait(TimeSpan.FromSeconds(5)));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, notificationCount);
        Assert.False(context.ViewModel.Operations.IsDownloadRunning);
    }

    [AvaloniaFact]
    public void DownloadRunningChanged_AfterOperationsDisposed_IgnoresStaleJourneyCallback()
    {
        var executor = new ThreadAwareGameOperationExecutor();
        using var context = CreateContext(executor);
        var notificationCount = 0;
        context.ViewModel.Operations.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GameOperationsViewModel.IsDownloadRunning))
            {
                notificationCount++;
            }
        };

        context.ViewModel.Operations.Dispose();
        context.ViewModel.Operations.Dispose();
        executor.RaiseStaleRunningChanged();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, notificationCount);
    }

    [AvaloniaFact]
    public void ApplyProgress_FromWorkerThread_QueuesUiUpdate()
    {
        using var context = CreateContext();
        var worker = Task.Run(() => context.ViewModel.Operations.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading,
            Progress = 50
        }));
        Assert.True(worker.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(context.ViewModel.Operations.IsProgressPanelVisible);

        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Operations.IsProgressPanelVisible);
        Assert.Equal(50, context.ViewModel.Operations.ProgressValue);
    }

    private sealed class ThreadAwareGameOperationExecutor : IGameOperationExecutor
    {
        private bool isDownloadRunning;
        private Action? isRunningChanged;
        private Action? staleIsRunningChanged;

        public bool IsDownloadRunning => isDownloadRunning;
        public bool IsPaused => false;

        public event Action? IsRunningChanged
        {
            add
            {
                isRunningChanged += value;
                staleIsRunningChanged = value;
            }
            remove => isRunningChanged -= value;
        }

        public void SetDownloadRunning(bool value)
        {
            isDownloadRunning = value;
            isRunningChanged?.Invoke();
        }

        public void RaiseStaleRunningChanged() => staleIsRunningChanged?.Invoke();

        public Task<GameLaunchResult> LaunchAsync(
            LauncherStatusSnapshot snapshot,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<GameOperationResult> InstallOrUpdateAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<GameOperationResult> RepairAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress) =>
            throw new NotSupportedException();
        public Task<GameOperationResult?> ResumePersistedAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<GameOperationResult> ValidateUninstallAsync(string gamePath) =>
            throw new NotSupportedException();
        public Task<GameOperationResult> UninstallAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress) =>
            throw new NotSupportedException();

        public void Stop(bool clearPersistedState)
        {
        }

        public void Pause()
        {
        }

        public void Resume()
        {
        }
    }
}
