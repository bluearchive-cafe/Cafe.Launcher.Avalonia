using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.Tests;

public sealed class WindowFilePickerServiceTests
{
    [Fact]
    public async Task Pickers_WithoutAttachedWindow_ReturnNoSelection()
    {
        var service = new WindowFilePickerService();

        Assert.Null(await service.PickVideoFileAsync("Choose video"));
        Assert.Null(await service.PickImageFileAsync("Choose image"));
        Assert.Null(await service.PickFolderAsync("Choose folder", "missing-folder"));
    }
}
