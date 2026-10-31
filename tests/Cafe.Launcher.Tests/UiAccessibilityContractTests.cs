using System.Xml;
using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class UiAccessibilityContractTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    private static readonly HashSet<string> InteractiveControlNames = new(StringComparer.Ordinal)
    {
        "Button",
        "TextBox",
        "ComboBox",
        "CheckBox",
        "RadioButton",
        "ToggleSwitch",
        "Slider",
        "ListBox",
        "ListBoxItem",
        "TabControl",
        "TabItem",
        "MenuItem",
        "NumericUpDown",
        "DatePicker",
        "AutoCompleteBox",
        "ColorPicker",
        "ColorView"
    };

    [Fact]
    public void EveryInteractiveControl_ExposesANonEmptyAccessibleName()
    {
        // 全量扫描取代抽样：新增交互控件时漏名会在契约阶段失败，而不是等到无障碍审计。
        // 设计系统画廊的状态样本刻意不进入 Tab 序列（IsTabStop="False"），属于展示件。
        var inspected = 0;
        var failures = new List<string>();
        foreach (var file in EnumeratePresentationXaml())
        {
            var document = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var element in document.Descendants())
            {
                if (!InteractiveControlNames.Contains(element.Name.LocalName)
                    || element.Attribute("IsTabStop")?.Value == "False")
                {
                    continue;
                }

                inspected++;
                if (!string.IsNullOrWhiteSpace(element.Attribute("AutomationProperties.Name")?.Value))
                {
                    continue;
                }

                var line = ((IXmlLineInfo)element).LineNumber;
                failures.Add($"{Path.GetFileName(file)}:{line} <{element.Name.LocalName}>");
            }
        }

        Assert.True(inspected >= 150, $"Expected the sweep to cover the shipped control set, inspected {inspected}.");
        Assert.True(
            failures.Count == 0,
            "Interactive controls without AutomationProperties.Name:\n" + string.Join('\n', failures));
    }

    private static IEnumerable<string> EnumeratePresentationXaml()
    {
        foreach (var directory in new[]
                 {
                     TestRepository.FromPresentationRoot("Views"),
                     TestRepository.FromPresentationRoot("Controls")
                 })
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.axaml", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    [Fact]
    public void LogViewer_InteractiveControls_ExposeLocalizedAccessibleNames()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowLogViewerOverlay.axaml"));
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
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowDialogsOverlay.axaml"));
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
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/DesignGalleryOverlay.axaml"));
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
        var dialogTheme = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/DialogSurface.axaml"));
        var surfaceStyle = dialogTheme
            .Descendants(Avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "controls|DialogSurface");
        Assert.Equal(
            "{Binding Title, RelativeSource={RelativeSource Self}}",
            surfaceStyle.Elements(Avalonia + "Setter")
                .Single(setter => setter.Attribute("Property")?.Value == "AutomationProperties.Name")
                .Attribute("Value")?.Value);

        var settings = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowSettingsOverlay.axaml"));
        var settingsSurface = settings
            .Descendants()
            .Single(element => element.Name.LocalName == "DialogSurface");
        Assert.Equal(
            "{Binding Shell.I18n[settings]}",
            settingsSurface.Attribute("AutomationProperties.Name")?.Value);

        var wizard = XDocument.Load(TestRepository.FromPresentationRoot("Views/SetupWizardOverlay.axaml"));
        var wizardSurface = wizard
            .Descendants()
            .Single(element => element.Name.LocalName == "DialogSurface");
        Assert.Equal(
            "{Binding Shell.I18n[setupWizardStepTitle]}",
            wizardSurface.Attribute("AutomationProperties.Name")?.Value);

        var toast = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowToastOverlay.axaml"));
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
}
