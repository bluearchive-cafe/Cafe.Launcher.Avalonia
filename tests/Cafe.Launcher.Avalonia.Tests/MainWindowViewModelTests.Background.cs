using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public partial class MainWindowViewModelTests
{
    [Fact]
    public async Task ResolveRandomBackgroundImage_WhenFolderHasSupportedImage_ReturnsImageFromFolder()
    {
        var folderPath = Path.Combine(tempDir, "wallpapers");
        Directory.CreateDirectory(folderPath);
        var imagePath = Path.Combine(folderPath, "wallpaper.PNG");
        await WriteTestPngAsync(imagePath);

        var resolved = BackgroundViewModel.ResolveRandomBackgroundImage(folderPath);

        Assert.Equal(imagePath, resolved);
    }

    [Fact]
    public async Task ResolveRandomBackgroundImage_WhenOnlySubfolderHasImage_ReturnsNull()
    {
        var folderPath = Path.Combine(tempDir, "wallpapers");
        var nestedFolderPath = Path.Combine(folderPath, "nested");
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(nestedFolderPath);
        await WriteTestPngAsync(Path.Combine(nestedFolderPath, "wallpaper.png"));

        var resolved = BackgroundViewModel.ResolveRandomBackgroundImage(folderPath);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveRandomBackgroundImage_WhenFolderHasNoSupportedImage_ReturnsNull()
    {
        var folderPath = Path.Combine(tempDir, "empty-wallpapers");
        Directory.CreateDirectory(folderPath);

        var resolved = BackgroundViewModel.ResolveRandomBackgroundImage(folderPath);

        Assert.Null(resolved);
    }

    private static Task WriteTestPngAsync(string path)
    {
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        return File.WriteAllBytesAsync(path, bytes);
    }
}
