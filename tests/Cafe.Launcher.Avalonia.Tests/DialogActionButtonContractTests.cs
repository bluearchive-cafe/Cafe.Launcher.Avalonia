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
    public void DialogActionConsumers_UseSharedHeight()
    {
        var settingsDocument = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var settingsButtons = settingsDocument
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && (element.Attribute("Classes")?.Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains("settings-footer-action", StringComparer.Ordinal) ?? false))
            .ToArray();

        Assert.Equal(2, settingsButtons.Length);
        Assert.All(
            settingsButtons,
            button => Assert.Contains(
                "dialog-action",
                button.Attribute("Classes")!.Value.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal));

        var dialogsDocument = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
        var continueAfterCrashButton = dialogsDocument
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value
                    == "{Binding Dialogs.ContinueAfterCrashCommand}");

        Assert.Null(continueAfterCrashButton.Attribute("Height"));
    }

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
