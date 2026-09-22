using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Media;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly LocalizationService localizer;

    private static readonly string RuntimeDescription =
        $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.RuntimeIdentifier}";
    private static readonly string PlatformDescription =
        $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}";

    [ObservableProperty]
    private string productName = ResolveProductName(DateTime.Now, Random.Shared.Next(2));

    [ObservableProperty]
    private string launcherVersionText = "";

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

    public ShellViewModel(LocalizationService localizer)
    {
        this.localizer = localizer;
        I18n = new LocalizedTextCatalog(localizer);
    }

    internal static string ResolveProductName(DateTime date, int randomIndex)
    {
        if (date.Month != 12 || date.Day != 8)
        {
            return LauncherConstants.ProductName;
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
    /// 与 <see cref="SettingsViewModel.ApplyLanguageAndTheme"/> 同一「父装配后接线」惯例。
    /// 为 null 时（直构 ShellViewModel 的单元测试）只刷新 Shell 自己。
    /// </summary>
    public IReadOnlyList<ILanguageAwarePresentation>? LanguageAwarePresentations { get; set; }

    public void ApplyLanguage(
        string language,
        bool hasSnapshot)
    {
        var effectiveLanguage = localizer.SetLanguage(language);
        FontFamily = LanguageFontFamilyService.GetForEffectiveLanguage(effectiveLanguage);
        LauncherVersionText = localizer.F(LocalizationKeys.LauncherVersionLabel, BuildInfo.LauncherVersion);
        FrameworkVersionText = RuntimeDescription;
        AvaloniaVersionText = BuildInfo.AvaloniaVersion;
        VersionCaptionText = string.IsNullOrWhiteSpace(BuildInfo.BuildTime)
            ? localizer.F(LocalizationKeys.LauncherVersionLabel, BuildInfo.LauncherVersion)
            : localizer.F(LocalizationKeys.AboutVersionCaption, BuildInfo.LauncherVersion, BuildInfo.BuildTime);
        CommitShaValue = BuildInfo.CommitSha;
        BuildConfigValue = BuildInfo.BuildConfiguration;
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

    public void SetRefreshError(Exception exception, SettingsViewModel settings)
    {
        // 刷新失败在状态栏只报中性可行动的状态（与快照的远端不可用同措辞）；
        // 异常原文已进诊断日志，不放进行内小空间。
        NetworkText = localizer.T(LocalizationKeys.GameRemoteStateUnavailable);
        VersionText = localizer.T(LocalizationKeys.VersionUnavailable);
        // 下载源是本地配置而非远端状态，远端不可用时也照常展示已保存的选择。
        DownloadSourceText = localizer.F(
            LocalizationKeys.DownloadSourceValue,
            settings.Options.ResolveDownloadSourceDisplayName(settings.Editor.GetSavedSnapshot().PatchUrlGroup));
        SetLoadingPlaceholders();
    }

    public void ApplySnapshot(LauncherStatusSnapshot snapshot, SettingsViewModel settings)
    {
        var gameConfig = snapshot.Remote.GameConfig;
        var localGame = snapshot.LocalGame;
        var localConfig = localGame.GameConfig;

        PathText = snapshot.Settings.GamePath;
        VersionText = snapshot.RuntimeState != LauncherRuntimeState.NotInstalled
            ? localizer.F(LocalizationKeys.VersionInstalled, localConfig?.Version, gameConfig?.GameLatestVersion ?? localizer.T(LocalizationKeys.Unknown))
            : localizer.F(LocalizationKeys.VersionLatest, gameConfig?.GameLatestVersion ?? localizer.T(LocalizationKeys.Unknown));
        var networkStatus = snapshot.RuntimeState == LauncherRuntimeState.RemoteUnavailable
            ? localizer.T(LocalizationKeys.GameRemoteStateUnavailable)
            : localizer.T(LocalizationKeys.StatusNetworkLoaded);
        NetworkText = networkStatus;
        DownloadSourceText = localizer.F(
            LocalizationKeys.DownloadSourceValue,
            settings.Options.ResolveDownloadSourceDisplayName(snapshot.Settings.PatchUrlGroup));
        var launchCheckValue = settings.Options.ResolveLaunchCheckDisplayName(snapshot.Settings.LaunchCheckMode);
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
}
