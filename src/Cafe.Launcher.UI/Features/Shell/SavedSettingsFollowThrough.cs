using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Features.Games;
using Cafe.Launcher.UI.Features.ResourcePanel;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;

namespace Cafe.Launcher.UI.Features.Shell;

/// <summary>
/// 已保存设置派生效果的唯一归属（ADR-052）：语言、主题、日志级别、动效、远端可见性
/// 与资源面板先就地生效；随后要么刷新宿主状态，要么在下载进行中只校正待用快照，
/// 并在下载源变化且游戏可运行时提示修复。
/// 调用方只决定「保存成功后显示什么」——toast、隐藏向导、打开面板。
/// </summary>
internal sealed class SavedSettingsFollowThrough : ISavedSettingsFollowThrough
{
    private readonly SettingsViewModel settings;
    private readonly ResourcePanelViewModel resourcePanel;
    private readonly RemoteContentViewModel defaultRemoteContent;
    private readonly DialogsViewModel dialogs;
    private readonly GameOperationsViewModel defaultOperations;
    private readonly LocalizationService localizer;
    private readonly Action<string> applyLanguage;
    private readonly Action<LauncherSettings> applyMotionSettings;
    private readonly Func<LauncherStatusSnapshot?> getCurrentSnapshot;
    private readonly Action<LauncherSettings> updateSnapshotSettings;
    private readonly Func<CancellationToken, Task> refreshAsync;
    private readonly GameLibraryViewModel? games;
    private readonly Func<LauncherSettings, LauncherStatusSnapshot?, Task>? updateBackground;

    private GameOperationsViewModel operations => games?.Active.Operations ?? defaultOperations;

    public SavedSettingsFollowThrough(
        SettingsViewModel settings,
        ResourcePanelViewModel resourcePanel,
        RemoteContentViewModel remoteContent,
        DialogsViewModel dialogs,
        GameOperationsViewModel operations,
        LocalizationService localizer,
        Action<string> applyLanguage,
        Action<LauncherSettings> applyMotionSettings,
        Func<LauncherStatusSnapshot?> getCurrentSnapshot,
        Action<LauncherSettings> updateSnapshotSettings,
        Func<CancellationToken, Task> refreshAsync,
        GameLibraryViewModel? games = null,
        Func<LauncherSettings, LauncherStatusSnapshot?, Task>? updateBackground = null)
    {
        this.settings = settings;
        this.resourcePanel = resourcePanel;
        defaultRemoteContent = remoteContent;
        this.dialogs = dialogs;
        defaultOperations = operations;
        this.localizer = localizer;
        this.applyLanguage = applyLanguage;
        this.applyMotionSettings = applyMotionSettings;
        this.getCurrentSnapshot = getCurrentSnapshot;
        this.updateSnapshotSettings = updateSnapshotSettings;
        this.refreshAsync = refreshAsync;
        this.games = games;
        this.updateBackground = updateBackground;
    }

    public SavedSettingsHandoff Apply(LauncherSettings savedSettings)
    {
        ArgumentNullException.ThrowIfNull(savedSettings);

        // 下载源的旧值必须在刷新替换快照之前取到，否则修复提示永远不成立。
        var previousPatchUrlGroup = getCurrentSnapshot()?.GameSettings.PatchUrlGroup;

        settings.ApplyLogLevel(savedSettings.LogLevel);
        applyLanguage(savedSettings.Language);
        settings.Appearance.ApplyFrom(savedSettings);
        resourcePanel.ApplySettings(savedSettings);
        if (games is null)
        {
            defaultRemoteContent.UpdateRemoteContentVisibility(savedSettings.ShowRemoteContentCard);
        }
        else
        {
            foreach (var entry in games.Entries)
            {
                entry.Context.RemoteContent.UpdateRemoteContentVisibility(savedSettings.ShowRemoteContentCard);
            }
        }
        applyMotionSettings(savedSettings);

        return new SavedSettingsHandoff(savedSettings, previousPatchUrlGroup);
    }

    public async Task CompleteAsync(SavedSettingsHandoff handoff)
    {
        var savedSettings = handoff.SavedSettings;
        var activeOperations = operations;
        var snapshot = getCurrentSnapshot();
        if (updateBackground is not null)
        {
            await updateBackground(savedSettings, snapshot);
        }

        if (activeOperations.IsTaskExecuting || activeOperations.IsDownloadRunning || activeOperations.IsUninstallExecuting)
        {
            // 整份刷新会打断下载；这条路径只把待用快照换成刚落盘的值。
            updateSnapshotSettings(savedSettings.DeepClone());
            return;
        }

        if (settings.IsGameSettingsEditing && snapshot is not null
            && snapshot.GameSettings.HasSameOperationSettingsState(
                savedSettings.GetGameSettings(activeOperations.FixedGameId)))
        {
            // 只改了与宿主状态无关的字段：就地替换快照，免去整份刷新。
            snapshot.Settings = savedSettings.DeepClone();
            snapshot.GameSettings = savedSettings.GetGameSettings(activeOperations.FixedGameId).DeepClone();
            return;
        }

        await refreshAsync(CancellationToken.None);

        var runtimeState = getCurrentSnapshot()?.RuntimeState;
        if (handoff.PreviousPatchUrlGroup is { } previousPatchUrlGroup
            && runtimeState is LauncherRuntimeState.Ready or LauncherRuntimeState.UpdateAvailable
            && !string.Equals(previousPatchUrlGroup,
                savedSettings.GetGameSettings(activeOperations.FixedGameId).PatchUrlGroup,
                StringComparison.Ordinal))
        {
            dialogs.RepairConfirm.Show(localizer.T(LocalizationKeys.DownloadSourceChangedRepairPrompt));
        }
    }
}
