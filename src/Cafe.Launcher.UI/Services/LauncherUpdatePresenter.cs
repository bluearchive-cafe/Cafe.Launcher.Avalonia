using System;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.UI.Services;

/// <summary>
/// The single owner of how an update-check result becomes user-visible output.
/// </summary>
/// <remarks>
/// 启动检查的静默分支是产品契约（失败只进诊断日志，已最新不打扰），不是遗漏：
/// trigger 决定「是否呈现」，不只是措辞。启动 toast 的动作复用本次结果开对话框，
/// 不重新检查——再次请求会让「有更新」的提示与随后的失败或「已最新」互相矛盾。
/// </remarks>
internal sealed class LauncherUpdatePresenter : ILauncherUpdatePresenter
{
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly DialogsViewModel dialogs;
    private readonly ILauncherSelfUpdateService selfUpdateService;
    private readonly LauncherBuildIdentity? buildIdentity;

    public LauncherUpdatePresenter(
        LocalizationService localizer,
        ToastService toastService,
        DialogsViewModel dialogs,
        ILauncherSelfUpdateService selfUpdateService,
        LauncherBuildIdentity? buildIdentity = null)
    {
        this.localizer = localizer;
        this.toastService = toastService;
        this.dialogs = dialogs;
        this.selfUpdateService = selfUpdateService;
        this.buildIdentity = buildIdentity;
    }

    /// <inheritdoc />
    public void Present(LauncherUpdateCheckResult result, LauncherUpdateTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.IsSuccessful)
        {
            if (trigger == LauncherUpdateTrigger.Manual)
            {
                toastService.ShowError(BuildFailureMessage(result));
            }

            return;
        }

        if (!result.IsUpdateAvailable)
        {
            if (trigger == LauncherUpdateTrigger.Manual)
            {
                toastService.ShowSuccess(localizer.F(
                    LocalizationKeys.LauncherUpdateUpToDate,
                    buildIdentity?.LauncherVersion ?? ""));
            }

            return;
        }

        if (trigger == LauncherUpdateTrigger.Manual)
        {
            ShowUpdateDialog(result);
            return;
        }

        toastService.Show(new ToastOptions
        {
            Message = localizer.F(LocalizationKeys.StartupUpdateAvailable, result.LatestVersion),
            Severity = ToastSeverity.Info,
            Duration = ToastDuration.Extended,
            PrimaryAction = new ToastAction(
                localizer.T(LocalizationKeys.LauncherUpdateView),
                _ =>
                {
                    ShowUpdateDialog(result);
                    return Task.FromResult(ToastActionResult.Success());
                },
                Timeout: null)
        });
    }

    private void ShowUpdateDialog(LauncherUpdateCheckResult result) =>
        dialogs.ShowUpdateAvailable(
            result.LatestVersion,
            result.Files,
            selfUpdateService.ResolveInAppAvailability(result.Files),
            result.ReleaseNotes);

    private string BuildFailureMessage(LauncherUpdateCheckResult result)
    {
        var operationMessage = localizer.T(LocalizationKeys.LauncherUpdateCheckFailed);
        if (result.FailureException is not null)
        {
            return ErrorHandlingService.FormatToastMessage(
                operationMessage,
                result.FailureException,
                localizer.T(LocalizationKeys.ErrorNetworkUnavailable),
                localizer.T(LocalizationKeys.ErrorFakeIpDns));
        }

        return string.IsNullOrWhiteSpace(result.FailureMessage)
            ? operationMessage
            : $"{operationMessage}：{result.FailureMessage}";
    }
}
