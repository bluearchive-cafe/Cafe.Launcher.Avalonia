using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class WindowChromeViewModelTests
{
    [Theory]
    [InlineData(PatchUrlGroups.Official, LauncherConstants.OfficialGameWebsiteUrl)]
    [InlineData(PatchUrlGroups.Cafe, LauncherConstants.CafeWebsiteUrl)]
    public void ResolveOfficialSiteUrl_UsesCurrentDownloadSource(
        string patchUrlGroup,
        string expectedUrl)
    {
        Assert.Equal(expectedUrl, WindowChromeViewModel.ResolveOfficialSiteUrl(patchUrlGroup));
    }
}
