using System.Text.Json.Nodes;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.Tests;

public sealed class SettingsGameOptionsViewModelTests
{
    [Fact]
    public void Refresh_CalibrationFixture_FormatsReadOnlyValues()
    {
        var viewModel = Create(fixture: "device-option-calibration-1.74.json");

        Assert.True(viewModel.IsSupported);
        Assert.False(viewModel.HasStatus);
        Assert.False(viewModel.HasUnknownFields);
        Assert.Contains("Unmuted", viewModel.Bgm, StringComparison.Ordinal);
        Assert.Contains("79%", viewModel.Bgm, StringComparison.Ordinal);
        Assert.Equal("Off", viewModel.BackgroundSound);
        Assert.Equal("Very high", viewModel.Quality);
        Assert.Equal("200%", viewModel.RenderScale);
        Assert.Equal("30 FPS", viewModel.FrameRate);
        Assert.Equal("Off", viewModel.AntiAliasing);
        Assert.Equal("Off", viewModel.PostProcessing);
        Assert.Equal("Dynamic batching", viewModel.DrawcallMode);
        Assert.Equal("On", viewModel.LetterBox);
        Assert.Equal("Windowed", viewModel.ScreenMode);
        Assert.Equal("4:3", viewModel.ScreenRatio);
        Assert.Equal("On", viewModel.VSync);
        Assert.Equal("1280 × 960", viewModel.WindowSize);
        Assert.Equal("PV 3", viewModel.TitleVideo);
        Assert.Equal("Off", viewModel.ControlDisplay);
        Assert.Equal("Off", viewModel.MouseCursor);
        Assert.Equal("On", viewModel.Vibration);
        Assert.Equal("Normal", viewModel.ControllerCursorSpeed);
        Assert.Equal("On", viewModel.CursorAutoFocus);
        Assert.Equal("On", viewModel.DownloadNotification);
    }

    [Fact]
    public void Refresh_TitleVideoId14_ResolvesToPv7()
    {
        var viewModel = Create(fixture: "device-option-1.74.json");

        Assert.True(viewModel.IsSupported);
        Assert.Equal("On", viewModel.BackgroundSound);
        Assert.Equal("100%", viewModel.RenderScale);
        Assert.Equal("On", viewModel.AntiAliasing);
        Assert.Equal("On", viewModel.VSync);
        Assert.Equal("On", viewModel.ControlDisplay);
        Assert.Equal("On", viewModel.MouseCursor);
        Assert.Equal("On", viewModel.Vibration);
        Assert.Equal("Normal", viewModel.ControllerCursorSpeed);
        Assert.Equal("On", viewModel.CursorAutoFocus);
        Assert.Equal("On", viewModel.DownloadNotification);
        Assert.Equal("PV 7", viewModel.TitleVideo);
    }

    [Fact]
    public void Refresh_TitleVideoId6WithoutSavedEntry_ShowsNotUnlocked()
    {
        var viewModel = Create(fixture: "device-option-1.74.json", titleVideoId: 6);

        Assert.True(viewModel.IsSupported);
        Assert.Equal("PV 5 (not unlocked)", viewModel.TitleVideo);
    }

    [Fact]
    public void Refresh_TitleVideoRandom_ShowsRandom()
    {
        var viewModel = Create(fixture: "device-option-1.74.json", titleVideoId: -1);

        Assert.True(viewModel.IsSupported);
        Assert.Equal("Random", viewModel.TitleVideo);
    }

    [Fact]
    public void Refresh_FutureFieldFixture_ExposesUnknownFieldWarning()
    {
        var viewModel = Create(fixture: "device-option-future-field.json");

        Assert.True(viewModel.IsSupported);
        Assert.True(viewModel.HasUnknownFields);
        Assert.Contains("FutureOption", viewModel.UnknownFieldsMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_WhenFileIsMissing_ShowsNotGeneratedStatus()
    {
        var viewModel = Create(fixture: null);

        Assert.False(viewModel.IsSupported);
        Assert.True(viewModel.HasStatus);
        Assert.Equal("Not generated yet. Start the game once, then refresh.", viewModel.StatusMessage);
    }

    [Fact]
    public void Refresh_WhenKnownEnumIsUncalibrated_ShowsFieldName()
    {
        var viewModel = Create(fixture: "device-option-uncalibrated-enum.json");

        Assert.False(viewModel.IsSupported);
        Assert.True(viewModel.HasStatus);
        Assert.Contains("FPS", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_WhenTitleVideoFileIsMissing_ShowsUnavailable()
    {
        var viewModel = Create(
            fixture: "device-option-1.74.json",
            titleVideoFixture: null);

        Assert.True(viewModel.IsSupported);
        Assert.Equal("PV data unavailable", viewModel.TitleVideo);
    }

    [Fact]
    public void SetGameTarget_StellaWithoutCalibratedSegments_ShowsUnavailableStatus()
    {
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.English);
        var viewModel = new SettingsGameOptionsViewModel(
            new SettingsEditor(),
            new GameDeviceOptionReader(),
            new TitleVideoAudioReader(),
            localizer,
            LauncherProfiles.BlueArchiveJapan,
            new GameCompatibilityPaths(TestDataRoot.ForCurrentProcess()));

        viewModel.SetGameTarget(LauncherProfiles.StellaSoraChina.RuntimeId, LauncherProfiles.StellaSoraChina);

        Assert.False(viewModel.IsSupported);
        Assert.Equal(
            "This game has no calibrated in-game options.",
            viewModel.StatusMessage);
    }

    private static SettingsGameOptionsViewModel Create(
        string? fixture,
        long? titleVideoId = null,
        string? titleVideoFixture = "TitleVideoAudioSaveData.json")
    {
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.English);
        var path = fixture is null
            ? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "DeviceOption")
            : TestRepository.FromRepositoryRoot(
                $"tests/Cafe.Launcher.Tests/Fixtures/GameDeviceOption/{fixture}");
        if (titleVideoId is long id)
        {
            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            root["TitleVideoAudioId"] = id;
            path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "DeviceOption");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString());
        }

        var titleVideoPath = titleVideoFixture is null
            ? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "TitleVideoAudioSaveData")
            : TestRepository.FromRepositoryRoot(
                $"tests/Cafe.Launcher.Tests/Fixtures/GameDeviceOption/{titleVideoFixture}");

        var viewModel = new SettingsGameOptionsViewModel(
            new SettingsEditor(),
            new GameDeviceOptionReader(),
            new TitleVideoAudioReader(),
            localizer,
            LauncherProfiles.BlueArchiveJapan,
            new GameCompatibilityPaths(TestDataRoot.ForCurrentProcess()),
            path,
            titleVideoPath);
        viewModel.Refresh();
        return viewModel;
    }
}
