using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DialogActionButtonContractTests
{
    [Fact]
    public void SettingsOverlay_UsesNoFooterActionsWhenChangesAutoSave()
    {
        var settingsDocument = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var settingsButtons = settingsDocument
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && HasAnyClass(element, "flat-action", "primary-action")
                && (element.Attribute("Command")?.Value
                    is "{Binding WindowChrome.ShowSettingsCommand}"
                    or "{Binding Settings.SaveSettingsCommand}"))
            .ToArray();

        Assert.Empty(settingsButtons);
    }

    [Fact]
    public void DialogActionButtons_UseUnifiedClassAndIconSize()
    {
        var documents = new[]
        {
            XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml")),
            XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml")),
            XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml")),
            XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml")),
            XDocument.Load(ProjectFile("Controls/ConfirmDialog.axaml")),
        };
        var actionButtons = documents
            .SelectMany(document => document.Descendants())
            .Where(element =>
                element.Name.LocalName == "Button"
                && HasAnyClass(
                    element,
                    "flat-action",
                    "primary-action",
                    "danger-action")
                && HasClass(element, "dialog-action"))
            .ToArray();

        Assert.NotEmpty(actionButtons);
        Assert.All(
            actionButtons,
            button =>
            {
                Assert.True(HasClass(button, "dialog-action"));
                Assert.Null(button.Attribute("Height"));
                Assert.Null(button.Attribute("Width"));
                Assert.All(
                    button
                        .Descendants()
                        .Where(element => element.Name.LocalName == "MaterialIcon"),
                    icon =>
                    {
                        Assert.Equal(
                            "{StaticResource Cafe.Icon.Small}",
                            icon.Attribute("Width")?.Value);
                        Assert.Equal(
                            "{StaticResource Cafe.Icon.Small}",
                            icon.Attribute("Height")?.Value);
                    });
            });
    }

    private static bool HasAnyClass(XElement element, params string[] classes) =>
        classes.Any(className => HasClass(element, className));

    private static bool HasClass(XElement element, string className) =>
        element.Attribute("Classes")?.Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(className, StringComparer.Ordinal) ?? false;

    private static string ProjectFile(string relativePath) =>
        Path.Combine(FindProjectRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cafe.Launcher.Avalonia.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cafe.Launcher.Avalonia.csproj was not found.");
    }

}
