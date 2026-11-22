using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

internal sealed class GameDeviceOptionReader : IGameDeviceOptionReader
{
    public GameDeviceOptionReadResult Read(string deviceOptionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceOptionPath);

        try
        {
            using var stream = new FileStream(
                deviceOptionPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            var document = JsonSerializer.Deserialize(
                stream,
                GameDeviceOptionJsonContext.Default.GameDeviceOptionDocument);
            return Parse(document);
        }
        catch (FileNotFoundException)
        {
            return Failure(GameDeviceOptionReadStatus.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(GameDeviceOptionReadStatus.Missing);
        }
        catch (JsonException)
        {
            return Failure(GameDeviceOptionReadStatus.InvalidJson);
        }
        catch (IOException)
        {
            return Failure(GameDeviceOptionReadStatus.Unreadable);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(GameDeviceOptionReadStatus.Unreadable);
        }
    }

    private static GameDeviceOptionReadResult Parse(GameDeviceOptionDocument? document)
    {
        if (document is null)
        {
            return Failure(GameDeviceOptionReadStatus.InvalidJson);
        }

        if (document.Bgm is null)
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "BGM");
        }

        if (document.Fxs is null)
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "FXS");
        }

        if (document.Voice is null)
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "Voice");
        }

        if (!HasAudioValues(document.Bgm))
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "BGM");
        }

        if (!HasAudioValues(document.Fxs))
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "FXS");
        }

        if (!HasAudioValues(document.Voice))
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "Voice");
        }

        if (!TryReadAudio(document.Bgm, out var bgm))
        {
            return Failure(GameDeviceOptionReadStatus.InvalidValues, "BGM");
        }

        if (!TryReadAudio(document.Fxs, out var fxs))
        {
            return Failure(GameDeviceOptionReadStatus.InvalidValues, "FXS");
        }

        if (!TryReadAudio(document.Voice, out var voice))
        {
            return Failure(GameDeviceOptionReadStatus.InvalidValues, "Voice");
        }

        GameDeviceAudioChannel? master = null;
        if (document.Master is not null)
        {
            if (!HasAudioValues(document.Master))
            {
                return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "Master");
            }

            if (!TryReadAudio(document.Master, out var masterChannel))
            {
                return Failure(GameDeviceOptionReadStatus.InvalidValues, "Master");
            }

            master = masterChannel;
        }

        if (document.Resolution is not int qualityRaw)
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "Resolution");
        }

        if (ReadQuality(qualityRaw) is not { } quality)
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "Resolution");
        }

        if (document.Fps is not int frameRateRaw)
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "FPS");
        }

        if (ReadFrameRate(frameRateRaw) is not { } frameRate)
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "FPS");
        }

        if (document.DrawcallMode is not int drawcallRaw)
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "DrawcallMode");
        }

        if (ReadDrawcallMode(drawcallRaw) is not { } drawcallMode)
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "DrawcallMode");
        }

        if (document.AntiAliasing is not int antiAliasingRaw)
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "AntiAliasing");
        }

        if (ReadAntiAliasing(antiAliasingRaw) is not { } antiAliasing)
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "AntiAliasing");
        }

        if (document.AllowPostProcess is not int allowPostProcessRaw)
        {
            return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "AllowPostProcess");
        }

        if (allowPostProcessRaw is not (0 or 1))
        {
            return Failure(GameDeviceOptionReadStatus.InvalidValues, "AllowPostProcess");
        }

        // The game omits this field when it is at its default (ToggleValue Off = 1).
        var letterBoxRaw = document.LetterBoxInBattle ?? 1;
        if (letterBoxRaw is not (0 or 1))
        {
            return Failure(GameDeviceOptionReadStatus.InvalidValues, "LetterBoxInBattle");
        }

        GameDeviceWindowRect? windowRect = null;
        if (document.WindowRect is { } rect)
        {
            if (rect.X is not double x
                || rect.Y is not double y
                || rect.Width is not double width
                || rect.Height is not double height)
            {
                return Failure(GameDeviceOptionReadStatus.InvalidValues, "windowRect");
            }

            windowRect = new GameDeviceWindowRect(x, y, width, height);
        }

        double? renderScale = null;
        if (document.RenderScale is { } scale)
        {
            if (scale.Scale is not double scaleValue)
            {
                return Failure(GameDeviceOptionReadStatus.InvalidValues, "renderScale");
            }

            renderScale = scaleValue;
        }

        GameDeviceScreenMode? screenMode = null;
        if (document.ScreenModeType is int screenModeRaw)
        {
            if (ReadScreenMode(screenModeRaw) is not { } parsedScreenMode)
            {
                return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "ScreenModeType");
            }

            screenMode = parsedScreenMode;
        }

        GameDeviceScreenRatio? screenRatio = null;
        if (document.ScreenRatioType is int screenRatioRaw)
        {
            if (ReadScreenRatio(screenRatioRaw) is not { } parsedScreenRatio)
            {
                return Failure(GameDeviceOptionReadStatus.UnsupportedSchema, "ScreenRatioType");
            }

            screenRatio = parsedScreenRatio;
        }

        var unknownFieldNames = document.ExtensionData is null
            ? Array.Empty<string>()
            : document.ExtensionData.Keys.Order(StringComparer.Ordinal).ToArray();

        var options = new GameDeviceOptions
        {
            Audio = new GameDeviceAudioSettings(bgm, fxs, voice, master),
            QualityPreset = quality,
            FrameRate = frameRate,
            DrawcallMode = drawcallMode,
            AllowPostProcessing = allowPostProcessRaw == 0,
            AntiAliasing = antiAliasing,
            LetterBoxInBattle = letterBoxRaw == 0,
            AudioVolumeVersion = document.AudioVolumeVersion,
            ScreenMode = screenMode,
            ScreenRatio = screenRatio,
            VSync = document.VSync,
            BackgroundSound = document.BackgroundSound,
            ControlDisplay = document.ControlDisplay,
            MouseCursor = document.MouseCursor,
            Vibration = document.Vibration,
            ControllerCursorSpeed = document.ControllerCursorSpeed,
            CursorAutoFocus = document.CursorAutoFocus,
            NotificationAfterDownloadAlarm = document.NotificationAfterDownloadAlarm,
            WindowRect = windowRect,
            RenderScale = renderScale,
            TitleVideoAudioId = document.TitleVideoAudioId,
            IsInitialized = document.IsInitialized
        };

        return new GameDeviceOptionReadResult(
            GameDeviceOptionReadStatus.Supported,
            options,
            unknownFieldNames,
            null);
    }

    private static bool HasAudioValues(GameDeviceAudioDocument document) =>
        document.Mute is not null && document.Volume is not null;

    private static bool TryReadAudio(
        GameDeviceAudioDocument document,
        out GameDeviceAudioChannel channel)
    {
        channel = null!;
        if (document.Mute is not int mute || document.Volume is not double volume)
        {
            return false;
        }

        if (mute is not (0 or 1) || volume is < 0 or > 1)
        {
            return false;
        }

        // Mute.On = 0 means muted; Mute.Off = 1 means audible.
        channel = new GameDeviceAudioChannel(mute == 0, (float)volume);
        return true;
    }

    private static GameDeviceQualityPreset? ReadQuality(int raw) => raw switch
    {
        0 => GameDeviceQualityPreset.VeryHigh,
        1 => GameDeviceQualityPreset.High,
        2 => GameDeviceQualityPreset.Normal,
        3 => GameDeviceQualityPreset.Low,
        _ => null
    };

    private static GameDeviceFrameRateMode? ReadFrameRate(int raw) => raw switch
    {
        0 => GameDeviceFrameRateMode.Sixty,
        1 => GameDeviceFrameRateMode.Thirty,
        _ => null
    };

    private static GameDeviceScreenMode? ReadScreenMode(int raw) => raw switch
    {
        0 => GameDeviceScreenMode.FullScreen,
        1 => GameDeviceScreenMode.Windowed,
        _ => null
    };

    private static GameDeviceScreenRatio? ReadScreenRatio(int raw) => raw switch
    {
        0 => GameDeviceScreenRatio.FourByThree,
        1 => GameDeviceScreenRatio.SixteenByNine,
        _ => null
    };

    private static GameDeviceDrawcallMode? ReadDrawcallMode(int raw) => raw switch
    {
        0 => GameDeviceDrawcallMode.SrpBatcher,
        1 => GameDeviceDrawcallMode.DynamicBatching,
        2 => GameDeviceDrawcallMode.Off,
        _ => null
    };

    private static GameDeviceAntiAliasing? ReadAntiAliasing(int raw) => raw switch
    {
        0 => GameDeviceAntiAliasing.Off,
        1 => GameDeviceAntiAliasing.Low,
        2 => GameDeviceAntiAliasing.High,
        _ => null
    };

    private static GameDeviceOptionReadResult Failure(
        GameDeviceOptionReadStatus status,
        string? field = null) =>
        new(status, null, Array.Empty<string>(), field);
}

