using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// Presentation properties the ShellLifecycle writes to.
/// </summary>
public interface IShellLifecyclePresentation
{
    bool IsBusy { get; set; }
    bool IsMotionReduced { get; set; }
}
