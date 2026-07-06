using System.Xml.Linq;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class EasterEggTests
{
    [Theory]
    [InlineData(0, "Midori Launcher")]
    [InlineData(1, "Momoi Launcher")]
    public void ResolveProductName_OnDecemberEighth_ReturnsSpecifiedName(
        int randomIndex,
        string expected)
    {
        var actual = ShellViewModel.ResolveProductName(
            new DateTime(2026, 12, 8),
            randomIndex);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveProductName_OutsideDecemberEighth_ReturnsDefaultName()
    {
        var actual = ShellViewModel.ResolveProductName(
            new DateTime(2026, 12, 9),
            0);

        Assert.Equal(LauncherConstants.ProductName, actual);
    }

    [Fact]
    public void AboutLauncherVersionChip_RegistersPointerHandler()
    {
        var document = XDocument.Load(ProjectFile("Views/SettingsAboutSection.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        var launcherVersionText = document
            .Descendants(avalonia + "TextBlock")
            .Single(element =>
                (string?)element.Attribute("Text") == "{Binding Shell.LauncherVersionText}");
        var versionChip = launcherVersionText.Parent;

        Assert.Equal(
            "LauncherVersionChip_OnPointerPressed",
            (string?)versionChip?.Attribute("PointerPressed"));
        Assert.True(File.Exists(ProjectFile("Assets/kuyashi.ogg")));
    }

    [Fact]
    public void RegisterLauncherVersionClick_TriggersOnEveryEighthClick()
    {
        var shell = new ShellViewModel(new LocalizationService());

        for (var click = 1; click < 8; click++)
        {
            Assert.False(shell.RegisterLauncherVersionClick());
        }

        Assert.True(shell.RegisterLauncherVersionClick());
        Assert.False(shell.RegisterLauncherVersionClick());
    }

    private static string ProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Cafe.Launcher.Avalonia.csproj")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName
                ?? throw new InvalidOperationException("Project root was not found."),
            relativePath);
    }
}
