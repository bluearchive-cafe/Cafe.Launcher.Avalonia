using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DialogActionButtonContractTests
{
    [Fact]
    public void DialogActionStyle_UsesUnifiedMetrics()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var setters = GetStyleSetters(document, "Button.dialog-action");

        Assert.Equal(
            "{StaticResource LauncherControlHeightDialog}",
            setters["Height"]);
        Assert.Equal("108", setters["MinWidth"]);
        Assert.Equal("16,0", setters["Padding"]);
        Assert.Equal("14", setters["FontSize"]);
        Assert.Equal("SemiBold", setters["FontWeight"]);
        Assert.DoesNotContain(
            document.Descendants(),
            element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Button.settings-footer-action");
    }

    [Fact]
    public void SettingsFooterActions_UseDialogActionClass()
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

        Assert.Equal(2, settingsButtons.Length);
        Assert.Equal("flat-action dialog-action", settingsButtons[0].Attribute("Classes")?.Value);
        Assert.Equal("primary-action dialog-action", settingsButtons[1].Attribute("Classes")?.Value);
    }

    [Fact]
    public void DialogActionButtons_UseUnifiedClassAndIconSize()
    {
        var documents = new[]
        {
            XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml")),
            XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml")),
            XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml")),
        };
        var actionButtons = documents
            .SelectMany(document => document.Descendants())
            .Where(element =>
                element.Name.LocalName == "Button"
                && HasAnyClass(
                    element,
                    "flat-action",
                    "primary-action",
                    "danger-action"))
            .ToArray();

        Assert.Equal(25, actionButtons.Length);
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
                            "{StaticResource LauncherIconSm}",
                            icon.Attribute("Width")?.Value);
                        Assert.Equal(
                            "{StaticResource LauncherIconSm}",
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

    private static IReadOnlyDictionary<string, string> GetStyleSetters(
        XDocument document,
        string selector)
    {
        return document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == selector)
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")?.Value
                    ?? throw new InvalidOperationException($"Setter in {selector} has no Property."),
                element => element.Attribute("Value")?.Value
                    ?? throw new InvalidOperationException($"Setter in {selector} has no Value."),
                StringComparer.Ordinal);
    }
}
