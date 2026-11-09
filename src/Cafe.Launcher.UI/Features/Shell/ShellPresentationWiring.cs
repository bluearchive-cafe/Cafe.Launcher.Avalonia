using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;

namespace Cafe.Launcher.UI.Features.Shell;

/// <summary>
/// 跨功能接线的唯一归属：呈现族之间的订阅、启动模块接线与模态注册收进一张表，
/// 由一对 <see cref="Wire"/>/<see cref="Unwire"/> 按登记逆序装卸。
/// 需要壳状态或写入方的动作经 <see cref="ShellWiringActions"/> 传入，避免接线模块
/// 反向依赖 <see cref="ShellLifecycle"/>；生命周期动词仍留在壳协调者。
/// </summary>
internal sealed class ShellPresentationWiring : IDisposable
{
    private readonly ShellPresentationFamily family;
    private readonly ISavedSettingsFollowThrough followThrough;
    private readonly ShellStartup startup;
    private readonly LocalizationService localizer;
    private readonly ShellWiringActions actions;
    private readonly ModalRegistrar modalRegistrar;
    private readonly IReadOnlyList<ModalRegistration> modalRegistrations;

    /// <summary>
    /// 退订记录表：每条接线经 <see cref="Attach"/> 配对登记，Unwire 逆序执行后清空——
    /// 订阅与退订只维护这一份清单（R2-c07／D15／AUD-ARCH-005）。
    /// </summary>
    private readonly List<Action> detachers = [];
    private bool isWired;

    public ShellPresentationWiring(
        ShellPresentationFamily family,
        ISavedSettingsFollowThrough followThrough,
        ShellStartup startup,
        LocalizationService localizer,
        ShellWiringActions actions)
    {
        this.family = family;
        this.followThrough = followThrough;
        this.startup = startup;
        this.localizer = localizer;
        this.actions = actions;
        modalRegistrar = new ModalRegistrar(family.ModalHost);
        modalRegistrations = ShellModalCatalog.Create(family);
    }

    /// <summary>
    /// Subscribes cross-feature events once for the active shell lifecycle.
    /// 登记顺序即 <see cref="Unwire"/> 的逆序拆卸顺序。
    /// </summary>
    public void Wire()
    {
        if (isWired) return;
        isWired = true;

        var settings = family.Settings;
        var dialogs = family.Dialogs;
        var operations = family.Operations;
        var debug = family.Debug;

        Attach(
            () => settings.Appearance.GetBackgroundBitmapAsync = family.Background.GetBackgroundBitmapAsync,
            () => settings.Appearance.GetBackgroundBitmapAsync = null);
        Attach(
            () => settings.PreviewAppearanceAsync = actions.PreviewAppearanceAsync,
            () => settings.PreviewAppearanceAsync = null);
        Attach(
            () => settings.SavedSettingsFollowThrough = followThrough,
            () => settings.SavedSettingsFollowThrough = null);

        Attach(
            () => family.ResourcePanel.ResourcePanelSourceConfirmRequested += ShowResourcePanelSourceConfirmDialog,
            () => family.ResourcePanel.ResourcePanelSourceConfirmRequested -= ShowResourcePanelSourceConfirmDialog);
        Attach(
            () => dialogs.ResourcePanelSourceConfirm.Confirmed += actions.SwitchResourcePanelSourceAsync,
            () => dialogs.ResourcePanelSourceConfirm.Confirmed -= actions.SwitchResourcePanelSourceAsync);

        Attach(
            () => operations.PickInstallPathAsync = settings.PickInstallationDraftAsync,
            () => operations.PickInstallPathAsync = null);
        Attach(
            () => operations.CommitInstallPathAsync = settings.CommitInstallationPathAsync,
            () => operations.CommitInstallPathAsync = null);
        Attach(
            () => operations.RefreshRequested += actions.RefreshForOperationsAsync,
            () => operations.RefreshRequested -= actions.RefreshForOperationsAsync);
        Attach(
            () => operations.OpenLogViewerRequested += OpenLogViewerAsync,
            () => operations.OpenLogViewerRequested -= OpenLogViewerAsync);
        Attach(
            () => operations.TaskSurfaceRequested += RevealTaskSurface,
            () => operations.TaskSurfaceRequested -= RevealTaskSurface);

        Attach(
            () => dialogs.DownloadRunningCloseConfirm.Confirmed += family.WindowChrome.CloseAfterStoppingDownload,
            () => dialogs.DownloadRunningCloseConfirm.Confirmed -= family.WindowChrome.CloseAfterStoppingDownload);
        Attach(
            () => dialogs.CloseRequested += family.WindowChrome.RequestClose,
            () => dialogs.CloseRequested -= family.WindowChrome.RequestClose);
        Attach(
            () => dialogs.ConfirmUpdateAvailableRequested += OnUpdateAvailableConfirmed,
            () => dialogs.ConfirmUpdateAvailableRequested -= OnUpdateAvailableConfirmed);
        Attach(
            () => dialogs.SelfUpdateStartRequested += actions.BeginSelfUpdate,
            () => dialogs.SelfUpdateStartRequested -= actions.BeginSelfUpdate);
        Attach(
            () => dialogs.ApplyUpdateRequested += actions.ApplyPendingUpdate,
            () => dialogs.ApplyUpdateRequested -= actions.ApplyPendingUpdate);
        Attach(
            () => dialogs.CancelUpdateRequested += actions.CancelUpdatePreparation,
            () => dialogs.CancelUpdateRequested -= actions.CancelUpdatePreparation);
        Attach(
            () => dialogs.ErrorViewLogRequested += OpenLogViewer,
            () => dialogs.ErrorViewLogRequested -= OpenLogViewer);

        Attach(
            () => debug.RefreshRequested += actions.RefreshFromDebugAsync,
            () => debug.RefreshRequested -= actions.RefreshFromDebugAsync);
        Attach(
            () => debug.ResetSettingsRequested += actions.ResetSettingsToDefaultsAsync,
            () => debug.ResetSettingsRequested -= actions.ResetSettingsToDefaultsAsync);
        Attach(
            () => debug.ResetSettingsConfirmationRequested += dialogs.DebugResetConfirm.Show,
            () => debug.ResetSettingsConfirmationRequested -= dialogs.DebugResetConfirm.Show);
        Attach(
            () => dialogs.DebugResetConfirm.Confirmed += debug.ConfirmResetSettingsAsync,
            () => dialogs.DebugResetConfirm.Confirmed -= debug.ConfirmResetSettingsAsync);
        Attach(
            () => dialogs.SettingsResetConfirm.Confirmed += actions.ResetSettingsFromSettingsPageAsync,
            () => dialogs.SettingsResetConfirm.Confirmed -= actions.ResetSettingsFromSettingsPageAsync);

        Attach(
            () => family.RemoteContent.OpenExternalUrlRequested = family.WindowChrome.OpenExternalUrl,
            () => family.RemoteContent.OpenExternalUrlRequested = null);

        startup.Wire();

        Attach(
            () => settings.Editor.CurrentPropertyChanged += OnSettingPropertyChanged,
            () => settings.Editor.CurrentPropertyChanged -= OnSettingPropertyChanged);

        foreach (var registration in modalRegistrations)
        {
            modalRegistrar.Register(registration);
        }
    }