internal sealed class GameDeviceOptionDocument
{
    [JsonPropertyName("BGM")]
    public GameDeviceAudioDocument? Bgm { get; set; }

    [JsonPropertyName("FXS")]
    public GameDeviceAudioDocument? Fxs { get; set; }

    [JsonPropertyName("Voice")]
    public GameDeviceAudioDocument? Voice { get; set; }

    [JsonPropertyName("Master")]
    public GameDeviceAudioDocument? Master { get; set; }

    [JsonPropertyName("AudioVolumeVersion")]
    public int? AudioVolumeVersion { get; set; }

    [JsonPropertyName("Resolution")]
    public int? Resolution { get; set; }

    [JsonPropertyName("FPS")]
    public int? Fps { get; set; }

    [JsonPropertyName("DrawcallMode")]
    public int? DrawcallMode { get; set; }

    [JsonPropertyName("AllowPostProcess")]
    public int? AllowPostProcess { get; set; }

    [JsonPropertyName("AntiAliasing")]
    public int? AntiAliasing { get; set; }

    [JsonPropertyName("LetterBoxInBattle")]
    public int? LetterBoxInBattle { get; set; }

    [JsonPropertyName("NotificationAfterDownloadAlarm")]
    public int? NotificationAfterDownloadAlarm { get; set; }

