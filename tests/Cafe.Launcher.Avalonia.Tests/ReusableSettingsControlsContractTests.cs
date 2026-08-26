using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

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
            .Select(file => XDocument.Load(ProjectFile($"Views/{file}")))
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

        // The appearance page retains one custom row because it has a color swatch
        // prefix; every plain option row is represented by SettingSelect.
        Assert.Single(directSettingCombos);
        Assert.Contains(
            directSettingCombos[0].Ancestors(),
            element => element.Name.LocalName == "StackPanel"
                && element.Descendants().Any(descendant => descendant.Name.LocalName == "Border"));
    }

    [Fact]
    public void SettingSelect_ProvidesTypedOptionTemplateAndTwoWaySelection()
    {
        var document = XDocument.Load(ProjectFile("Controls/SettingSelect.axaml"));
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

    private static string ProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectFile = Path.Combine(
                directory.FullName,
                "src",
                "Cafe.Launcher.Avalonia",
                "Cafe.Launcher.Avalonia.csproj");
            if (File.Exists(projectFile))
            {
                return Path.Combine(
                    Path.GetDirectoryName(projectFile)!,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The application project was not found.");
    }
}
