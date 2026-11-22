using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Features.ResourcePanel;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.UI.ViewModels;

internal partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly LocalizationService localizer;
    private readonly LauncherBuildIdentity? buildIdentity;
    private readonly string cafeWebsiteUrl;
    private readonly Func<string, Task<bool>> copyText;
    private readonly INotificationService? notifications;

    private static readonly string RuntimeDescription =
        $"{RuntimeInformation.FrameworkDescription} ({RuntimeInformation.RuntimeIdentifier})";
    private static readonly string PlatformDescription =
        $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    [ObservableProperty]
    private string productName = "";

    [ObservableProperty]
    private string launcherVersionText = "";

    /// <summary>Title-bar label with an invariant v prefix and the host's complete version.</summary>
    public string TitleBarVersionText => buildIdentity?.LauncherVersion is { Length: > 0 } version
        ? $"v{version}"
        : "";

    /// <summary>OS-drawn window caption: product name plus the host version when known.</summary>
    public string WindowTitle => TitleBarVersionText.Length > 0
        ? $"{ProductName} {TitleBarVersionText}"
        : ProductName;

    partial void OnProductNameChanged(string value) => OnPropertyChanged(nameof(WindowTitle));

    [ObservableProperty]
    private string frameworkVersionText = "";

    [ObservableProperty]
    private string avaloniaVersionText = "";

    /// <summary>Whether the host platform is Linux; gates Linux-only settings such as the game runtime.</summary>
    public bool IsLinuxPlatform => OperatingSystem.IsLinux();

    // 关于分区（ADR-018 融合变体）：身份卡版本 caption 与 key-value 行的值。
    [ObservableProperty]
    private string versionCaptionText = "";

    [ObservableProperty]
    private string commitShaValue = "";

    [ObservableProperty]
    private string buildConfigValue = "";

    [ObservableProperty]
    private string platformValue = "";

    [ObservableProperty]
    private string pathText = "";

    [ObservableProperty]
    private string versionText = "";

    [ObservableProperty]
    private string networkText = "";

    [ObservableProperty]
    private string downloadSourceText = "";

    [ObservableProperty]
    private string launchCheckText = "";

    [ObservableProperty]
    private string executableText = "";

    [ObservableProperty]
    private string executableNameText = "";

    [ObservableProperty]
    private string launchCheckValueText = "";

    [ObservableProperty]
    private string diskSpaceText = "";

    [ObservableProperty]
    private bool isInstallBlockedByDiskSpace;

    [ObservableProperty]
    private string installDiskSpaceMessage = "";

    [ObservableProperty]
    private string settingsSummary = "";

    [ObservableProperty]
    private bool isBusy = true;

    [ObservableProperty]
    private FontFamily fontFamily =
        LanguageFontFamilyService.GetForEffectiveLanguage(LauncherLanguages.English);

    public LocalizedTextCatalog I18n { get; }

    [ObservableProperty]
    private IReadOnlyList<AboutCredit> contributors = [];

    [ObservableProperty]
    private IReadOnlyList<AboutCredit> specialThanks = [];

    /// <param name="productProfile">
    /// 产品档案：窗口标题、托盘与关于页展示的产品名来自它。
    /// </param>
    /// <param name="buildIdentity">
    /// 宿主注入的构建标识（版本/提交/构建时间/配置）。此前读宿主 <c>BuildInfo</c>，迁入 UI 后改为注入。
    /// </param>
    public ShellViewModel(
        LauncherProductProfile productProfile,
        LocalizationService localizer,
        LauncherBuildIdentity? buildIdentity = null,
        PresentationEnvironment? environment = null,
        INotificationService? notifications = null)
    {
        this.localizer = localizer;
        this.buildIdentity = buildIdentity;
        this.notifications = notifications;
        copyText = environment?.CopyTextAsync
            ?? new Func<string, Task<bool>>(static _ => Task.FromResult(false));
        cafeWebsiteUrl = productProfile.CafeWebsiteUrl;
        productName = ResolveProductName(productProfile.ProductName, DateTime.Now, Random.Shared.Next(2));
        I18n = new LocalizedTextCatalog(localizer);
        RefreshAboutCredits();
    }

    internal static string ResolveProductName(string productName, DateTime date, int randomIndex)
    {
        if (date.Month != 12 || date.Day != 8)
        {
            return productName;
        }

        return randomIndex switch
        {
            0 => "Midori Launcher",
            1 => "Momoi Launcher",
            _ => throw new ArgumentOutOfRangeException(nameof(randomIndex)),
        };
    }

    /// <summary>
    /// 语言变化时随 Shell 一并刷新的呈现成员（D10）：由 ShellLifecycle 装配后注入，
    /// 与设置页的预览委托同一「父装配后接线」惯例。
    /// 为 null 时（直构 ShellViewModel 的单元测试）只刷新 Shell 自己。
    /// </summary>
    public IReadOnlyList<ILanguageAwarePresentation>? LanguageAwarePresentations { get; set; }

    public void ApplyLanguage(
        string language,
        bool hasSnapshot)
    {
        var previousLanguage = localizer.CurrentLanguage;
        var effectiveLanguage = localizer.SetLanguage(language);
        if (effectiveLanguage != previousLanguage)
        {
            RefreshAboutCredits();
        }

        FontFamily = LanguageFontFamilyService.GetForEffectiveLanguage(effectiveLanguage);
        LauncherVersionText = localizer.F(LocalizationKeys.LauncherVersionLabel, buildIdentity?.LauncherVersion ?? "");
        FrameworkVersionText = RuntimeDescription;
        AvaloniaVersionText = ResolveAvaloniaVersion();
        var launcherVersion = buildIdentity?.LauncherVersion ?? "";
        var buildTime = buildIdentity?.BuildTime ?? "";
        VersionCaptionText = string.IsNullOrWhiteSpace(buildTime)
            ? localizer.F(LocalizationKeys.LauncherVersionLabel, launcherVersion)
            : localizer.F(LocalizationKeys.AboutVersionCaption, launcherVersion, buildTime);
        CommitShaValue = buildIdentity?.CommitSha ?? "";
        BuildConfigValue = buildIdentity?.BuildConfiguration ?? "";
        PlatformValue = PlatformDescription;

        // 展示刷新不写设置草稿：首次向导的语言预览也走这里，预览改了草稿就等于用户
        // 没保存过也把编辑器变脏（保存按钮亮起、设置页显示预览过的语言）。语言进入
        // 已保存设置只有两条路——设置页自己保存，或向导完成时整份替换。
        DiskSpaceText = localizer.T(LocalizationKeys.DiskSpaceEmpty);

        if (LanguageAwarePresentations is not null)
        {
            foreach (var presentation in LanguageAwarePresentations)
            {
                presentation.RefreshLocalizedText();
            }
        }

        if (!hasSnapshot)
        {
            SetLoadingPlaceholders();
            VersionText = localizer.T(LocalizationKeys.VersionLoading);
            NetworkText = localizer.T(LocalizationKeys.NetworkLoading);
            DownloadSourceText = localizer.T(LocalizationKeys.DownloadSourceLoading);
            LaunchCheckText = localizer.T(LocalizationKeys.LaunchCheckLoading);
            SettingsSummary = localizer.T(LocalizationKeys.Settings);
        }
    }

    public void SetLoading()
    {
        ExecutableNameText = localizer.T(LocalizationKeys.LauncherLoadingValue);
        LaunchCheckValueText = localizer.T(LocalizationKeys.LauncherLoadingValue);
    }

    /// <summary>
    /// 把四行「还在读」的占位文案复位：语言切换（尚无快照时）与刷新失败都要重写这几行，
    /// 此前逐处各抄一遍——新增一行状态就得记得改三处。
    /// </summary>
    private void SetLoadingPlaceholders()
    {
        PathText = localizer.T(LocalizationKeys.PathLoading);
        ExecutableText = localizer.T(LocalizationKeys.ExecutableLoading);
        ExecutableNameText = localizer.T(LocalizationKeys.LauncherLoadingValue);
        LaunchCheckValueText = localizer.T(LocalizationKeys.LauncherLoadingValue);
    }

    private void RefreshAboutCredits()
    {
        Contributors =
        [
            new("gytxtx", localizer.T(LocalizationKeys.AboutCreditGytxtx),
                "https://github.com/gytxtx", FluentGlyphs.Account),
            new("MF-Dust", localizer.T(LocalizationKeys.AboutCreditMfDust),
                "https://github.com/MF-Dust", FluentGlyphs.Account)
        ];
        SpecialThanks =
        [
            new("Avalonia", localizer.T(LocalizationKeys.AboutCreditAvalonia),
                "https://github.com/AvaloniaUI/Avalonia", FluentGlyphs.Code),
            new("CommunityToolkit.Mvvm", localizer.T(LocalizationKeys.AboutCreditCommunityToolkit),
                "https://github.com/CommunityToolkit/dotnet", FluentGlyphs.Code),
            new(localizer.T(LocalizationKeys.AboutCreditBlueArchiveCafe), localizer.T(LocalizationKeys.AboutCreditBlueArchiveCafeDescription),
                cafeWebsiteUrl, FluentGlyphs.Globe),
            new(localizer.T(LocalizationKeys.AboutCreditAppIcon), localizer.T(LocalizationKeys.AboutCreditAppIconDescription),
                "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/commit/3401794c9b06956a09a6308dfaae21341b3814f5", FluentGlyphs.Heart)
        ];
    }

    /// <summary>
    /// 构建信息单行复制：值为空时不写剪贴板，避免复制出一个空串。
    /// </summary>
    [RelayCommand]
    private async Task CopyBuildInfoValueAsync(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        await CopyToClipboardAsync(value);
    }

    /// <summary>
    /// 「复制全部」拼装：首行是产品名与版本 caption，其后是本地化的键值行；
    /// 值缺失的行跳过，方便用户把整段贴进 issue。
    /// </summary>
    [RelayCommand]
    private Task CopyAllBuildInfoAsync()
    {
        var rows = new (string Label, string Value)[]
        {
            (localizer.T(LocalizationKeys.AboutRowCommit), CommitShaValue),
            (localizer.T(LocalizationKeys.AboutRowBuildConfig), BuildConfigValue),
            (localizer.T(LocalizationKeys.AboutRowRuntime), FrameworkVersionText),
            (localizer.T(LocalizationKeys.AboutRowAvalonia), AvaloniaVersionText),
            (localizer.T(LocalizationKeys.AboutRowPlatform), PlatformValue),
        };

        var text = new StringBuilder()
            .Append(ProductName)
            .Append(' ')
            .AppendLine(VersionCaptionText);
        foreach (var (label, value) in rows)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            text.Append(label).Append(": ").AppendLine(value);
        }

        return CopyToClipboardAsync(text.ToString().TrimEnd());
    }

    private async Task CopyToClipboardAsync(string text)
    {
        if (await copyText(text))
        {
            notifications?.ShowSuccess(localizer.T(LocalizationKeys.AboutCopied));
            return;
        }

        notifications?.ShowError(localizer.T(LocalizationKeys.AboutCopyFailed));
    }

    public void SetRefreshError(Exception exception, SettingsViewModel settings, string? gameId = null)
    {
        // 刷新失败在状态栏只报中性可行动的状态（与快照的远端不可用同措辞）；
        // 异常原文已进诊断日志，不放进行内小空间。
        NetworkText = localizer.T(LocalizationKeys.GameRemoteStateUnavailable);
        VersionText = localizer.T(LocalizationKeys.VersionUnavailable);
        // 下载源是本地配置而非远端状态，远端不可用时也照常展示已保存的选择。
        DownloadSourceText = localizer.F(
            LocalizationKeys.DownloadSourceValue,
            settings.Options.ResolveDownloadSourceDisplayName(
                settings.Editor.GetSavedSnapshot().GetGameSettings(gameId ?? LauncherProfiles.BlueArchiveJapan.RuntimeId).PatchUrlGroup));
        SetLoadingPlaceholders();
    }

    public void ApplySnapshot(LauncherStatusSnapshot snapshot, SettingsViewModel settings)
    {
        var gameConfig = snapshot.Remote.GameConfig;
        var localGame = snapshot.LocalGame;
        var localConfig = localGame.GameConfig;

        PathText = snapshot.GameSettings.GamePath;
        VersionText = snapshot.RuntimeState != LauncherRuntimeState.NotInstalled
            ? localizer.F(LocalizationKeys.VersionInstalled, localConfig?.Version, gameConfig?.GameLatestVersion ?? localizer.T(LocalizationKeys.Unknown))
            : localizer.F(LocalizationKeys.VersionLatest, gameConfig?.GameLatestVersion ?? localizer.T(LocalizationKeys.Unknown));
        var networkStatus = snapshot.RuntimeState == LauncherRuntimeState.RemoteUnavailable
            ? localizer.T(LocalizationKeys.GameRemoteStateUnavailable)
            : localizer.T(LocalizationKeys.StatusNetworkLoaded);
        NetworkText = networkStatus;
        DownloadSourceText = localizer.F(
            LocalizationKeys.DownloadSourceValue,
            settings.Options.ResolveDownloadSourceDisplayName(snapshot.GameSettings.PatchUrlGroup));
        var launchCheckValue = settings.Options.ResolveLaunchCheckDisplayName(snapshot.GameSettings.LaunchCheckMode);
        SetLaunchCheckResult(launchCheckValue);
        var executableName = string.IsNullOrWhiteSpace(localConfig?.Name)
            ? gameConfig?.GameStartExeName ?? localizer.T(LocalizationKeys.Unknown)
            : localConfig.Name;
        ExecutableText = localizer.F(LocalizationKeys.ExecutableValue, executableName);
        ExecutableNameText = localizer.F(LocalizationKeys.ExecutableNameValue, executableName);
        var diskCheck = settings.Options.ResolveDiskSpaceCheck(localGame.GamePath, gameConfig?.DecompressionSize);
        DiskSpaceText = settings.Options.ResolveDiskSpaceText(gameConfig?.DecompressionSize, diskCheck);
        IsInstallBlockedByDiskSpace = snapshot.RuntimeState == LauncherRuntimeState.NotInstalled
            && diskCheck.RequiredBytes > 0
            && diskCheck.IsAvailableKnown
            && !diskCheck.HasEnoughSpace;
        InstallDiskSpaceMessage = IsInstallBlockedByDiskSpace
            ? localizer.F(
                LocalizationKeys.DiskSpaceInsufficientDetail,
                FileSizeFormatter.Format(diskCheck.RequiredBytes),
                FileSizeFormatter.Format(diskCheck.AvailableBytes!.Value))
            : "";
        SettingsSummary = localizer.F(
            LocalizationKeys.SettingsSummaryWithTheme,
            snapshot.Settings.ProxyMode,
            snapshot.Settings.CloseBehavior,
            settings.Options.ResolveLanguageDisplayName(snapshot.Settings.Language),
            settings.Options.ResolveThemeDisplayName(snapshot.Settings.ThemeMode));
    }

    public void SetLaunchCheckResult(string value)
    {
        LaunchCheckText = localizer.F(LocalizationKeys.LaunchCheckWithMessage, value);
        LaunchCheckValueText = value;
    }

    public void Dispose()
    {
        I18n.Dispose();
    }

    /// <summary>Avalonia 框架版本：UI 程序集自己就能读到，不再经宿主 BuildInfo 转手。</summary>
    private static string ResolveAvaloniaVersion()
    {
        try
        {
            var version = typeof(global::Avalonia.Application).Assembly.GetName().Version;
            return version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }
}
