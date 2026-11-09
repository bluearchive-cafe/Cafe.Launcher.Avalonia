using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.UI.Services;

/// <summary>
/// Platform effects used by presentation orchestration. Tests replace these effects rather
/// than replacing ViewModel registrations, so production constructor wiring stays exercised.
/// </summary>
internal sealed record PresentationEnvironment
{
    public Func<Action, Task> InvokeOnUiAsync { get; init; } =
        async action => await Dispatcher.UIThread.InvokeAsync(action);

    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;

    public Func<TimeSpan, Task> DelayBeforeWindowActionAsync { get; init; } = delay => Task.Delay(delay);

    public Action<string?> OpenExternalUrl { get; init; } = ExternalLinkService.Open;

    public Action<string> OpenDirectory { get; init; } =
        static path => ShellFolderOpener.OpenInFileManager(path);
}
