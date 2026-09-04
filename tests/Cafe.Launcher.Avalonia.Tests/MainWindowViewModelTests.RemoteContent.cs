using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Tests;

public partial class MainWindowViewModelTests
{
    [Fact]
    public async Task InitializeAsync_WhenNewsAndNoticesExist_AddsBothToNewsCategories()
    {
        var snapshot = CreateSnapshot();
        snapshot.Remote.OperationsResource = CreateOperationsResource();
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();

        var items = viewModel.RemoteContent.NewsCategories.SelectMany(category => category.Items);
        Assert.Contains(items, item => item.Title == "news title");
        Assert.Contains(items, item => item.Title == "notice title");
    }

    [Fact]
    public async Task InitializeAsync_WhenShowRemoteContentCardIsFalse_HidesRemoteContentCard()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.ShowRemoteContentCard = false;
        snapshot.Remote.OperationsResource = CreateOperationsResource();
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();

        var items = viewModel.RemoteContent.NewsCategories.SelectMany(category => category.Items);
        Assert.Contains(items, item => item.Title == "news title");
        Assert.Contains(items, item => item.Title == "notice title");
        Assert.True(viewModel.RemoteContent.HasNewsItems);
        Assert.False(viewModel.RemoteContent.HasRemoteContent);
        Assert.False(viewModel.RemoteContent.IsPanelVisible);
    }

    [Fact]
    public async Task InitializeAsync_WhenSocialChannelIsPixiv_UsesPaletteIcon()
    {
        var snapshot = CreateSnapshot();
        snapshot.Remote.SocialMediaResource = new SocialMediaResourceResponse
        {
            SocialMediaResourceOpen = true,
            SocialMediaResourceList =
            [
                new SocialMediaResourceItem
                {
                    SocialMediaChannel = "pixiv",
                    JumpUrl = "https://example.invalid/pixiv"
                }
            ]
        };
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();

        var item = Assert.Single(viewModel.RemoteContent.SocialMediaItems);
        Assert.Equal("Palette", item.SocialIconKind);
    }

    [Fact]
    public void RemoteContentItem_ImageStateTransitionsSeparateLoadingAndFailure()
    {
        var item = new RemoteContentItem();

        Assert.True(item.IsImageLoading);
        Assert.False(item.IsImageLoadFailed);

        item.MarkImageLoadFailed();

        Assert.False(item.IsImageLoading);
        Assert.True(item.IsImageLoadFailed);

        item.MarkImageLoading();

        Assert.True(item.IsImageLoading);
        Assert.False(item.IsImageLoadFailed);

        item.MarkImageLoaded();

        Assert.False(item.IsImageLoading);
        Assert.False(item.IsImageLoadFailed);
    }
}
