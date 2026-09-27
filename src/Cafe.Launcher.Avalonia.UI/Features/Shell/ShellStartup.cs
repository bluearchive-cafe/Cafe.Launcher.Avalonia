using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// 启动编排子模块:持有开始动作的规则 —— 一次性初始化、首启动动效偏好、初始语言,
/// 以及首次向导完成序列(保存 → 应用语言 → 隐藏向导 → 刷新)。向导订阅由本模块
/// 自接线(Wire/Unwire),因此「首启发生什么」就近可读,不散落在壳协调者中。
/// </summary>
internal sealed class ShellStartup
{
    private readonly Func<CancellationToken, Task> refreshAsync;
    private readonly Action<LauncherSettings> applyMotionSettings;
    private readonly Action<string> applyLanguage;
    private readonly Func<LauncherSettings, Task> saveSettingsAsync;
    private readonly Action hideSetupWizard;
    private readonly Func<bool> isSetupWizardVisible;
    private readonly SetupWizardViewModel setupWizard;
    private int initialized;

    public ShellStartup(
        Func<CancellationToken, Task> refreshAsync,
        Action<LauncherSettings> applyMotionSettings,
        Action<string> applyLanguage,
        Func<LauncherSettings, Task> saveSettingsAsync,
        Action hideSetupWizard,
        Func<bool> isSetupWizardVisible,
        SetupWizardViewModel setupWizard)
    {
        this.refreshAsync = refreshAsync;
        this.applyMotionSettings = applyMotionSettings;
        this.applyLanguage = applyLanguage;
        this.saveSettingsAsync = saveSettingsAsync;
        this.hideSetupWizard = hideSetupWizard;
        this.isSetupWizardVisible = isSetupWizardVisible;
        this.setupWizard = setupWizard;
    }

    /// <summary>Initializes the shell once by loading settings and launcher state.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref initialized, 1) == 1)
        {
            return Task.CompletedTask;
        }

        return refreshAsync(cancellationToken);
    }

    /// <summary>
    /// 首启分支不执行 RefreshAsync(设置快照由向导驱动后再加载),但动效偏好必须
    /// 在向导显示前按默认配置(System 档跟随 Windows 动画开关)先行应用,
    /// 否则 IsMotionReduced 停留在字段默认的 true,首启向导全程处于降动效。
    /// </summary>
    public void ApplyFirstLaunchMotionPreference()
    {
        applyMotionSettings(LauncherSettings.CreateDefaults());
    }

    /// <summary>Applies the initial automatic language before a launcher snapshot exists.</summary>
    public void ApplyInitialLanguage() => applyLanguage(LauncherLanguages.Auto);

    /// <summary>Saves completed wizard settings, applies their language, and refreshes the shell.</summary>
    public async Task HandleSetupWizardCompletedAsync(LauncherSettings newSettings)
    {
        await saveSettingsAsync(newSettings);
        applyLanguage(newSettings.Language);
        hideSetupWizard();
        await refreshAsync(CancellationToken.None);
    }

    /// <summary>Subscribes the first-run wizard events this module owns.</summary>
    public void Wire()
    {
        setupWizard.LanguagePreviewRequested += HandleLanguagePreviewRequested;
        setupWizard.SettingsApplied += HandleSettingsApplied;
    }

    /// <summary>Removes the first-run wizard subscriptions established by <see cref="Wire"/>.</summary>
    public void Unwire()
    {
        setupWizard.LanguagePreviewRequested -= HandleLanguagePreviewRequested;
        setupWizard.SettingsApplied -= HandleSettingsApplied;
    }

    private void HandleLanguagePreviewRequested(string language)
    {
        if (isSetupWizardVisible())
        {
            applyLanguage(language);
        }
    }

    private Task HandleSettingsApplied(LauncherSettings newSettings) =>
        HandleSetupWizardCompletedAsync(newSettings);
}
