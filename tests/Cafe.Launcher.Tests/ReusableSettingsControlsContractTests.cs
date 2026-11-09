using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class ReusableSettingsControlsContractTests
{
    private static readonly string[] SettingsSections =
    [
        "SettingsGeneralSection.axaml",
        "SettingsGameSection.axaml",
        "SettingsDownloadNetworkSection.axaml",
        "SettingsAppearanceSection.axaml",
        "SettingsAdvancedSection.axaml"
    ];

    [Fact]
    public void SettingsSections_UseSettingSelectForSimpleOptionRows()
    {
        var documents = SettingsSections
            .Select(file => XDocument.Load(TestRepository.FromPresentationRoot($"Views/{file}")))
            .ToArray();

        Assert.All(
            documents,
            document => Assert.Contains(
                document.Descendants(),
                element => element.Name.LocalName == "SettingSelect"));

        var directSettingCombos = documents
            .SelectMany(document => document.Descendants())
            .Where(element => element.Name.LocalName == "ComboBox")
            .Where(element => element.Attribute("Classes")?.Value == "setting-control")
            .ToArray();

        // The appearance page keeps one direct ComboBox for the four theme-color sources,
        // whose items need a resolved-color dot; shorter enumerations use segmented buttons.
        var themeColorCombo = Assert.Single(directSettingCombos);
        Assert.Equal(
            "{Binding Code, DataType={x:Type models:SelectableOption}}",
            themeColorCombo.Attribute("SelectedValueBinding")?.Value);
        Assert.Contains(
            themeColorCombo.Descendants(),
            element => element.Name.LocalName == "DataTemplate"
                && element.Attribute(XName.Get("DataType", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "models:ThemeColorOption");
        Assert.Contains(
            documents.SelectMany(document => document.Descendants()),
            element => element.Name.LocalName == "RadioButton"
                && element.Attribute("Classes")?.Value == "segment-option");
    }

    [Fact]
    public void SettingSelect_ProvidesTypedOptionTemplateAndTwoWaySelection()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Controls/SettingSelect.axaml"));
        var comboBox = document
            .Descendants()
            .Single(element => element.Name.LocalName == "ComboBox");

        Assert.Equal(
            "{Binding SelectedValue, ElementName=Root, Mode=TwoWay}",
            comboBox.Attribute("SelectedValue")?.Value);
        Assert.Equal(
            "{Binding Code, DataType={x:Type models:SelectableOption}}",
            comboBox.Attribute("SelectedValueBinding")?.Value);
        Assert.Equal(
            "models:SelectableOption",
            comboBox
                .Descendants()
                .Single(element => element.Name.LocalName == "DataTemplate")
                .Attribute(XName.Get("DataType", "http://schemas.microsoft.com/winfx/2006/xaml"))
                ?.Value);
    }
}