    /// <summary>Records the teardown half of one wiring so Unwire cannot miss it.</summary>
    private void Attach(Action attach, Action detach)
    {
        attach();
        detachers.Add(detach);
    }

    /// <summary>
    /// Removes the subscriptions established by <see cref="Wire"/>. 按登记的逆序执行退订；
    /// 模态注册与 startup 接线不在记录表里，按同一逆序原则手工排在首尾。
    /// </summary>
    public void Unwire()
    {
        if (!isWired) return;
        isWired = false;

        modalRegistrar.Dispose();
        for (var i = detachers.Count - 1; i >= 0; i--)
        {
            detachers[i]();
        }

        detachers.Clear();
        startup.Unwire();
    }

    /// <summary>Handles Escape for the active modal and returns whether a modal consumed it.</summary>
    public bool TryDispatchEscape()
    {
        var top = family.ModalHost.Top;
        return top is not null && modalRegistrar.TryDispatchEscape(top.Kind);
    }

    public void Dispose() => Unwire();

    private void ShowResourcePanelSourceConfirmDialog() =>
        family.Dialogs.ResourcePanelSourceConfirm.Show(localizer.T(LocalizationKeys.ResourcePanelCafeOnlyMessage));

    private void RevealTaskSurface()
    {
        // Navigation closes ordinary overlays through their own commands. A draft confirmation,
        // executing uninstall or other decision remains in place until the user handles it.
        while (family.ModalHost.Top is { } top && top.Kind is ModalKind.Settings or ModalKind.ResourcePanel
            or ModalKind.LogViewer or ModalKind.LogExport or ModalKind.Debug or ModalKind.DesignGallery)
        {
            if (!modalRegistrar.TryDispatchEscape(top.Kind) || ReferenceEquals(top, family.ModalHost.Top)) break;
        }
    }

    private Task OpenLogViewerAsync() => family.LogViewer.OpenCommand.ExecuteAsync(null);

    private void OpenLogViewer() => family.LogViewer.OpenCommand.Execute(null);

    private void OnUpdateAvailableConfirmed(string url) => family.WindowChrome.OpenExternalUrl(url);

    private void OnSettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherSettings.StatusDetailMode))
        {
            actions.StatusDetailModeChanged();
        }
    }
}