    [JsonPropertyName("ScreenRatioType")]
    public int? ScreenRatioType { get; set; }

    [JsonPropertyName("ScreenModeType")]
    public int? ScreenModeType { get; set; }

    [JsonPropertyName("BackgroundSound")]
    public int? BackgroundSound { get; set; }

    [JsonPropertyName("ControlDisplay")]
    public int? ControlDisplay { get; set; }

    [JsonPropertyName("VSync")]
    public int? VSync { get; set; }

    [JsonPropertyName("MXMouseCursor")]
    public int? MouseCursor { get; set; }

    [JsonPropertyName("windowRect")]
    public GameDeviceWindowRectDocument? WindowRect { get; set; }

    [JsonPropertyName("renderScale")]
    public GameDeviceRenderScaleDocument? RenderScale { get; set; }

    [JsonPropertyName("Vibration")]
    public int? Vibration { get; set; }

    [JsonPropertyName("ControllerCursorSpeed")]
    public int? ControllerCursorSpeed { get; set; }

    [JsonPropertyName("CursorAutoFocus")]
    public int? CursorAutoFocus { get; set; }

    [JsonPropertyName("TitleVideoAudioId")]
    public long? TitleVideoAudioId { get; set; }

    [JsonPropertyName("IsInitialized")]
    public bool? IsInitialized { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal sealed class GameDeviceAudioDocument
{
    [JsonPropertyName("Mute")]
    public int? Mute { get; set; }

    [JsonPropertyName("Volume")]
    public double? Volume { get; set; }
}

internal sealed class GameDeviceWindowRectDocument
{
    [JsonPropertyName("m_XMin")]
    public double? X { get; set; }

    [JsonPropertyName("m_YMin")]
    public double? Y { get; set; }

    [JsonPropertyName("m_Width")]
    public double? Width { get; set; }

    [JsonPropertyName("m_Height")]
    public double? Height { get; set; }
}

internal sealed class GameDeviceRenderScaleDocument
{
    [JsonPropertyName("scale")]
    public double? Scale { get; set; }
}
