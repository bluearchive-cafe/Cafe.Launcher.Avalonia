using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

internal sealed class FakeErrorHandlingService : IErrorHandlingService
{
    public Task HandleErrorAsync(string context, Exception exception, ErrorHandlingOptions? options = null)
        => Task.CompletedTask;

    public Task HandleCriticalErrorAsync(string context, Exception exception)
        => Task.CompletedTask;

    public event Action<CriticalErrorInfo>? CriticalErrorRequested
    {
        add { }
        remove { }
    }
}

public sealed class SettingsOptionsDiskSpaceTests
{
    static SettingsOptionsDiskSpaceTests()
    {
        TestLocalizationHelper.Initialize();
    }

    private static SettingsOptionsViewModel CreateOptions(DiskSpaceService diskSpaceService)
    {
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.SimplifiedChinese);
        return new SettingsOptionsViewModel(localizer, diskSpaceService);
    }

    [Fact]
    public void ResolveDiskSpaceText_WhenRequiredIsMissing_AppendsNoSuffix()
    {
        var diskSpace = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ => 1024L
        };
        var options = CreateOptions(diskSpace);

        var text = options.ResolveDiskSpaceText(@"C:\Games\YostarGames\BlueArchive_JP", null);

        var localized = new LocalizationService();
        localized.SetLanguage(LauncherLanguages.SimplifiedChinese);
        Assert.Equal("所需 -- / 可用 1KB", text);
        Assert.DoesNotContain(localized.T("diskSpaceOkSuffix"), text, StringComparison.Ordinal);
        Assert.DoesNotContain(localized.T("diskSpaceShortSuffix").Split('{')[0], text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDiskSpaceText_WhenAvailableCannotBeRead_AppendsNoSuffix()
    {
        var diskSpace = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ => null
        };
        var options = CreateOptions(diskSpace);

        var text = options.ResolveDiskSpaceText(@"C:\Games\YostarGames\BlueArchive_JP", "10GB");

        var localized = new LocalizationService();
        localized.SetLanguage(LauncherLanguages.SimplifiedChinese);
        Assert.Equal("所需 10GB / 可用 --", text);
        Assert.DoesNotContain(localized.T("diskSpaceOkSuffix"), text, StringComparison.Ordinal);
        Assert.DoesNotContain(localized.T("diskSpaceShortSuffix").Split('{')[0], text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDiskSpaceText_WhenAvailableMeetsRequired_AppendsOkSuffix()
    {
        var diskSpace = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ => 20L * 1024 * 1024 * 1024 // 20 GB
        };
        var options = CreateOptions(diskSpace);

        var text = options.ResolveDiskSpaceText(@"C:\Games\YostarGames\BlueArchive_JP", "10GB");

        var localized = new LocalizationService();
        localized.SetLanguage(LauncherLanguages.SimplifiedChinese);
        Assert.Equal("所需 10GB / 可用 20GB （充足）", text);
        Assert.Contains(localized.T("diskSpaceOkSuffix"), text, StringComparison.Ordinal);
        Assert.DoesNotContain(localized.T("diskSpaceShortSuffix").Split('{')[0], text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDiskSpaceText_WhenAvailableBelowRequired_AppendsShortSuffixWithDifference()
    {
        var diskSpace = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ => 6L * 1024 * 1024 * 1024 // 6 GB
        };
        var options = CreateOptions(diskSpace);

        var text = options.ResolveDiskSpaceText(@"C:\Games\YostarGames\BlueArchive_JP", "10GB");

        var localized = new LocalizationService();
        localized.SetLanguage(LauncherLanguages.SimplifiedChinese);
        Assert.Equal("所需 10GB / 可用 6GB （不足，还差 4GB）", text);
        // The suffix template carries a {0} placeholder; the resolved text should embed the difference
        // (4GB), and contain the suffix's leading literal before the placeholder.
        Assert.Contains(localized.T("diskSpaceShortSuffix").Split('{')[0], text, StringComparison.Ordinal);
        Assert.Contains("4GB", text, StringComparison.Ordinal);
        Assert.DoesNotContain(localized.T("diskSpaceOkSuffix"), text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDiskSpaceCheck_WhenAvailableBelowRequired_FormatsTheSameResultWithoutAnotherRead()
    {
        var readCount = 0;
        var diskSpace = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ =>
            {
                readCount++;
                return 6L * 1024 * 1024 * 1024;
            }
        };
        var options = CreateOptions(diskSpace);

        var check = options.ResolveDiskSpaceCheck(
            @"C:\Games\YostarGames\BlueArchive_JP",
            "10 GB");
        var text = options.ResolveDiskSpaceText("10 GB", check);

        Assert.Equal(1, readCount);
        Assert.Equal(10L * 1024 * 1024 * 1024, check.RequiredBytes);
        Assert.Equal(6L * 1024 * 1024 * 1024, check.AvailableBytes);
        Assert.False(check.HasEnoughSpace);
        Assert.Equal("所需 10GB / 可用 6GB （不足，还差 4GB）", text);
    }

    [Theory]
    [InlineData(6L * 1024 * 1024 * 1024, true)]
    [InlineData(null, false)]
    public void ApplySnapshot_ForFreshInstall_UsesOneDiskReadAndBlocksOnlyKnownShortage(
        long? availableBytes,
        bool expectedBlocked)
    {
        var readCount = 0;
        var diskSpace = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ =>
            {
                readCount++;
                return availableBytes;
            }
        };
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.SimplifiedChinese);
        var options = new SettingsOptionsViewModel(localizer, diskSpace);
        var editor = new SettingsEditor();
        var errorHandling = new FakeErrorHandlingService();
        var shell = new ShellViewModel(localizer);
        using var settings = new SettingsViewModel(
            null!,
            localizer,
            null!,
            null!,
            null!,
            null!,
            null!,
            options,
            new SettingsAppearanceViewModel(editor),
            errorHandling,
            new GameRuntimeStatusService(Array.Empty<IGameRunner>()));
        var snapshot = new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.NotInstalled,
            Settings = new LauncherSettings { GamePath = @"C:\Games\YostarGames\BlueArchive_JP" },
            LocalGame = new LocalInstallationState { GamePath = @"C:\Games\YostarGames\BlueArchive_JP" },
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse { DecompressionSize = "10GB" }
            }
        };

        shell.ApplySnapshot(snapshot, settings);

        Assert.Equal(1, readCount);
        Assert.Equal(expectedBlocked, shell.IsInstallBlockedByDiskSpace);
        if (expectedBlocked)
        {
            Assert.Equal("所需 10GB / 可用 6GB （不足，还差 4GB）", shell.DiskSpaceText);
            Assert.Equal("磁盘空间不足：需要 10GB，可用 6GB。", shell.InstallDiskSpaceMessage);
        }
        else
        {
            Assert.Equal("所需 10GB / 可用 --", shell.DiskSpaceText);
            Assert.Empty(shell.InstallDiskSpaceMessage);
        }
    }

    [Fact]
    public void ResolveDiskSpaceText_WhenRequiredCannotBeParsed_AppendsNoSuffix()
    {
        var diskSpace = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ => 1024L
        };
        var options = CreateOptions(diskSpace);

        var text = options.ResolveDiskSpaceText(@"C:\Games\YostarGames\BlueArchive_JP", "garbage");

        var localized = new LocalizationService();
        localized.SetLanguage(LauncherLanguages.SimplifiedChinese);
        Assert.Equal("所需 garbage / 可用 1KB", text);
        Assert.DoesNotContain(localized.T("diskSpaceOkSuffix"), text, StringComparison.Ordinal);
        Assert.DoesNotContain(localized.T("diskSpaceShortSuffix").Split('{')[0], text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10GB", 10L * 1024 * 1024 * 1024)]
    [InlineData("10 GB", 10L * 1024 * 1024 * 1024)]
    [InlineData("1.5GB", 1610612736L)]
    [InlineData("1024", 1024L)]
    public void TryParseHumanReadable_WhenGivenValidString_ReturnsBytes(string value, long expected)
    {
        Assert.True(FileSizeFormatter.TryParseHumanReadable(value, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("10 GB trailing")]
    [InlineData("9223372036854775808")]
    [InlineData("999999999999999999999GB")]
    public void TryParseHumanReadable_WhenGivenInvalidString_ReturnsFalse(string value)
    {
        Assert.False(FileSizeFormatter.TryParseHumanReadable(value, out var parsed));
        Assert.Equal(0, parsed);
    }
}
