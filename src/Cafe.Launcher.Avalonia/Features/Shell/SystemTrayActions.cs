using System;
using System.ComponentModel;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>Aggregates shell presentation for the tray; native adapters do not depend on feature types.</summary>
public sealed class SystemTrayActions : ISystemTrayActions, IDisposable
{
    private readonly MainWindowViewModel viewModel;
    private readonly IGameSessionMonitor sessionMonitor;
    private bool disposed;

    /// <summary>Observes the same state and commands as the main window.</summary>
    public SystemTrayActions(MainWindowViewModel viewModel, IGameSessionMonitor sessionMonitor)
    {
        this.viewModel = viewModel;
        this.sessionMonitor = sessionMonitor;
        viewModel.Shell.PropertyChanged += OnPresentationChanged;
        viewModel.Operations.PropertyChanged += OnPresentationChanged;
        viewModel.ModalHost.PropertyChanged += OnPresentationChanged;
        viewModel.Settings.PropertyChanged += OnPresentationChanged;
        viewModel.Operations.StartGameCommand.CanExecuteChanged += OnCommandChanged;
        sessionMonitor.StateChanged += OnSessionChanged;
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <inheritdoc/>
    public bool IsGameStarting => sessionMonitor.State == GameSessionState.Starting;

    /// <inheritdoc/>
    public bool IsGameRunning => sessionMonitor.State == GameSessionState.Running;

    /// <inheritdoc/>
    public bool CanStartGame => !disposed
        && viewModel.Operations.IsControlPanelVisible
        && !viewModel.Shell.IsBusy
        && !viewModel.Operations.IsDownloadRunning
        && !IsGameStarting && !IsGameRunning
        && viewModel.ModalHost.IsBaseLayerInteractive
        && viewModel.Operations.StartGameCommand.CanExecute(null);

    /// <inheritdoc/>
    public bool CanOpenSettings => !disposed && !viewModel.Settings.IsSaving
        && (viewModel.ModalHost.IsBaseLayerInteractive || viewModel.ModalHost.IsSettingsInteractive);

    /// <inheritdoc/>
    public void StartGame()
    {
        if (CanStartGame)
        {
            viewModel.Operations.StartGameCommand.Execute(null);
        }
    }

    /// <inheritdoc/>
    public void OpenSettings()
    {
        if (CanOpenSettings && !viewModel.WindowChrome.IsSettingsVisible)
        {
            viewModel.WindowChrome.ShowSettingsCommand.Execute(null);
        }
    }

    private void OnPresentationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or ""
            or nameof(ShellViewModel.IsBusy)
            or nameof(GameOperationsViewModel.IsControlPanelVisible)
            or nameof(GameOperationsViewModel.IsDownloadRunning)
            or nameof(ModalHostViewModel.IsBaseLayerInteractive)
            or nameof(ModalHostViewModel.IsSettingsInteractive)
            or nameof(Features.Settings.SettingsViewModel.IsSaving))
        {
            Changed?.Invoke();
        }
    }

    private void OnCommandChanged(object? sender, EventArgs e) => Changed?.Invoke();
    private void OnSessionChanged() => Changed?.Invoke();

    /// <summary>Detaches all subscriptions before the presentation family is disposed by DI.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.Shell.PropertyChanged -= OnPresentationChanged;
        viewModel.Operations.PropertyChanged -= OnPresentationChanged;
        viewModel.ModalHost.PropertyChanged -= OnPresentationChanged;
        viewModel.Settings.PropertyChanged -= OnPresentationChanged;
        viewModel.Operations.StartGameCommand.CanExecuteChanged -= OnCommandChanged;
        sessionMonitor.StateChanged -= OnSessionChanged;
    }
}
