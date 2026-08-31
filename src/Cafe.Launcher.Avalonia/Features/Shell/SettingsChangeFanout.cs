using System;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// 设置变更扇出子模块:一次保存的设置变更会触发什么 —— 即时表现跟随(动效、远程卡片
/// 可见性)、下载运行中的短路路径、状态刷新,以及补丁源变更后的修复确认 —— 全部决策
/// 集中在一个命名模块,不再散落在壳协调者的方法分支里。
/// </summary>
internal sealed class SettingsChangeFanout
{
    private readonly Func<LauncherSettings> getSavedSettings;
    private readonly Func<string?> getPreviousPatchUrlGroup;
    private readonly Func<LauncherStatusSnapshot?> getCurrentSnapshot;
    private readonly Action<LauncherSettings> applyImmediatePresentation;
    private readonly Func<bool> isDownloadRunning;
    private readonly Func<Task<LauncherSettings>> readSettingsAsync;
    private readonly Func<Task> refreshAsync;
    private readonly Func<string> getRepairPrompt;
    private readonly Action<string> showRepairConfirmation;

    public SettingsChangeFanout(
        Func<LauncherSettings> getSavedSettings,
        Func<string?> getPreviousPatchUrlGroup,
        Func<LauncherStatusSnapshot?> getCurrentSnapshot,
        Action<LauncherSettings> applyImmediatePresentation,
        Func<bool> isDownloadRunning,
        Func<Task<LauncherSettings>> readSettingsAsync,
        Func<Task> refreshAsync,
        Func<string> getRepairPrompt,
        Action<string> showRepairConfirmation)
    {
        this.getSavedSettings = getSavedSettings;
        this.getPreviousPatchUrlGroup = getPreviousPatchUrlGroup;
        this.getCurrentSnapshot = getCurrentSnapshot;
        this.applyImmediatePresentation = applyImmediatePresentation;
        this.isDownloadRunning = isDownloadRunning;
        this.readSettingsAsync = readSettingsAsync;
        this.refreshAsync = refreshAsync;
        this.getRepairPrompt = getRepairPrompt;
        this.showRepairConfirmation = showRepairConfirmation;
    }

    /// <summary>
    /// Applies the follow-throughs of one saved settings snapshot. The patch-url
    /// comparison uses the pre-refresh values: the refresh itself may replace the
    /// snapshot, so the "before" must be captured first.
    /// </summary>
    public async Task ApplySavedChangesAsync()
    {
        var savedSettings = getSavedSettings();
        var previousPatchUrlGroup = getPreviousPatchUrlGroup();
        applyImmediatePresentation(savedSettings);

        if (isDownloadRunning())
        {
            var snapshot = getCurrentSnapshot();
            if (snapshot is not null)
            {
                snapshot.Settings = await readSettingsAsync();
            }

            return;
        }

        await refreshAsync();
        var runtimeState = getCurrentSnapshot()?.RuntimeState;
        if (runtimeState is LauncherRuntimeState.Ready or LauncherRuntimeState.UpdateAvailable
            && !string.Equals(previousPatchUrlGroup, savedSettings.PatchUrlGroup, StringComparison.Ordinal))
        {
            showRepairConfirmation(getRepairPrompt());
        }
    }
}
