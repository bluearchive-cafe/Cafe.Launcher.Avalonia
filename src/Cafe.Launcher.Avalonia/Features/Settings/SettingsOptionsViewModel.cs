using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Cafe.Launcher.Avalonia.Constants;
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
    }

    public ObservableCollection<SettingOption> BackgroundSource { get; } =
    [
        new() { Code = BackgroundSources.Bundled },
        new() { Code = BackgroundSources.Remote },
        new() { Code = BackgroundSources.Custom }
    ];

    public ObservableCollection<SettingOption> BackgroundFit { get; } =
    [
        new() { Code = BackgroundFits.Fill },
        new() { Code = BackgroundFits.Uniform },
        new() { Code = BackgroundFits.UniformToFill }
    ];

    public ObservableCollection<SettingOption> ThemeColor { get; } =
    [
        new() { Code = ThemeColorModes.Default },
        new() { Code = ThemeColorModes.System },
        new() { Code = ThemeColorModes.Wallpaper },
        new() { Code = ThemeColorModes.Custom }
    ];

    public ObservableCollection<SettingOption> ThemeColorExtractionAlgorithm { get; } =
    [
        new() { Code = ThemeColorExtractionAlgorithms.Octree },
        new() { Code = ThemeColorExtractionAlgorithms.CelebiScore },
        new() { Code = ThemeColorExtractionAlgorithms.Wu },
        new() { Code = ThemeColorExtractionAlgorithms.Wsmeans }
    ];

    public ObservableCollection<SettingOption> ThemeColorVariant { get; } =
    [
        new() { Code = ThemeColorVariants.TonalSpot },
        new() { Code = ThemeColorVariants.Vibrant },
        new() { Code = ThemeColorVariants.Expressive },
        new() { Code = ThemeColorVariants.Fidelity },
        new() { Code = ThemeColorVariants.Content },
        new() { Code = ThemeColorVariants.Monochrome },
        new() { Code = ThemeColorVariants.Neutral },
        new() { Code = ThemeColorVariants.Rainbow }
    ];

    public ObservableCollection<SettingOption> NeutralColorStrategy { get; } =
    [
        new() { Code = NeutralColorStrategies.BrandBlue },
        new() { Code = NeutralColorStrategies.SeedFollowing }
    ];

    public ObservableCollection<SettingOption> LaunchCheckMode { get; } =
    [
        new() { Code = LaunchCheckModes.LocalManifest },
        new() { Code = LaunchCheckModes.RemoteManifest },
        new() { Code = LaunchCheckModes.None }
    ];

    // Native is intentionally absent: the runtime section only renders on Linux,
    // where native PE execution can never work (see the cross-platform runtime design).
    public ObservableCollection<SettingOption> GameRuntimeRunner { get; } =
    [
        new() { Code = GameRuntimeRunners.Auto },
        new() { Code = GameRuntimeRunners.Umu },
        new() { Code = GameRuntimeRunners.Wine }
    ];

    public ObservableCollection<SettingOption> ProxyMode { get; } =
    [
        new() { Code = ProxyModes.Auto },
        new() { Code = ProxyModes.Direct },
        new() { Code = ProxyModes.System }
    ];

    public ObservableCollection<SettingOption> PatchUrlGroup { get; } =
    [
        new() { Code = PatchUrlGroups.Official },
        new() { Code = PatchUrlGroups.Cafe }
    ];

    public ObservableCollection<SettingOption> DownloadSpeedLimit { get; } =
    [
        new() { Code = DownloadSpeedLimits.Unlimited },
        new() { Code = DownloadSpeedLimits.Speed1MBs },
        new() { Code = DownloadSpeedLimits.Speed5MBs },
        new() { Code = DownloadSpeedLimits.Speed10MBs },
        new() { Code = DownloadSpeedLimits.Speed25MBs },
        new() { Code = DownloadSpeedLimits.Speed50MBs }
    ];

    public ObservableCollection<SettingOption> CloseBehavior { get; } =
    [
        new() { Code = CloseBehaviors.Minimize },
        new() { Code = CloseBehaviors.Exit }
    ];

    public IReadOnlyList<LanguageOption> Language { get; }

    public ObservableCollection<SettingOption> UpdateChannel { get; } =
    [
        new() { Code = UpdateChannels.Stable },
        new() { Code = UpdateChannels.Beta }
    ];

    public ObservableCollection<SettingOption> LogLevel { get; } =
    [
        new() { Code = LogLevels.Verbose },
        new() { Code = LogLevels.Debug },
        new() { Code = LogLevels.Information },
        new() { Code = LogLevels.Warning },
        new() { Code = LogLevels.Error },
        new() { Code = LogLevels.Fatal }
    ];

    public ObservableCollection<ThemeOption> Theme { get; } =
    [
        new() { Code = ThemeModes.System },
        new() { Code = ThemeModes.Light },
        new() { Code = ThemeModes.Dark }
    ];

    public ObservableCollection<SettingOption> MotionMode { get; } =
    [
        new() { Code = MotionModes.System },
        new() { Code = MotionModes.Full },
        new() { Code = MotionModes.Reduced }
    ];

    public ObservableCollection<SettingOption> StatusDetailMode { get; } =
    [
        new() { Code = StatusDetailModes.Hidden },
        new() { Code = StatusDetailModes.Compact }
    ];

    public ObservableCollection<SettingOption> SettingsCategories { get; } = [];

    public void RefreshDisplayNames()
    {
        Language.First(option => option.Code == LauncherLanguages.Auto).DisplayName = localizer.T(LocalizationKeys.LanguageAuto);
        EnsureSettingCategories();
        UpdateSettingCategory(SettingsCategoryCodes.General, localizer.T(LocalizationKeys.SettingsCategoryGeneral), localizer.T(LocalizationKeys.SettingsCategoryGeneralDescription));
        UpdateSettingCategory(SettingsCategoryCodes.Game, localizer.T(LocalizationKeys.SettingsCategoryGame), localizer.T(LocalizationKeys.SettingsCategoryGameDescription));
        UpdateSettingCategory(SettingsCategoryCodes.DownloadNetwork, localizer.T(LocalizationKeys.SettingsCategoryDownloadNetwork), localizer.T(LocalizationKeys.SettingsCategoryDownloadNetworkDescription));
        UpdateSettingCategory(SettingsCategoryCodes.Appearance, localizer.T(LocalizationKeys.SettingsCategoryAppearance), localizer.T(LocalizationKeys.SettingsCategoryAppearanceDescription));
        UpdateSettingCategory(SettingsCategoryCodes.Advanced, localizer.T(LocalizationKeys.SettingsCategoryAdvanced), localizer.T(LocalizationKeys.SettingsCategoryAdvancedDescription));
        UpdateSettingCategory(SettingsCategoryCodes.About, localizer.T(LocalizationKeys.SettingsCategoryAbout), localizer.T(LocalizationKeys.SettingsCategoryAboutDescription));

        RefreshOptions(Theme, code => code switch
        {
            ThemeModes.Light => localizer.T(LocalizationKeys.ThemeLight),
            ThemeModes.Dark => localizer.T(LocalizationKeys.ThemeDark),
            _ => localizer.T(LocalizationKeys.ThemeSystem)
        });

        RefreshOptions(MotionMode, code => code switch
        {
            MotionModes.Full => localizer.T(LocalizationKeys.MotionModeFull),
            MotionModes.Reduced => localizer.T(LocalizationKeys.MotionModeReduced),
            _ => localizer.T(LocalizationKeys.MotionModeSystem)
        });

        RefreshOptions(StatusDetailMode, code => code switch
        {
            StatusDetailModes.Hidden => localizer.T(LocalizationKeys.StatusDetailModeHidden),
            _ => localizer.T(LocalizationKeys.StatusDetailModeCompact)
        });

        RefreshOptions(ThemeColor, code => code switch
        {
            ThemeColorModes.System => localizer.T(LocalizationKeys.ThemeColorSystem),
            ThemeColorModes.Wallpaper => localizer.T(LocalizationKeys.ThemeColorWallpaper),
            ThemeColorModes.Custom => localizer.T(LocalizationKeys.ThemeColorCustom),
            _ => localizer.T(LocalizationKeys.ThemeColorDefault)
        });

        RefreshOptions(ThemeColorExtractionAlgorithm, code => code switch
        {
            ThemeColorExtractionAlgorithms.Octree => localizer.T(LocalizationKeys.ThemeColorExtractionAlgorithmOctree),
            ThemeColorExtractionAlgorithms.Wu => localizer.T(LocalizationKeys.ThemeColorExtractionAlgorithmWu),
            ThemeColorExtractionAlgorithms.Wsmeans => localizer.T(LocalizationKeys.ThemeColorExtractionAlgorithmWsmeans),
            _ => localizer.T(LocalizationKeys.ThemeColorExtractionAlgorithmCelebiScore)
        });

        RefreshOptions(ThemeColorVariant, code => code switch
        {
            ThemeColorVariants.Vibrant => localizer.T(LocalizationKeys.ThemeColorVariantVibrant),
            ThemeColorVariants.Expressive => localizer.T(LocalizationKeys.ThemeColorVariantExpressive),
            ThemeColorVariants.Fidelity => localizer.T(LocalizationKeys.ThemeColorVariantFidelity),
            ThemeColorVariants.Content => localizer.T(LocalizationKeys.ThemeColorVariantContent),
            ThemeColorVariants.Monochrome => localizer.T(LocalizationKeys.ThemeColorVariantMonochrome),
            ThemeColorVariants.Neutral => localizer.T(LocalizationKeys.ThemeColorVariantNeutral),
            ThemeColorVariants.Rainbow => localizer.T(LocalizationKeys.ThemeColorVariantRainbow),
            _ => localizer.T(LocalizationKeys.ThemeColorVariantTonalSpot)
        });

        RefreshOptions(NeutralColorStrategy, code => code switch
        {
            NeutralColorStrategies.SeedFollowing => localizer.T(LocalizationKeys.NeutralColorStrategySeedFollowing),
            _ => localizer.T(LocalizationKeys.NeutralColorStrategyBrandBlue)
        });

        RefreshOptions(LaunchCheckMode, code => code switch
        {
            LaunchCheckModes.RemoteManifest => localizer.T(LocalizationKeys.LaunchCheckRemoteManifest),
            LaunchCheckModes.None => localizer.T(LocalizationKeys.LaunchCheckNone),
            _ => localizer.T(LocalizationKeys.LaunchCheckLocalManifest)
        });

        RefreshOptions(GameRuntimeRunner, code => code switch
        {
            GameRuntimeRunners.Umu => localizer.T(LocalizationKeys.GameRuntimeRunnerUmu),
            GameRuntimeRunners.Wine => localizer.T(LocalizationKeys.GameRuntimeRunnerWine),
            _ => localizer.T(LocalizationKeys.GameRuntimeRunnerAuto)
        });

        RefreshOptions(ProxyMode, code => code switch
        {
            ProxyModes.Auto => localizer.T(LocalizationKeys.ProxyAuto),
            ProxyModes.System => localizer.T(LocalizationKeys.ProxySystem),
            _ => localizer.T(LocalizationKeys.ProxyDirect)
        });

        RefreshOptions(PatchUrlGroup, code => code switch
        {
            PatchUrlGroups.Cafe => localizer.T(LocalizationKeys.DownloadSourceCafe),
            _ => localizer.T(LocalizationKeys.DownloadSourceOfficial)
        });

        RefreshOptions(CloseBehavior, code => code switch
        {
            CloseBehaviors.Exit => localizer.T(LocalizationKeys.CloseBehaviorExit),
            _ => localizer.T(LocalizationKeys.CloseBehaviorMinimize)
        });

        RefreshOptions(DownloadSpeedLimit, code => code switch
        {
            DownloadSpeedLimits.Speed1MBs => localizer.T(LocalizationKeys.Speed1MBs),
            DownloadSpeedLimits.Speed5MBs => localizer.T(LocalizationKeys.Speed5MBs),
            DownloadSpeedLimits.Speed10MBs => localizer.T(LocalizationKeys.Speed10MBs),
            DownloadSpeedLimits.Speed25MBs => localizer.T(LocalizationKeys.Speed25MBs),
            DownloadSpeedLimits.Speed50MBs => localizer.T(LocalizationKeys.Speed50MBs),
            _ => localizer.T(LocalizationKeys.SpeedUnlimited)
        });

        RefreshOptions(BackgroundSource, code => code switch
        {
            BackgroundSources.Remote => localizer.T(LocalizationKeys.BackgroundSourceRemote),
            BackgroundSources.Custom => localizer.T(LocalizationKeys.BackgroundSourceCustom),
            _ => localizer.T(LocalizationKeys.BackgroundSourceBundled)
        });

        RefreshOptions(BackgroundFit, code => code switch
        {
            BackgroundFits.Fill => localizer.T(LocalizationKeys.BackgroundFitFill),
            BackgroundFits.Uniform => localizer.T(LocalizationKeys.BackgroundFitUniform),
            _ => localizer.T(LocalizationKeys.BackgroundFitUniformToFill)
        });

        RefreshOptions(UpdateChannel, code => code switch
        {
            UpdateChannels.Beta => localizer.T(LocalizationKeys.LauncherUpdateChannelBeta),
            _ => localizer.T(LocalizationKeys.LauncherUpdateChannelStable)
        });

        RefreshOptions(LogLevel, code => code switch
        {
            LogLevels.Verbose => localizer.T(LocalizationKeys.LogLevelVerbose),
            LogLevels.Debug => localizer.T(LocalizationKeys.LogLevelDebug),
            LogLevels.Warning => localizer.T(LocalizationKeys.LogLevelWarning),
            LogLevels.Error => localizer.T(LocalizationKeys.LogLevelError),
            LogLevels.Fatal => localizer.T(LocalizationKeys.LogLevelFatal),
            _ => localizer.T(LocalizationKeys.LogLevelInformation)
        });
    }

    private void RefreshOptions(ObservableCollection<SettingOption> options, System.Func<string, string> resolveDisplayName)
    {
        foreach (var option in options)
        {
            option.DisplayName = resolveDisplayName(option.Code);
        }
    }

    private void RefreshOptions(ObservableCollection<ThemeOption> options, System.Func<string, string> resolveDisplayName)
    {
        foreach (var option in options)
        {
            option.DisplayName = resolveDisplayName(option.Code);
        }
    }

    private void EnsureSettingCategories()
    {
        if (SettingsCategories.Count > 0)
        {
            return;
        }

        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.General, IconKind = "CogOutline", SelectedIconKind = "Cog" });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.Game, IconKind = "GamepadSquareOutline", SelectedIconKind = "GamepadSquare" });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.DownloadNetwork, IconKind = "DownloadOutline", SelectedIconKind = "Download" });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.Appearance, IconKind = "PaletteOutline", SelectedIconKind = "Palette" });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.Advanced, IconKind = "Tune", SelectedIconKind = "Tune" });
        SettingsCategories.Add(new() { Code = SettingsCategoryCodes.About, IconKind = "InformationOutline", SelectedIconKind = "Information" });
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
        ?? localizer.T(LocalizationKeys.ThemeSystem);

    public string ResolveLaunchCheckDisplayName(string launchCheckMode) =>
        launchCheckMode switch
        {
            LaunchCheckModes.RemoteManifest => localizer.T(LocalizationKeys.StatusLaunchCheckRemote),
            LaunchCheckModes.None => localizer.T(LocalizationKeys.StatusLaunchCheckNone),
            _ => localizer.T(LocalizationKeys.StatusLaunchCheckLocal)
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
        var baseText = localizer.F(LocalizationKeys.DiskSpace, requiredDisplay, availableDisplay);

        // Only append a conclusion when both required and available are known.
        if (!check.IsAvailableKnown
            || string.IsNullOrWhiteSpace(requiredSize)
            || !FileSizeFormatter.TryParseHumanReadable(requiredSize, out _))
        {
            return baseText;
        }

        if (check.HasEnoughSpace)
        {
            return baseText + " " + localizer.T(LocalizationKeys.DiskSpaceOkSuffix);
        }

        var difference = check.RequiredBytes - check.AvailableBytes!.Value;
        return baseText + " " + localizer.F(LocalizationKeys.DiskSpaceShortSuffix, FileSizeFormatter.Format(difference));
    }

    public string ResolveDiskSpaceText(string gamePath, string? requiredSize)
    {
        var check = ResolveDiskSpaceCheck(gamePath, requiredSize);
        return ResolveDiskSpaceText(requiredSize, check);
    }
}
