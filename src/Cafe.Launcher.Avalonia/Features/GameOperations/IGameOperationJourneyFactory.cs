using System;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>Creates game-operation journeys at the presentation seam.</summary>
internal interface IGameOperationJourneyFactory
{
    IGameOperationJourney Create(IGameOperationJourneyHost host);
}

/// <summary>Production adapter that hides journey assembly from the presentation module.</summary>
internal sealed class GameOperationJourneyFactory : IGameOperationJourneyFactory
{
    private readonly IGameLaunchWorkflow launchWorkflow;
    private readonly IGameInstallationWorkflow installationWorkflow;
    private readonly IGameUninstallWorkflow uninstallWorkflow;
    private readonly IGameShortcutService shortcutService;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly LocalDiagnostics diagnostics;
    private readonly ShellViewModel shell;
    private readonly DialogsViewModel dialogs;
    private readonly IErrorHandlingService errorHandling;
    private readonly Func<TimeSpan, Task> delayAsync;

    public GameOperationJourneyFactory(
        IGameLaunchWorkflow launchWorkflow,
        IGameInstallationWorkflow installationWorkflow,
        IGameUninstallWorkflow uninstallWorkflow,
        IGameShortcutService shortcutService,
        LocalizationService localizer,
        ToastService toastService,
        LocalDiagnostics diagnostics,
        ShellViewModel shell,
        DialogsViewModel dialogs,
        IErrorHandlingService errorHandling,
        Func<TimeSpan, Task>? delayAsync = null)
    {
        this.launchWorkflow = launchWorkflow;
        this.installationWorkflow = installationWorkflow;
        this.uninstallWorkflow = uninstallWorkflow;
        this.shortcutService = shortcutService;
        this.localizer = localizer;
        this.toastService = toastService;
        this.diagnostics = diagnostics;
        this.shell = shell;
        this.dialogs = dialogs;
        this.errorHandling = errorHandling;
        this.delayAsync = delayAsync ?? Task.Delay;
    }

    public IGameOperationJourney Create(IGameOperationJourneyHost host) =>
        new GameOperationJourney(
            launchWorkflow,
            installationWorkflow,
            uninstallWorkflow,
            shortcutService,
            localizer,
            toastService,
            diagnostics,
            shell,
            dialogs,
            errorHandling,
            delayAsync,
            host);
}
