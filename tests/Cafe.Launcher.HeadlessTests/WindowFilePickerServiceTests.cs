using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.HeadlessTests;

public sealed class WindowFilePickerServiceTests
{
    [AvaloniaFact]
    public async Task Pickers_WhenAttachedWindowHasNoPickerSupport_ReturnNoSelection()
    {
        var service = new WindowFilePickerService();
        var window = new Window();
        service.Attach(window);
        try
        {
            Assert.False(window.StorageProvider.CanOpen);
            Assert.False(window.StorageProvider.CanPickFolder);

            Assert.Null(await service.PickBackgroundFileAsync("Choose background", "Images and videos"));
            Assert.Null(await service.PickFolderAsync("Choose folder", "missing-folder"));
        }
        finally
        {
            service.Detach(window);
            window.Close();
        }

        Assert.Null(await service.PickBackgroundFileAsync("Choose background", "Images and videos"));
    }
}
