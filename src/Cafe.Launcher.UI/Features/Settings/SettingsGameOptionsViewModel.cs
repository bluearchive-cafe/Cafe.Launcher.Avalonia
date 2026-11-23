using System;
using System.Globalization;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.UI.Features.Settings;

/// <summary>
/// Read-only projection of the game's <c>DeviceOption</c> file for the settings page.
/// It never writes the file; changes must be made in the game itself.
/// </summary>
internal sealed partial class SettingsGameOptionsViewModel : ObservableObject
{
    private readonly SettingsEditor editor;
    private readonly IGameDeviceOptionReader reader;
    private readonly IGameTitleVideoAudioReader titleVideoAudioReader;
    private readonly LocalizationService localizer;
    private readonly GameCompatibilityPaths compatibilityPaths;
    private readonly string? deviceOptionPathOverride;
    private readonly string? titleVideoAudioPathOverride;
    private YostarGameProfile profile;
    private string gameId;

    public SettingsGameOptionsViewModel(
        SettingsEditor editor,
        IGameDeviceOptionReader reader,
        IGameTitleVideoAudioReader titleVideoAudioReader,
        LocalizationService localizer,
        YostarGameProfile profile,
        GameCompatibilityPaths compatibilityPaths,
        string? deviceOptionPathOverride = null,
        string? titleVideoAudioPathOverride = null)
    {
        this.editor = editor;
        this.reader = reader;
        this.titleVideoAudioReader = titleVideoAudioReader;
        this.localizer = localizer;
        this.profile = profile;
        this.compatibilityPaths = compatibilityPaths;
        this.deviceOptionPathOverride = deviceOptionPathOverride;
        this.titleVideoAudioPathOverride = titleVideoAudioPathOverride;
        gameId = profile.RuntimeId;
    }

    /// <summary>Whether the last read produced a complete, calibrated snapshot.</summary>
    public bool IsSupported { get; private set; }

    /// <summary>Whether <see cref="StatusMessage"/> should be shown instead of the values.</summary>
    public bool HasStatus => !IsSupported && !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Localized failure reason; empty when the snapshot is supported.</summary>
    public string StatusMessage { get; private set; } = "";

    /// <summary>Whether the file contains fields this build does not know about.</summary>
    public bool HasUnknownFields { get; private set; }

    /// <summary>Comma-separated unknown field names, already localized by the view's format string.</summary>
    public string UnknownFields { get; private set; } = "";

    /// <summary>Localized warning for unknown fields; empty when there are none.</summary>
    public string UnknownFieldsMessage { get; private set; } = "";

    public string Bgm { get; private set; } = "";
    public string Fxs { get; private set; } = "";
    public string Voice { get; private set; } = "";
    public string Master { get; private set; } = "";
    public string BackgroundSound { get; private set; } = "";
    public string Quality { get; private set; } = "";
    public string RenderScale { get; private set; } = "";
    public string FrameRate { get; private set; } = "";
    public string AntiAliasing { get; private set; } = "";
    public string PostProcessing { get; private set; } = "";
    public string DrawcallMode { get; private set; } = "";
    public string LetterBox { get; private set; } = "";
    public string ScreenMode { get; private set; } = "";
    public string ScreenRatio { get; private set; } = "";
    public string VSync { get; private set; } = "";
    public string WindowSize { get; private set; } = "";
    public string TitleVideo { get; private set; } = "";
    public string ControlDisplay { get; private set; } = "";
    public string MouseCursor { get; private set; } = "";
    public string Vibration { get; private set; } = "";
    public string ControllerCursorSpeed { get; private set; } = "";
    public string CursorAutoFocus { get; private set; } = "";
    public string DownloadNotification { get; private set; } = "";

    public void SetGameTarget(string targetGameId, YostarGameProfile targetProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetGameId);
        ArgumentNullException.ThrowIfNull(targetProfile);
        if (string.Equals(gameId, targetGameId, StringComparison.Ordinal)
            && ReferenceEquals(profile, targetProfile))
        {
            return;
        }

