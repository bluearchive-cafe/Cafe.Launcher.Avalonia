using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Features.Settings;

public sealed class SettingsOptionsViewModel
{
    private readonly LocalizationService localizer;
    private readonly DiskSpaceService diskSpaceService;

    public SettingsOptionsViewModel(
        LocalizationService localizer,
        DiskSpaceService diskSpaceService)
    {
        this.localizer = localizer;
        this.diskSpaceService = diskSpaceService;
        Language = LocalizationService.GetLanguageOptions(localizer);
        BackgroundSource = CreateSettingOptions(SettingOptionDescriptors.BackgroundSource);
        BackgroundFit = CreateSettingOptions(SettingOptionDescriptors.BackgroundFit);
        ThemeColor = CreateSettingOptions(SettingOptionDescriptors.ThemeColor);
        LaunchCheckMode = CreateSettingOptions(SettingOptionDescriptors.LaunchCheckMode);
        ProxyMode = CreateSettingOptions(SettingOptionDescriptors.ProxyMode);
        PatchUrlGroup = CreateSettingOptions(SettingOptionDescriptors.PatchUrlGroup);
        DownloadSpeedLimit = CreateSettingOptions(SettingOptionDescriptors.DownloadSpeedLimit);
        CloseBehavior = CreateSettingOptions(SettingOptionDescriptors.CloseBehavior);
        UpdateChannel = CreateSettingOptions(SettingOptionDescriptors.UpdateChannel);
        LogLevel = CreateSettingOptions(SettingOptionDescriptors.LogLevel);
        Theme = CreateThemeOptions(SettingOptionDescriptors.Theme);
        MotionMode = CreateSettingOptions(SettingOptionDescriptors.MotionMode);
        StatusDetailMode = CreateSettingOptions(SettingOptionDescriptors.StatusDetailMode);
    }

    public ObservableCollection<SettingOption> BackgroundSource { get; }

    public ObservableCollection<SettingOption> BackgroundFit { get; }

    public ObservableCollection<SettingOption> ThemeColor { get; }

    public ObservableCollection<SettingOption> LaunchCheckMode { get; }

    public ObservableCollection<SettingOption> ProxyMode { get; }

    public ObservableCollection<SettingOption> PatchUrlGroup { get; }

    public ObservableCollection<SettingOption> DownloadSpeedLimit { get; }

    public ObservableCollection<SettingOption> CloseBehavior { get; }

    public IReadOnlyList<LanguageOption> Language { get; }

    public ObservableCollection<SettingOption> UpdateChannel { get; }

    public ObservableCollection<SettingOption> LogLevel { get; }

    public ObservableCollection<ThemeOption> Theme { get; }

    public ObservableCollection<SettingOption> MotionMode { get; }

    public ObservableCollection<SettingOption> StatusDetailMode { get; }

    public ObservableCollection<SettingOption> SettingsCategories { get; } = [];

    public void RefreshDisplayNames()
    {
        Language.First(option => option.Code == LauncherLanguages.Auto).DisplayName = localizer.T("languageAuto");
        EnsureSettingCategories();
        UpdateSettingCategory(SettingsCategoryCodes.General, localizer.T("settingsCategoryGeneral"), localizer.T("settingsCategoryGeneralDescription"));
        UpdateSettingCategory(SettingsCategoryCodes.Game, localizer.T("settingsCategoryGame"), localizer.T("settingsCategoryGameDescription"));
        UpdateSettingCategory(SettingsCategoryCodes.DownloadNetwork, localizer.T("settingsCategoryDownloadNetwork"), localizer.T("settingsCategoryDownloadNetworkDescription"));
        UpdateSettingCategory(SettingsCategoryCodes.Appearance, localizer.T("settingsCategoryAppearance"), localizer.T("settingsCategoryAppearanceDescription"));
        UpdateSettingCategory(SettingsCategoryCodes.Advanced, localizer.T("settingsCategoryAdvanced"), localizer.T("settingsCategoryAdvancedDescription"));
        UpdateSettingCategory(SettingsCategoryCodes.About, localizer.T("settingsCategoryAbout"), localizer.T("settingsCategoryAboutDescription"));

        RefreshOptions(Theme, SettingOptionDescriptors.Theme);
        RefreshOptions(MotionMode, SettingOptionDescriptors.MotionMode);
        RefreshOptions(StatusDetailMode, SettingOptionDescriptors.StatusDetailMode);
        RefreshOptions(ThemeColor, SettingOptionDescriptors.ThemeColor);
        RefreshOptions(LaunchCheckMode, SettingOptionDescriptors.LaunchCheckMode);
        RefreshOptions(ProxyMode, SettingOptionDescriptors.ProxyMode);
        RefreshOptions(PatchUrlGroup, SettingOptionDescriptors.PatchUrlGroup);
        RefreshOptions(CloseBehavior, SettingOptionDescriptors.CloseBehavior);
        RefreshOptions(DownloadSpeedLimit, SettingOptionDescriptors.DownloadSpeedLimit);
        RefreshOptions(BackgroundSource, SettingOptionDescriptors.BackgroundSource);
        RefreshOptions(BackgroundFit, SettingOptionDescriptors.BackgroundFit);
        RefreshOptions(UpdateChannel, SettingOptionDescriptors.UpdateChannel);
        RefreshOptions(LogLevel, SettingOptionDescriptors.LogLevel);
    }

