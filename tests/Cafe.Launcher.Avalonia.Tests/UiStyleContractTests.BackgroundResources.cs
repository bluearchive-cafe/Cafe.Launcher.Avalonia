using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

// Background and resource contracts: banner imagery, remote-content loading
// states, window background rendering, tray icons, and bundled assets.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void BannerImage_UsesDistinctLoadingAndFailureStates()
    {
        var mainWindow = File.ReadAllText(ProjectFile("Views/MainWindow.axaml"));

        Assert.Contains(
            "IsVisible=\"{Binding IsImageLoading}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsVisible=\"{Binding IsImageLoadFailed}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Shell.I18n[bannerLoading]",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "BannerBitmap, Converter={x:Static ObjectConverters.IsNull}",
            mainWindow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteContentPanel_UsesExplicitLoadingState()
    {
        var mainWindow = File.ReadAllText(ProjectFile("Views/MainWindow.axaml"));
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var app = XDocument.Load(ProjectFile("App.axaml"));

        Assert.Contains(
            "IsVisible=\"{Binding RemoteContent.IsPanelVisible}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsVisible=\"{Binding RemoteContent.IsLoading}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Shell.I18n[remoteContentLoading]",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsVisible=\"{Binding RemoteContent.HasLoadError}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Shell.I18n[remoteContentLoadFailed]",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Equal("True", GetStyleSetters(styles, "Border.remote-surface")["ClipToBounds"]);
        Assert.Equal("{StaticResource Launcher.Spacing.Thickness.None}", GetStyleSetters(styles, "Border.remote-surface")["Padding"]);
        Assert.Equal("#99000000", app.Descendants().Single(element =>
            element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "Launcher.Color.Overlay.Scrim.Md").Attribute("Color")?.Value);

        var remoteSurface = document.Descendants().Single(element =>
            element.Name.LocalName == "Border" && HasClass(element, "remote-surface"));
        var panel = remoteSurface.Elements().Single(element => element.Name.LocalName == "Panel");
        Assert.Single(panel.Elements(), element =>
            element.Name.LocalName == "ScrollViewer" && HasClass(element, "remote-content-layout-host"));
        Assert.Single(panel.Elements(), element => element.Name.LocalName == "Border");
        Assert.Single(panel.Elements(), element => element.Name.LocalName == "LoadingOverlay");
    }

    [Fact]
    public void MainWindow_BackgroundImages_UseHighQualityInterpolation()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.axaml"));
        var backgroundImages = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Image"
                && (element.Attribute("Source")?.Value == "{Binding Background.BackgroundImageSource}"
                    || element.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "Name"
                        && attribute.Value == "BackgroundCrossFade")))
            .ToArray();

        Assert.Equal(2, backgroundImages.Length);
        Assert.All(
            backgroundImages,
            image => Assert.Equal(
                "HighQuality",
                image.Attribute("RenderOptions.BitmapInterpolationMode")?.Value));
    }

    [Fact]
    public void SystemTrayMenu_DoesNotLoadItemIcons()
    {
        var platform = File.ReadAllText(ProjectFile("Services/SystemTrayPlatform.cs"));

        Assert.DoesNotContain("LoadMenuIcon", platform, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon = menuIcon", platform, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Assets/notification-8be8201c.png",
            platform,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BundledBackground_UsesNormalizedResourceName()
    {
        const string resourceName = "Assets/launcher-background.png";
        var backgroundViewModel =
            File.ReadAllText(ProjectFile("ViewModels/BackgroundViewModel.cs"));

        Assert.True(File.Exists(ProjectFile(resourceName)));
        Assert.Contains(resourceName, backgroundViewModel, StringComparison.Ordinal);
        Assert.False(File.Exists(ProjectFile("Assets/bg-7b36e4e0.png")));
    }
}
