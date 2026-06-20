using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DialogsViewModelTests
{
    static DialogsViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void ShowUpdateAvailable_ListsFilesWithoutSelectingOne()
    {
        var viewModel = CreateViewModel();
        var files = CreateFiles();

        viewModel.ShowUpdateAvailable("1.2.0", files);

        Assert.True(viewModel.IsUpdateAvailableVisible);
        Assert.Equal("1.2.0", viewModel.UpdateAvailableVersion);
        Assert.Equal(files, viewModel.UpdateAvailableFiles);
        Assert.Null(viewModel.SelectedUpdateFile);
        Assert.False(viewModel.HasSelectedUpdateFile);
    }

    [Fact]
    public void ConfirmUpdateAvailable_WithoutSelection_DoesNotCloseOrRequestDownload()
    {
        var viewModel = CreateViewModel();
        string? requestedUrl = null;
        viewModel.ConfirmUpdateAvailableRequested += url => requestedUrl = url;
        viewModel.ShowUpdateAvailable("1.2.0", CreateFiles());

        viewModel.ConfirmUpdateAvailableCommand.Execute(null);

        Assert.True(viewModel.IsUpdateAvailableVisible);
        Assert.Null(requestedUrl);
    }

    [Fact]
    public void ConfirmUpdateAvailable_WithSelection_RequestsSelectedFileUrl()
    {
        var viewModel = CreateViewModel();
        var files = CreateFiles();
        string? requestedUrl = null;
        viewModel.ConfirmUpdateAvailableRequested += url => requestedUrl = url;
        viewModel.ShowUpdateAvailable("1.2.0", files);
        viewModel.SelectedUpdateFile = files[1];

        viewModel.ConfirmUpdateAvailableCommand.Execute(null);

        Assert.False(viewModel.IsUpdateAvailableVisible);
        Assert.Equal(files[1].Url, requestedUrl);
    }

    [Fact]
    public void ShowUpdateAvailable_WhenReopened_ClearsPreviousSelection()
    {
        var viewModel = CreateViewModel();
        var firstFiles = CreateFiles();
        viewModel.ShowUpdateAvailable("1.2.0", firstFiles);
        viewModel.SelectedUpdateFile = firstFiles[0];
        viewModel.CancelUpdateAvailableCommand.Execute(null);

        Assert.False(viewModel.IsUpdateAvailableVisible);
        Assert.Empty(viewModel.UpdateAvailableFiles);
        Assert.Null(viewModel.SelectedUpdateFile);

        var secondFiles = new[]
        {
            new ReleaseFile
            {
                Name = "Cafe.Launcher_v1.3.0.zip",
                Url = "https://example.com/Cafe.Launcher_v1.3.0.zip",
                Size = 7000000
            }
        };
        viewModel.ShowUpdateAvailable("1.3.0", secondFiles);

        Assert.Equal(secondFiles, viewModel.UpdateAvailableFiles);
        Assert.Null(viewModel.SelectedUpdateFile);
        Assert.False(viewModel.HasSelectedUpdateFile);
    }

    private static DialogsViewModel CreateViewModel()
    {
        var noticePath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            "shown_notices.json");
        return new DialogsViewModel(
            new LocalizationService(),
            new NoticeStateService(noticePath));
    }

    private static ReleaseFile[] CreateFiles() =>
    [
        new()
        {
            Name = "Cafe.Launcher_v1.2.0.zip",
            Url = "https://example.com/Cafe.Launcher_v1.2.0.zip",
            Size = 5000000
        },
        new()
        {
            Name = "Cafe.Launcher_Setup_v1.2.0.exe",
            Url = "https://example.com/Cafe.Launcher_Setup_v1.2.0.exe",
            Size = 6000000
        }
    ];
}