    private static ObservableCollection<SettingOption> CreateSettingOptions(
        IReadOnlyList<SettingOptionDescriptor> descriptors)
    {
        return new ObservableCollection<SettingOption>(descriptors.Select(descriptor => new SettingOption
        {
            Code = descriptor.Code
        }));
    }

    private static ObservableCollection<ThemeOption> CreateThemeOptions(
        IReadOnlyList<SettingOptionDescriptor> descriptors)
    {
        return new ObservableCollection<ThemeOption>(descriptors.Select(descriptor => new ThemeOption
        {
            Code = descriptor.Code
        }));
    }

    private void RefreshOptions<TOption>(
        IEnumerable<TOption> options,
        IReadOnlyList<SettingOptionDescriptor> descriptors)
        where TOption : SelectableOption
    {
        foreach (var option in options)
        {
            var resourceKey = SettingOptionDescriptors.ResolveDisplayResourceKey(descriptors, option.Code);
            option.DisplayName = localizer.T(resourceKey);
        }
    }

    private void EnsureSettingCategories()
    {
        if (SettingsCategories.Count > 0)
        {
            return;
        }

        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.General });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.Game });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.DownloadNetwork });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.Appearance });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.Advanced });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.About });
    }

    private void UpdateSettingCategory(string code, string displayName, string description)
    {
        var option = SettingsCategories.First(option => option.Code == code);
        option.DisplayName = displayName;
        option.Description = description;
    }

    public string ResolveLanguageDisplayName(string language) =>
        Language.FirstOrDefault(option => option.Code == language)?.DisplayName
        ?? Language.First(option => option.Code == LauncherLanguages.Auto).DisplayName;

    public string ResolveThemeDisplayName(string themeMode) =>
        Theme.FirstOrDefault(option => option.Code == themeMode)?.DisplayName
        ?? localizer.T("themeSystem");

    public string ResolveLaunchCheckDisplayName(string launchCheckMode) =>
        launchCheckMode switch
        {
            LaunchCheckModes.RemoteManifest => localizer.T("statusLaunchCheckRemote"),
            LaunchCheckModes.None => localizer.T("statusLaunchCheckNone"),
            _ => localizer.T("statusLaunchCheckLocal")
        };

    public DiskSpaceCheckResult ResolveDiskSpaceCheck(string gamePath, string? requiredSize)
    {
        var requiredBytes = DiskSpaceService.ResolveRequiredBytes(true, 0L, requiredSize);
        return diskSpaceService.Check(gamePath, requiredBytes);
    }

    public string ResolveDiskSpaceText(string? requiredSize, DiskSpaceCheckResult check)
    {
        var requiredDisplay = string.IsNullOrWhiteSpace(requiredSize)
            ? "--"
            : requiredSize.Replace(" ", "", System.StringComparison.Ordinal);
        var availableDisplay = check.AvailableBytes.HasValue
            ? FileSizeFormatter.Format(check.AvailableBytes.Value)
            : "--";
        var baseText = localizer.F("diskSpace", requiredDisplay, availableDisplay);

        // Only append a conclusion when both required and available are known.
        if (!check.IsAvailableKnown
            || string.IsNullOrWhiteSpace(requiredSize)
            || !FileSizeFormatter.TryParseHumanReadable(requiredSize, out _))
        {
            return baseText;
        }

        if (check.HasEnoughSpace)
        {
            return baseText + " " + localizer.T("diskSpaceOkSuffix");
        }

        var difference = check.RequiredBytes - check.AvailableBytes!.Value;
        return baseText + " " + localizer.F("diskSpaceShortSuffix", FileSizeFormatter.Format(difference));
    }

    public string ResolveDiskSpaceText(string gamePath, string? requiredSize)
    {
        var check = ResolveDiskSpaceCheck(gamePath, requiredSize);
        return ResolveDiskSpaceText(requiredSize, check);
    }
}
