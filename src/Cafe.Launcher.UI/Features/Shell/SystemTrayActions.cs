using System;
using System.ComponentModel;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Features.Games;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;

namespace Cafe.Launcher.UI.Features.Shell;

/// <summary>Aggregates shell presentation for the tray; native adapters do not depend on feature types.</summary>
internal sealed class SystemTrayActions : ISystemTrayActions, IDisposable
{
    private readonly MainWindowViewModel viewModel;
    private readonly GameLibraryViewModel games;
    private ShellViewModel shell;
    private GameOperationsViewModel operations;
    private IGameSessionMonitor sessionMonitor;
    private bool disposed;

    /// <summary>Observes the active game's state and commands as the main window presents them.</summary>
    public SystemTrayActions(MainWindowViewModel viewModel, GameLibraryViewModel games)
    {
        this.viewModel = viewModel;
        this.games = games;
        shell = viewModel.Shell;
        operations = viewModel.Operations;
        sessionMonitor = games.Active.SessionMonitor;
        shell.PropertyChanged += OnPresentationChanged;
        operations.PropertyChanged += OnPresentationChanged;
        viewModel.ModalHost.PropertyChanged += OnPresentationChanged;
        viewModel.Settings.PropertyChanged += OnPresentationChanged;
        operations.StartGameCommand.CanExecuteChanged += OnCommandChanged;
        sessionMonitor.StateChanged += OnSessionChanged;
        games.ActiveChanged += OnActiveGameChanged;
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <inheritdoc/>
    public bool IsGameStarting => sessionMonitor.State == GameSessionState.Starting;

    /// <inheritdoc/>
    public bool IsGameRunning => sessionMonitor.State == GameSessionState.Running;

    /// <inheritdoc/>
    public bool CanStartGame => !disposed
        && operations.IsControlPanelVisible
        && !shell.IsBusy
        && !operations.IsDownloadRunning
        && !IsGameStarting && !IsGameRunning
        && viewModel.ModalHost.IsBaseLayerInteractive
        && operations.StartGameCommand.CanExecute(null);

    /// <inheritdoc/>
    public bool CanExit => !disposed && !viewModel.Operations.IsUninstallExecuting;

    public bool CanOpenSettings => !disposed && !viewModel.Settings.IsSaving
        && (viewModel.ModalHost.IsBaseLayerInteractive || viewModel.ModalHost.IsSettingsInteractive);

    /// <inheritdoc/>
    public void StartGame()
    {
        if (CanStartGame)
        {
            operations.StartGameCommand.Execute(null);
        }
    }

    /// <inheritdoc/>
    public void OpenSettings()
    {
        if (CanOpenSettings && !viewModel.WindowChrome.IsSettingsVisible)
        {
            viewModel.WindowChrome.ToggleSettingsCommand.Execute(null);
        }
    }

    private void OnPresentationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or ""
            or nameof(ShellViewModel.IsBusy)
            or nameof(GameOperationsViewModel.IsControlPanelVisible)
            or nameof(GameOperationsViewModel.IsDownloadRunning)
            or nameof(GameOperationsViewModel.IsUninstallExecuting)
            or nameof(ModalHostViewModel.IsBaseLayerInteractive)
            or nameof(ModalHostViewModel.IsSettingsInteractive)
            or nameof(Features.Settings.SettingsViewModel.IsSaving))
        {
            Changed?.Invoke();
        }
    }

    private void OnCommandChanged(object? sender, EventArgs e) => Changed?.Invoke();
    private void OnSessionChanged() => Changed?.Invoke();

    /// <summary>
    /// Re-binds the tray to the newly displayed game. The window swaps its shell,
    /// operations and session monitor instances on selection; keeping the initial
    /// Blue Archive references would report the wrong game's running state.
    /// </summary>
    private void OnActiveGameChanged()
    {
        if (disposed)
        {
            return;
        }

        shell.PropertyChanged -= OnPresentationChanged;
        operations.PropertyChanged -= OnPresentationChanged;
        operations.StartGameCommand.CanExecuteChanged -= OnCommandChanged;
        sessionMonitor.StateChanged -= OnSessionChanged;

        shell = viewModel.Shell;
        operations = viewModel.Operations;
        sessionMonitor = games.Active.SessionMonitor;

        shell.PropertyChanged += OnPresentationChanged;
        operations.PropertyChanged += OnPresentationChanged;
        operations.StartGameCommand.CanExecuteChanged += OnCommandChanged;
        sessionMonitor.StateChanged += OnSessionChanged;
        Changed?.Invoke();
    }

    /// <summary>Detaches all subscriptions before the presentation family is disposed by DI.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        games.ActiveChanged -= OnActiveGameChanged;
        shell.PropertyChanged -= OnPresentationChanged;
        operations.PropertyChanged -= OnPresentationChanged;
        viewModel.ModalHost.PropertyChanged -= OnPresentationChanged;
        viewModel.Settings.PropertyChanged -= OnPresentationChanged;
        operations.StartGameCommand.CanExecuteChanged -= OnCommandChanged;
        sessionMonitor.StateChanged -= OnSessionChanged;
    }
}
