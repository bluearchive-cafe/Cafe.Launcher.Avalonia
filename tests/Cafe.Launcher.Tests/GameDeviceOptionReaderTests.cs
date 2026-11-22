using System.Text.Json.Nodes;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class GameDeviceOptionReaderTests
{
    private readonly GameDeviceOptionReader reader = new();

    [Fact]
    public void Read_BlueArchive174Fixture_MapsInvertedMuteAndToggleValues()
    {
        var result = reader.Read(Fixture("device-option-1.74.json"));

        Assert.Equal(GameDeviceOptionReadStatus.Supported, result.Status);
        Assert.True(result.IsSupported);
        Assert.Null(result.InvalidField);
        Assert.Empty(result.UnknownFieldNames);

        var options = Assert.IsType<GameDeviceOptions>(result.Options);
        Assert.False(options.Audio.Bgm.IsMuted);
        Assert.Equal(0.5795, (double)options.Audio.Bgm.Volume, 4);
        Assert.Equal(0.7636, (double)options.Audio.Fxs.Volume, 4);
        Assert.Equal(1.0, (double)options.Audio.Voice.Volume, 4);
        Assert.NotNull(options.Audio.Master);
        Assert.False(options.Audio.Master.IsMuted);

        Assert.Equal(GameDeviceQualityPreset.VeryHigh, options.QualityPreset);
        Assert.Equal(GameDeviceFrameRateMode.Sixty, options.FrameRate);
        Assert.Equal(GameDeviceDrawcallMode.SrpBatcher, options.DrawcallMode);
        Assert.True(options.AllowPostProcessing);
        Assert.Equal(GameDeviceAntiAliasing.On, options.AntiAliasing);
        Assert.False(options.LetterBoxInBattle);

        Assert.Equal(1, options.AudioVolumeVersion);
        Assert.Equal(GameDeviceScreenRatio.SixteenByNine, options.ScreenRatio);
        Assert.Equal(GameDeviceScreenMode.FullScreen, options.ScreenMode);
        Assert.True(options.VSync);
        Assert.True(options.BackgroundSound);
        Assert.True(options.ControlDisplay);
        Assert.True(options.MouseCursor);
        Assert.True(options.Vibration);
        Assert.Equal(GameDeviceControllerCursorSpeed.Normal, options.ControllerCursorSpeed);
        Assert.True(options.CursorAutoFocus);
        Assert.True(options.NotificationAfterDownloadAlarm);
        Assert.Equal(GameDeviceRenderScale.Percent100, options.RenderScale);
        Assert.Equal(14, options.TitleVideoAudioId);
        Assert.True(options.IsInitialized);

        var window = Assert.IsType<GameDeviceWindowRect>(options.WindowRect);
        Assert.Equal(320, window.X);
        Assert.Equal(167, window.Y);
        Assert.Equal(1280, window.Width);
        Assert.Equal(720, window.Height);
    }

    [Fact]
    public void Read_CalibrationFixture_MapsWindowedFourByThreeAndThirtyFps()
    {
        var result = reader.Read(Fixture("device-option-calibration-1.74.json"));

        Assert.Equal(GameDeviceOptionReadStatus.Supported, result.Status);
        var options = Assert.IsType<GameDeviceOptions>(result.Options);
        Assert.Equal(GameDeviceScreenMode.Windowed, options.ScreenMode);
        Assert.Equal(GameDeviceScreenRatio.FourByThree, options.ScreenRatio);
        Assert.Equal(GameDeviceFrameRateMode.Thirty, options.FrameRate);
        Assert.Equal(GameDeviceDrawcallMode.DynamicBatching, options.DrawcallMode);
        Assert.Equal(GameDeviceAntiAliasing.Off, options.AntiAliasing);
        Assert.False(options.AllowPostProcessing);
        Assert.True(options.LetterBoxInBattle);
        Assert.Equal(GameDeviceRenderScale.Percent200, options.RenderScale);
        Assert.True(options.VSync);
        Assert.False(options.BackgroundSound);
        Assert.False(options.ControlDisplay);
        Assert.False(options.MouseCursor);
        Assert.True(options.Vibration);
        Assert.Equal(GameDeviceControllerCursorSpeed.Normal, options.ControllerCursorSpeed);
        Assert.True(options.CursorAutoFocus);
        Assert.True(options.NotificationAfterDownloadAlarm);
        Assert.Equal(9, options.TitleVideoAudioId);

        var window = Assert.IsType<GameDeviceWindowRect>(options.WindowRect);
        Assert.Equal(312, window.X);
        Assert.Equal(16, window.Y);
        Assert.Equal(1280, window.Width);
        Assert.Equal(960, window.Height);
    }

    [Fact]
    public void Read_PrePcFixture_AllowsMissingNewerFields()
    {
        var result = reader.Read(Fixture("device-option-pre-pc.json"));

        Assert.Equal(GameDeviceOptionReadStatus.Supported, result.Status);
        var options = Assert.IsType<GameDeviceOptions>(result.Options);
        Assert.Null(options.Audio.Master);
        Assert.Null(options.AudioVolumeVersion);
        Assert.Null(options.ScreenMode);
        Assert.Null(options.ScreenRatio);
        Assert.Null(options.VSync);
        Assert.Null(options.WindowRect);
        Assert.Null(options.RenderScale);
        Assert.Equal(GameDeviceQualityPreset.High, options.QualityPreset);
        Assert.Equal(GameDeviceFrameRateMode.Thirty, options.FrameRate);
        Assert.Equal(GameDeviceDrawcallMode.DynamicBatching, options.DrawcallMode);
        Assert.False(options.AllowPostProcessing);
        Assert.Equal(GameDeviceAntiAliasing.On, options.AntiAliasing);
    }

    [Fact]
    public void Read_WhenFileContainsUnknownField_ReportsNameWithoutRejecting()
    {
        var result = reader.Read(Fixture("device-option-future-field.json"));

        Assert.Equal(GameDeviceOptionReadStatus.Supported, result.Status);
        Assert.True(result.IsSupported);
        Assert.Equal(["FutureOption"], result.UnknownFieldNames);
    }

    [Theory]
    [InlineData(
        "device-option-missing-required.json",
        GameDeviceOptionReadStatus.UnsupportedSchema,
        "Resolution")]
    [InlineData(
        "device-option-uncalibrated-enum.json",
        GameDeviceOptionReadStatus.UnsupportedSchema,
        "FPS")]
    [InlineData(
        "device-option-invalid-audio.json",
        GameDeviceOptionReadStatus.InvalidValues,
        "BGM")]
    public void Read_UnsupportedFixture_ReturnsStableFailureWithoutPartialOptions(
        string fixtureName,
        GameDeviceOptionReadStatus expectedStatus,
        string expectedField)
    {
        var result = reader.Read(Fixture(fixtureName));

        Assert.Equal(expectedStatus, result.Status);
        Assert.False(result.IsSupported);
        Assert.Null(result.Options);
        Assert.Equal(expectedField, result.InvalidField);
        Assert.Empty(result.UnknownFieldNames);
    }

    [Fact]
    public void Read_WhenFileDoesNotExist_ReturnsMissingWithoutCreatingIt()
    {
        using var directory = TestDirectory.Create();
        var path = directory.Sub("DeviceOption");

        var result = reader.Read(path);

        Assert.Equal(GameDeviceOptionReadStatus.Missing, result.Status);
        Assert.False(result.IsSupported);
        Assert.Null(result.Options);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Read_WhenJsonIsMalformed_ReturnsInvalidJson()
    {
        using var directory = TestDirectory.Create();
        var path = directory.Sub("DeviceOption");
        File.WriteAllText(path, """{"BGM":""");

        var result = reader.Read(path);

        Assert.Equal(GameDeviceOptionReadStatus.InvalidJson, result.Status);
        Assert.Null(result.Options);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    public void Read_WhenRootIsNotAnObject_ReturnsInvalidJson(string json)
    {
        using var directory = TestDirectory.Create();
        var path = directory.Sub("DeviceOption");
        File.WriteAllText(path, json);

        var result = reader.Read(path);

        Assert.Equal(GameDeviceOptionReadStatus.InvalidJson, result.Status);
        Assert.Null(result.Options);
    }

    [Theory]
    [InlineData("BGM")]
    [InlineData("FXS")]
    [InlineData("Voice")]
    public void Read_WhenRequiredAudioObjectIsNull_ReturnsUnsupportedSchema(string property)
    {
        using var directory = TestDirectory.Create();
        var path = WriteVariant(directory, root => root[property] = null);

        var result = reader.Read(path);

        Assert.Equal(GameDeviceOptionReadStatus.UnsupportedSchema, result.Status);
        Assert.Equal(property, result.InvalidField);
    }

    [Theory]
    [InlineData("BGM", "Mute")]
    [InlineData("BGM", "Volume")]
    [InlineData("FXS", "Mute")]
    [InlineData("Voice", "Volume")]
    [InlineData("Master", "Mute")]
    public void Read_WhenRequiredAudioValueIsMissing_ReturnsUnsupportedSchema(
        string audioProperty,
        string valueProperty)
    {
        using var directory = TestDirectory.Create();
        var path = WriteVariant(
            directory,
            root => root[audioProperty]!.AsObject().Remove(valueProperty));

        var result = reader.Read(path);

        Assert.Equal(GameDeviceOptionReadStatus.UnsupportedSchema, result.Status);
        Assert.Equal(audioProperty, result.InvalidField);
    }

    [Theory]
    [InlineData("BGM", "Mute", -1.0)]
    [InlineData("FXS", "Mute", 2.0)]
    [InlineData("Voice", "Mute", -1.0)]
    [InlineData("Master", "Mute", 2.0)]
    [InlineData("BGM", "Volume", 2.0)]
    [InlineData("FXS", "Volume", -0.1)]
    [InlineData("Voice", "Volume", 1.1)]
    [InlineData("Master", "Volume", 2.0)]
    public void Read_WhenAudioValueIsOutOfRange_ReturnsInvalidValues(
        string audioProperty,
        string valueProperty,
        double value)
    {
        using var directory = TestDirectory.Create();
        var path = WriteVariant(
            directory,
            root => root[audioProperty]!.AsObject()[valueProperty] = JsonValue.Create(value));

        var result = reader.Read(path);

        Assert.Equal(GameDeviceOptionReadStatus.InvalidValues, result.Status);
        Assert.Equal(audioProperty, result.InvalidField);
    }

    [Theory]
    [InlineData("Resolution")]
    [InlineData("FPS")]
    [InlineData("DrawcallMode")]
    [InlineData("AllowPostProcess")]
    [InlineData("AntiAliasing")]
    public void Read_WhenRequiredScalarIsMissing_ReturnsUnsupportedSchema(string property)
    {
        using var directory = TestDirectory.Create();
        var path = WriteVariant(directory, root => root.Remove(property));

        var result = reader.Read(path);

        Assert.Equal(GameDeviceOptionReadStatus.UnsupportedSchema, result.Status);
        Assert.Equal(property, result.InvalidField);
    }

    [Theory]
    [InlineData("Resolution", 9)]
    [InlineData("FPS", 9)]
    [InlineData("DrawcallMode", 9)]
    [InlineData("AntiAliasing", 2)]
    [InlineData("ScreenModeType", 9)]
    [InlineData("ScreenRatioType", 9)]
    [InlineData("AllowPostProcess", 2)]
    [InlineData("LetterBoxInBattle", 2)]
    [InlineData("VSync", 2)]
    [InlineData("BackgroundSound", 2)]
    [InlineData("ControlDisplay", 2)]
    [InlineData("MXMouseCursor", 2)]
    [InlineData("Vibration", 2)]
    [InlineData("CursorAutoFocus", 2)]
    [InlineData("NotificationAfterDownloadAlarm", 2)]
    [InlineData("ControllerCursorSpeed", 3)]
    public void Read_WhenKnownEnumIsOutOfRange_ReturnsStableFailure(
        string property,
        int value)
    {
        using var directory = TestDirectory.Create();
        var path = WriteVariant(directory, root => root[property] = value);

        var result = reader.Read(path);

        var expectedStatus = property is "AllowPostProcess"
            or "LetterBoxInBattle"
            or "VSync"
            or "BackgroundSound"
            or "ControlDisplay"
            or "MXMouseCursor"
            or "Vibration"
            or "CursorAutoFocus"
            or "NotificationAfterDownloadAlarm"
            ? GameDeviceOptionReadStatus.InvalidValues
            : GameDeviceOptionReadStatus.UnsupportedSchema;
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(property, result.InvalidField);
    }

    [Fact]
    public void Read_WhenLetterBoxIsMissing_UsesTheGameDefault()
    {
        using var directory = TestDirectory.Create();
        var path = WriteVariant(directory, root => root.Remove("LetterBoxInBattle"));

        var result = reader.Read(path);

        Assert.Equal(GameDeviceOptionReadStatus.Supported, result.Status);
        var options = Assert.IsType<GameDeviceOptions>(result.Options);
        Assert.False(options.LetterBoxInBattle);
    }

    [Theory]
    [InlineData("m_XMin")]
    [InlineData("m_YMin")]
    [InlineData("m_Width")]
    [InlineData("m_Height")]
    public void Read_WhenWindowRectMissesValue_ReturnsInvalidValues(string property)
    {
        using var directory = TestDirectory.Create();
        var path = WriteVariant(
            directory,
            root => root["windowRect"]!.AsObject().Remove(property));

        var result = reader.Read(path);

        Assert.Equal(GameDeviceOptionReadStatus.InvalidValues, result.Status);
        Assert.Equal("windowRect", result.InvalidField);
    }

    [Fact]
    public void Read_WhenRenderScaleMissesValue_ReturnsInvalidValues()
    {
        using var directory = TestDirectory.Create();
        var path = WriteVariant(
            directory,
            root => root["renderScale"]!.AsObject().Remove("scale"));

        var result = reader.Read(path);

        Assert.Equal(GameDeviceOptionReadStatus.InvalidValues, result.Status);
        Assert.Equal("renderScale", result.InvalidField);
    }

    [Fact]
    public void Read_WhenRenderScaleIsOutOfRange_ReturnsInvalidValues()
    {
        using var directory = TestDirectory.Create();
        var path = WriteVariant(
            directory,
            root => root["renderScale"]!.AsObject()["scale"] = 6);

        var result = reader.Read(path);

        Assert.Equal(GameDeviceOptionReadStatus.InvalidValues, result.Status);
        Assert.Equal("renderScale", result.InvalidField);
    }

    [Fact]
    public void Read_WhenFileIsLocked_ReturnsUnreadable()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Windows sharing semantics are required to make an open file unreadable.");

        using var directory = TestDirectory.Create();
        var path = directory.Sub("DeviceOption");
        File.Copy(Fixture("device-option-1.74.json"), path);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = reader.Read(path);

        Assert.Equal(GameDeviceOptionReadStatus.Unreadable, result.Status);
        Assert.Null(result.Options);
    }

    private static string WriteVariant(TestDirectory directory, Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(File.ReadAllText(Fixture("device-option-1.74.json")))!.AsObject();
        mutate(root);
        var path = directory.Sub("DeviceOption");
        File.WriteAllText(path, root.ToJsonString());
        return path;
    }

    private static string Fixture(string name) =>
        TestRepository.FromRepositoryRoot(
            $"tests/Cafe.Launcher.Tests/Fixtures/GameDeviceOption/{name}");
}
