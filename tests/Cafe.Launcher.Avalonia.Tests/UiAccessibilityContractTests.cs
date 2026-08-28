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

        Assert.Equal(26, samples.Length);
        Assert.All(samples, sample => Assert.Equal("False", sample.Attribute("IsTabStop")?.Value));
    }

    [Fact]
    public void ModalSurfaces_ExposeLocalizedAutomationNames()
    {
        // §8（2026-08-28 审计修复）：DialogSurface 表面名 = 标题文本；
        // 无标题外壳（设置/向导）由使用方显式给名；Toast 宿主命名并声明 live region。
        var dialogTheme = XDocument.Load(ProjectFile("Views/Styles/DialogSurface.axaml"));
        var surfaceStyle = dialogTheme
            .Descendants(Avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "controls|DialogSurface");
        Assert.Equal(
            "{Binding Title, RelativeSource={RelativeSource Self}}",
            surfaceStyle.Elements(Avalonia + "Setter")
                .Single(setter => setter.Attribute("Property")?.Value == "AutomationProperties.Name")
                .Attribute("Value")?.Value);

        var settings = XDocument.Load(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));
        var settingsSurface = settings
            .Descendants()
            .Single(element => element.Name.LocalName == "DialogSurface");
        Assert.Equal(
            "{Binding Shell.I18n[settings]}",
            settingsSurface.Attribute("AutomationProperties.Name")?.Value);

        var wizard = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
        var wizardSurface = wizard
            .Descendants()
            .Single(element => element.Name.LocalName == "DialogSurface");
        Assert.Equal(
            "{Binding Shell.I18n[setupWizardStepTitle]}",
            wizardSurface.Attribute("AutomationProperties.Name")?.Value);

        var toast = XDocument.Load(ProjectFile("Views/MainWindowToastOverlay.axaml"));
        var toastHost = toast
            .Descendants(Avalonia + "Grid")
            .Single(element => HasClass(element, "toast-host"));
        Assert.Equal(
            "{Binding Shell.I18n[toastRegionName]}",
            toastHost.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("Polite", toastHost.Attribute("AutomationProperties.LiveSetting")?.Value);
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
