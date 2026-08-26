using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class UiAccessibilityContractTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void LogViewer_InteractiveControls_ExposeLocalizedAccessibleNames()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
        var filterButtons = document
            .Descendants(Avalonia + "Button")
            .Where(element => HasClass(element, "log-filter"))
            .ToArray();

        Assert.Equal(7, filterButtons.Length);
        Assert.All(filterButtons, button => Assert.StartsWith(
            "{Binding Shell.I18n[logFilter",
            button.Attribute("AutomationProperties.Name")?.Value));

        var search = document
            .Descendants(Avalonia + "TextBox")
            .Single(element => HasClass(element, "log-search"));
        Assert.Equal(
            "{Binding Shell.I18n[logSearchPlaceholder]}",
            search.Attribute("AutomationProperties.Name")?.Value);
    }

    [Fact]
    public void UpdateAndNoticeActions_ExposeLocalizedAccessibleNames()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var commands = new[]
        {
            "{Binding Dialogs.DismissNoticeCommand}",
            "{Binding Dialogs.CancelUpdateAvailableCommand}",
            "{Binding Dialogs.ConfirmUpdateAvailableCommand}"
        };

        foreach (var command in commands)
        {
            var buttons = document
                .Descendants(Avalonia + "Button")
                .Where(element => element.Attribute("Command")?.Value == command)
                .ToArray();

            Assert.NotEmpty(buttons);
            Assert.All(buttons, button => Assert.False(
                string.IsNullOrWhiteSpace(button.Attribute("AutomationProperties.Name")?.Value)));
        }
    }

    [Fact]
    public void DesignGallery_StateSamples_DoNotEnterKeyboardTabOrder()
    {
        var document = XDocument.Load(ProjectFile("Views/DesignGalleryOverlay.axaml"));
        var samples = document
            .Descendants()
            .Where(element => HasClass(element, "gallery-button") || HasClass(element, "gallery-select"))
            .ToArray();

        Assert.Equal(12, samples.Length);
        Assert.All(samples, sample => Assert.Equal("False", sample.Attribute("IsTabStop")?.Value));
    }

    private static bool HasClass(XElement element, string className) =>
        (element.Attribute("Classes")?.Value ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(className, StringComparer.Ordinal);

    private static string ProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(
                   directory.FullName,
                   "src",
                   "Cafe.Launcher.Avalonia",
                   "Cafe.Launcher.Avalonia.csproj")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("Project root was not found."),
            "src",
            "Cafe.Launcher.Avalonia",
            relativePath);
    }
}
