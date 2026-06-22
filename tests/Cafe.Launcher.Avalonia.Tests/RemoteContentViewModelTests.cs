using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class RemoteContentViewModelTests
{
    static RemoteContentViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void Apply_WhenBannersAreRemoved_StopsExistingCarouselTimer()
    {
        using var httpClientFactory = new HttpClientFactory(new ProxySettingsService());
        using var imageCacheService = new ImageCacheService(
            httpClientFactory,
            new Crc64Service(),
            RemoteHttpUrlValidator.CreateForTesting());
        using var viewModel = new RemoteContentViewModel(
            new LocalizationService(),
            imageCacheService);
        var settings = new LauncherSettings();

        viewModel.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = true,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem(),
                        new OperationsBannerItem()
                    ]
                }
            },
            settings,
            CancellationToken.None);
        Assert.True(viewModel.IsCarouselTimerRunning);

        viewModel.Apply(new LauncherRemoteState(), settings, CancellationToken.None);

        Assert.False(viewModel.IsCarouselTimerRunning);
    }
}
