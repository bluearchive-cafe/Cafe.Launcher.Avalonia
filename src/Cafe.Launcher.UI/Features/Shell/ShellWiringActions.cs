using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.UI.Features.GameOperations;

namespace Cafe.Launcher.UI.Features.Shell;

/// <summary>
/// 接线表需要触达的壳动作。呈现族之间的直接订阅留在
/// <see cref="ShellPresentationWiring"/>；需要壳状态、写入方或停机编排的动作经此聚合传入，
/// 让接线模块不反向依赖 <see cref="ShellLifecycle"/> 的具体类型。
/// </summary>
internal sealed record ShellWiringActions(
    Func<LauncherSettings, string?, CancellationToken, Task> PreviewAppearanceAsync,
    Func<GameOperationsRefreshMode, Task> RefreshForOperationsAsync,
    Func<Task> RefreshFromDebugAsync,
    Func<Task> SwitchResourcePanelSourceAsync,
    Func<Task> ResetSettingsToDefaultsAsync,
    Func<Task> ResetSettingsFromSettingsPageAsync,
    Action StatusDetailModeChanged,
    Action<string, IReadOnlyList<ReleaseFile>> BeginSelfUpdate,
    Action ApplyPendingUpdate,
    Action CancelUpdatePreparation);
