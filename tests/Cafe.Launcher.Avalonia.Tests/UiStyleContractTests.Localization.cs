using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

// Localization contracts: resource-key bindings for debug overlays and the
// fixed-English-literal scanner over user-facing attributes.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void LocalizedTextCatalog_DebugBindings_UseResourceKeys()
    {
        var overlay = XDocument.Load(ProjectFile("Views/MainWindowDebugOverlay.axaml"));
        var source = File.ReadAllText(ProjectFile("Services/LocalizationService.cs"));
        var debugProperties = overlay
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .SelectMany(value => Regex.Matches(value, @"Shell\.I18n\[(debug[A-Za-z0-9_]+)\]"))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(debugProperties);
        foreach (var key in debugProperties)
        {
            Assert.Contains($"this[string key] => localizer.T(key)", source, StringComparison.Ordinal);
            Assert.StartsWith("debug", key, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ViewsAndControls_UserFacingTextHasNoFixedEnglishLiterals()
    {
        var violations = Directory
            .GetFiles(ProjectFile("Views"), "*.axaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(ProjectFile("Controls"), "*.axaml", SearchOption.AllDirectories))
            .SelectMany(path => FindFixedEnglishLiterals(
                XDocument.Load(path, LoadOptions.SetLineInfo),
                Path.GetRelativePath(TestLocalizationHelper.FindProjectRoot(), path)))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("OnContent")]
    [InlineData("OffContent")]
    [InlineData("AutomationProperties.Name")]
    [InlineData("Description")]
    [InlineData("Message")]
    [InlineData("CancelText")]
    [InlineData("ConfirmText")]
    [InlineData("CloseToolTip")]
    public void FixedEnglishScanner_UserFacingAttributeLiteral_IsReported(string attributeName)
    {
        var document = XDocument.Parse(
            $"<Control xmlns=\"https://github.com/avaloniaui\" {attributeName}=\"Hardcoded English\" />",
            LoadOptions.SetLineInfo);

        var violation = Assert.Single(FindFixedEnglishLiterals(document, "fixture.axaml"));
        Assert.Contains($"{attributeName}=\"Hardcoded English\"", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void FixedEnglishScanner_BindingsAndDesignNamespacePreview_AreIgnored()
    {
        var document = XDocument.Parse(
            """
            <Panel xmlns="https://github.com/avaloniaui"
                   xmlns:d="http://schemas.microsoft.com/expression/blend/2008">
                <SettingRow Title="{Binding LocalizedTitle}"
                            Description="{Binding LocalizedDescription}" />
                <TextBlock d:Text="English design preview" />
                <d:Preview Text="English preview text"
                           Title="English preview title" />
            </Panel>
            """,
            LoadOptions.SetLineInfo);

        Assert.Empty(FindFixedEnglishLiterals(document, "fixture.axaml"));
    }
}
