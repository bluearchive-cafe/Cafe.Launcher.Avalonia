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
            "{StaticResource Launcher.Control.Height.Dialog}",
            setters["Height"]);
        Assert.Equal("{StaticResource Launcher.Component.Dialog.Action.MinWidth}", setters["MinWidth"]);
        Assert.Equal("{StaticResource Launcher.Component.Action.Dialog.Padding}", setters["Padding"]);
        Assert.Equal(
            "{StaticResource Launcher.Typography.FontSize.Body.Md}",
            setters["FontSize"]);
        Assert.DoesNotContain("FontWeight", setters);
        Assert.DoesNotContain(
            document.Descendants(),
            element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Button.settings-footer-action");
    }

    [Fact]
    public void DialogActionStyle_FollowsBaseSemanticActionStyles()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var styles = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToList();
        var dialogActionIndex = FindStyleIndex(styles, "Button.dialog-action");

        Assert.True(
            dialogActionIndex > FindStyleIndex(styles, "Button.primary-action"),
            "Button.dialog-action must follow Button.primary-action.");
        Assert.True(
            dialogActionIndex > FindStyleIndex(styles, "Button.flat-action"),
            "Button.dialog-action must follow Button.flat-action.");
        Assert.True(
            dialogActionIndex > FindStyleIndex(styles, "Button.danger-action"),
            "Button.dialog-action must follow Button.danger-action.");
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
                    "danger-action"))
            .ToArray();

        // ADR-017：向导"上一步"改用 wizard-action tonal 族离开本计数；
        // 向导"下一步/完成"仍为 primary-action + dialog-action，继续受本契约约束。
        Assert.Equal(28, actionButtons.Length);
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
                            "{StaticResource Launcher.Icon.Sm}",
                            icon.Attribute("Width")?.Value);
                        Assert.Equal(
                            "{StaticResource Launcher.Icon.Sm}",
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

    private static int FindStyleIndex(IReadOnlyList<XElement> styles, string selector) =>
        styles
            .Select((element, index) => (element, index))
            .Single(item => item.element.Attribute("Selector")?.Value == selector)
            .index;

    private static string ProjectFile(string relativePath) =>
        Path.Combine(TestLocalizationHelper.FindProjectRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));


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
