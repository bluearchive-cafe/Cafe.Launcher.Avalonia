using Cafe.Launcher.UI.ViewModels;

namespace Cafe.Launcher.Tests;

public partial class MainWindowViewModelTests
{
    [Theory]
    [InlineData("PNG")]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData("bmp")]
    [InlineData("webp")]
    [InlineData("MP4")]
    [InlineData("mkv")]
    [InlineData("webm")]
    public async Task ResolveRandomBackgroundFile_WhenFolderHasSupportedFile_ReturnsFileFromFolder(string extension)
    {
        var folderPath = Path.Combine(tempDir, "wallpapers");
        Directory.CreateDirectory(folderPath);
        var imagePath = Path.Combine(folderPath, $"wallpaper.{extension}");
        await WriteTestPngAsync(imagePath);

        var resolved = BackgroundViewModel.ResolveRandomBackgroundFile(folderPath);

        Assert.Equal(imagePath, resolved);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("mp4")]
    public async Task ResolveRandomBackgroundFile_WhenOnlySubfolderHasMedia_ReturnsNull(string extension)
    {
        var folderPath = Path.Combine(tempDir, "wallpapers");
        var nestedFolderPath = Path.Combine(folderPath, "nested");
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(nestedFolderPath);
        await WriteTestPngAsync(Path.Combine(nestedFolderPath, $"wallpaper.{extension}"));

        var resolved = BackgroundViewModel.ResolveRandomBackgroundFile(folderPath);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveRandomBackgroundFile_WhenFolderHasNoSupportedImage_ReturnsNull()
    {
        var folderPath = Path.Combine(tempDir, "empty-wallpapers");
        Directory.CreateDirectory(folderPath);

        var resolved = BackgroundViewModel.ResolveRandomBackgroundFile(folderPath);

        Assert.Null(resolved);
    }

    private static Task WriteTestPngAsync(string path)
    {
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        return File.WriteAllBytesAsync(path, bytes);
    }
}
