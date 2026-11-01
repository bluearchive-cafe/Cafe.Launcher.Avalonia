using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.Tests;

public sealed class WindowFilePickerServiceTests
{
    [Fact]
    public async Task Pickers_WithoutAttachedWindow_ReturnNoSelection()
    {
        var service = new WindowFilePickerService();

        Assert.Null(await service.PickBackgroundFileAsync("Choose background", "Images and videos"));
        Assert.Null(await service.PickFolderAsync("Choose folder", "missing-folder"));
    }
}