        gameId = targetGameId;
        profile = targetProfile;
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        IsSupported = false;
        StatusMessage = "";
        HasUnknownFields = false;
        UnknownFields = "";
        UnknownFieldsMessage = "";
        ClearValues();

        var segments = profile.DeviceOptionRelativeSegments;
        if (segments.Length == 0)
        {
            ShowFailure(LocalizationKeys.GameOptionUnavailable);
            return;
        }

        var path = deviceOptionPathOverride ?? ResolvePath(profile.DeviceOptionRelativeSegments);
        if (path is null)
        {
            ShowFailure(LocalizationKeys.GameOptionUnsupportedPlatform);
            return;
        }

        var result = reader.Read(path);
        if (!result.IsSupported)
        {
            ShowFailure(FailureKey(result.Status), result.InvalidField);
            return;
        }

        Apply(result.Options!, result.UnknownFieldNames, ReadTitleVideoSelection());
    }

    private string? ResolvePath(string[] relativeSegments)
    {
        if (relativeSegments.Length == 0)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return GameUserDataPathResolver.ResolveWindows(relativeSegments);
        }

        if (OperatingSystem.IsLinux())
        {
            var runtime = editor.Current.GetGameSettings(gameId).GameRuntime;
            return GameUserDataPathResolver.ResolveLinux(
                runtime,
                Environment.UserName,
                runner => compatibilityPaths.GetDefaultPrefixPath(profile.RuntimeId, runner),
                relativeSegments);
        }

        return null;
    }

    private TitleVideoAudioSelection? ReadTitleVideoSelection()
    {
        var path = titleVideoAudioPathOverride
            ?? ResolvePath(profile.TitleVideoAudioRelativeSegments);
        if (path is null)
        {
            return null;
        }

        return titleVideoAudioReader.Read(path).Selection;
    }

    private void Apply(
        GameDeviceOptions options,
        System.Collections.Generic.IReadOnlyList<string> unknownFields,
        TitleVideoAudioSelection? titleVideoSelection)
    {
        IsSupported = true;
        Bgm = FormatAudio(options.Audio.Bgm);
        Fxs = FormatAudio(options.Audio.Fxs);
        Voice = FormatAudio(options.Audio.Voice);
        Master = FormatAudio(options.Audio.Master);
        BackgroundSound = FormatToggle(options.BackgroundSound);
        Quality = options.QualityPreset switch
        {
            GameDeviceQualityPreset.VeryHigh => localizer.T(LocalizationKeys.GameOptionQualityVeryHigh),
            GameDeviceQualityPreset.High => localizer.T(LocalizationKeys.GameOptionQualityHigh),
            GameDeviceQualityPreset.Normal => localizer.T(LocalizationKeys.GameOptionQualityNormal),
            _ => localizer.T(LocalizationKeys.GameOptionQualityLow)
        };
        RenderScale = options.RenderScale is { } renderScale
            ? localizer.F(LocalizationKeys.GameOptionPercentFormat, 100 + (20 * (int)renderScale))
            : "";
        FrameRate = localizer.F(
            LocalizationKeys.GameOptionFpsFormat,
            options.FrameRate == GameDeviceFrameRateMode.Sixty ? 60 : 30);
        AntiAliasing = localizer.T(options.AntiAliasing == GameDeviceAntiAliasing.Off
            ? LocalizationKeys.GameOptionAntiAliasingOff
            : LocalizationKeys.GameOptionOn);
        PostProcessing = localizer.T(options.AllowPostProcessing
            ? LocalizationKeys.GameOptionOn
            : LocalizationKeys.GameOptionOff);
        DrawcallMode = options.DrawcallMode switch
        {
            GameDeviceDrawcallMode.SrpBatcher => localizer.T(LocalizationKeys.GameOptionDrawcallSrpBatcher),
            GameDeviceDrawcallMode.DynamicBatching => localizer.T(LocalizationKeys.GameOptionDrawcallDynamicBatching),
            _ => localizer.T(LocalizationKeys.GameOptionOff)
        };
        LetterBox = localizer.T(options.LetterBoxInBattle
            ? LocalizationKeys.GameOptionOn
            : LocalizationKeys.GameOptionOff);
        ScreenMode = options.ScreenMode switch
        {
            GameDeviceScreenMode.FullScreen => localizer.T(LocalizationKeys.GameOptionScreenModeFullScreen),
            GameDeviceScreenMode.Windowed => localizer.T(LocalizationKeys.GameOptionScreenModeWindowed),
            _ => ""
        };
        ScreenRatio = options.ScreenRatio switch
        {
            GameDeviceScreenRatio.FourByThree => localizer.T(LocalizationKeys.GameOptionScreenRatio4x3),
            GameDeviceScreenRatio.SixteenByNine => localizer.T(LocalizationKeys.GameOptionScreenRatio16x9),
            _ => ""
        };
        VSync = FormatToggle(options.VSync);
        WindowSize = options.WindowRect is { } rect
            ? localizer.F(
                LocalizationKeys.GameOptionWindowSizeFormat,
                FormatDimension(rect.Width),
                FormatDimension(rect.Height))
            : "";
        TitleVideo = FormatTitleVideo(options.TitleVideoAudioId, titleVideoSelection);
        ControlDisplay = FormatToggle(options.ControlDisplay);
        MouseCursor = FormatToggle(options.MouseCursor);
        Vibration = FormatToggle(options.Vibration);
        ControllerCursorSpeed = options.ControllerCursorSpeed switch
        {
            GameDeviceControllerCursorSpeed.Normal => localizer.T(LocalizationKeys.GameOptionControllerCursorSpeedNormal),
            GameDeviceControllerCursorSpeed.Fast => localizer.T(LocalizationKeys.GameOptionControllerCursorSpeedFast),
            GameDeviceControllerCursorSpeed.Slow => localizer.T(LocalizationKeys.GameOptionControllerCursorSpeedSlow),
            _ => ""
        };
        CursorAutoFocus = FormatToggle(options.CursorAutoFocus);
        DownloadNotification = FormatToggle(options.NotificationAfterDownloadAlarm);
        HasUnknownFields = unknownFields.Count > 0;
        UnknownFields = string.Join(", ", unknownFields);
        UnknownFieldsMessage = HasUnknownFields
            ? localizer.F(LocalizationKeys.GameOptionUnknownFieldsFormat, UnknownFields)
            : "";
        NotifyValuesChanged();
    }

    private string FormatTitleVideo(
        long? titleVideoAudioId,
        TitleVideoAudioSelection? selection)
    {
        if (titleVideoAudioId is not long id)
        {
            return "";
        }

        if (id == TitleVideoAudioCatalog.RandomId)
        {
            return localizer.T(LocalizationKeys.GameOptionPvRandom);
        }

        if (selection is null)
        {
            return localizer.T(LocalizationKeys.GameOptionPvUnavailable);
        }

        foreach (var entry in selection.Saved)
        {
            if (entry.Id != id)
            {
                continue;
            }

            var known = TitleVideoAudioCatalog.FindByVideoPath(entry.VideoPath)
                ?? TitleVideoAudioCatalog.FindById(entry.Id);
            return known is not null
                ? localizer.F(
                    LocalizationKeys.GameOptionPvFormat,
                    TitleVideoAudioCatalog.DisplayLabel(known.DisplayOrder))
                : entry.VideoPath;
        }

        var knownById = TitleVideoAudioCatalog.FindById(id);
        return knownById is not null
            ? localizer.F(
                LocalizationKeys.GameOptionPvUnlockedFormat,
                TitleVideoAudioCatalog.DisplayLabel(knownById.DisplayOrder))
            : localizer.F(LocalizationKeys.GameOptionPvIdFormat, id);
    }

    private void ShowFailure(LocalizationKeys key, string? field = null)
    {
        StatusMessage = string.IsNullOrWhiteSpace(field)
            ? localizer.T(key)
            : localizer.F(key, field);
        NotifyValuesChanged();
    }

    private string FormatToggle(bool? value) => value switch
    {
        true => localizer.T(LocalizationKeys.GameOptionOn),
        false => localizer.T(LocalizationKeys.GameOptionOff),
        null => ""
    };

    private string FormatAudio(GameDeviceAudioChannel? channel)
    {
        if (channel is null)
        {
            return "";
        }

        var state = localizer.T(channel.IsMuted
            ? LocalizationKeys.GameOptionMuted
            : LocalizationKeys.GameOptionUnmuted);
        var volume = (int)Math.Round(channel.Volume * 100, MidpointRounding.AwayFromZero);
        return $"{state} · {localizer.F(LocalizationKeys.GameOptionVolumeFormat, volume)}";
    }

    private static string FormatDimension(double value) =>
        Math.Abs(value - Math.Round(value)) < 0.01
            ? ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.#", CultureInfo.InvariantCulture);

    private static LocalizationKeys FailureKey(GameDeviceOptionReadStatus status) => status switch
    {
        GameDeviceOptionReadStatus.Missing => LocalizationKeys.GameOptionNotGenerated,
        GameDeviceOptionReadStatus.Unreadable => LocalizationKeys.GameOptionUnreadable,
        GameDeviceOptionReadStatus.InvalidJson => LocalizationKeys.GameOptionInvalidJson,
        GameDeviceOptionReadStatus.UnsupportedSchema => LocalizationKeys.GameOptionUnsupportedSchemaFormat,
        GameDeviceOptionReadStatus.InvalidValues => LocalizationKeys.GameOptionInvalidValuesFormat,
        _ => LocalizationKeys.GameOptionUnavailable
    };

    private void ClearValues()
    {
        Bgm = "";
        Fxs = "";
        Voice = "";
        Master = "";
        BackgroundSound = "";
        Quality = "";
        RenderScale = "";
        FrameRate = "";
        AntiAliasing = "";
        PostProcessing = "";
        DrawcallMode = "";
        LetterBox = "";
        ScreenMode = "";
        ScreenRatio = "";
        VSync = "";
        WindowSize = "";
        TitleVideo = "";
        ControlDisplay = "";
        MouseCursor = "";
        Vibration = "";
        ControllerCursorSpeed = "";
        CursorAutoFocus = "";
        DownloadNotification = "";
    }

    private void NotifyValuesChanged()
    {
        OnPropertyChanged(nameof(IsSupported));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasUnknownFields));
        OnPropertyChanged(nameof(UnknownFields));
        OnPropertyChanged(nameof(UnknownFieldsMessage));
        OnPropertyChanged(nameof(Bgm));
        OnPropertyChanged(nameof(Fxs));
        OnPropertyChanged(nameof(Voice));
        OnPropertyChanged(nameof(Master));
        OnPropertyChanged(nameof(BackgroundSound));
        OnPropertyChanged(nameof(Quality));
        OnPropertyChanged(nameof(RenderScale));
        OnPropertyChanged(nameof(FrameRate));
        OnPropertyChanged(nameof(AntiAliasing));
        OnPropertyChanged(nameof(PostProcessing));
        OnPropertyChanged(nameof(DrawcallMode));
        OnPropertyChanged(nameof(LetterBox));
        OnPropertyChanged(nameof(ScreenMode));
        OnPropertyChanged(nameof(ScreenRatio));
        OnPropertyChanged(nameof(VSync));
        OnPropertyChanged(nameof(WindowSize));
        OnPropertyChanged(nameof(TitleVideo));
        OnPropertyChanged(nameof(ControlDisplay));
        OnPropertyChanged(nameof(MouseCursor));
        OnPropertyChanged(nameof(Vibration));
        OnPropertyChanged(nameof(ControllerCursorSpeed));
        OnPropertyChanged(nameof(CursorAutoFocus));
        OnPropertyChanged(nameof(DownloadNotification));
    }
}
