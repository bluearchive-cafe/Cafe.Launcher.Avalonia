using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

// Localization contracts: resource-key bindings for debug overlays and the
// fixed-English-literal scanner over user-facing attributes.
public sealed partial class UiStyleContractTests
{
    [Fact]
    public void LocalizedTextCatalog_DebugBindings_UseResourceKeys()
    {
        var overlay = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowDebugOverlay.axaml"));
        var localizer = new Cafe.Launcher.UI.Services.LocalizationService();
        using var catalog = new Cafe.Launcher.UI.Services.LocalizedTextCatalog(localizer);
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
            Assert.Equal(localizer.T(Cafe.Launcher.UI.Resources.LauncherResourceCatalog.Parse(key)), catalog[key]);
            Assert.StartsWith("debug", key, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ViewsAndControls_UserFacingTextHasNoFixedEnglishLiterals()
    {
        var violations = ProjectMarkupFiles()
            .SelectMany(relativePath => FindFixedEnglishLiterals(
                XDocument.Load(TestRepository.FromPresentationRoot(relativePath), LoadOptions.SetLineInfo),
                relativePath))
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

    [Fact]
    public void ProductionMarkup_DoesNotUseGlyphSeparators()
    {
        var violations = ProjectMarkupFiles()
            .SelectMany(relativePath => FindGlyphSeparators(
                XDocument.Load(TestRepository.FromPresentationRoot(relativePath), LoadOptions.SetLineInfo),
                relativePath))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("|")]
    [InlineData("·")]
    [InlineData("•")]
    [InlineData("・")]
    public void GlyphSeparatorScanner_StandaloneSeparatorText_IsReported(string glyph)
    {
        var document = XDocument.Parse(
            $"<TextBlock xmlns=\"https://github.com/avaloniaui\" Text=\"{glyph}\" />",
            LoadOptions.SetLineInfo);

        Assert.Single(FindGlyphSeparators(document, "fixture.axaml"));
    }

    [Fact]
    public void GlyphSeparatorScanner_BoundAndProseText_AreIgnored()
    {
        var document = XDocument.Parse(
            """
            <Panel xmlns="https://github.com/avaloniaui">
                <TextBlock Text="{Binding InstallDownloadSizeText}" />
                <TextBlock Text="Download 3.50 GB" />
                <TextBlock>Required 18.50 GB</TextBlock>
            </Panel>
            """,
            LoadOptions.SetLineInfo);

        Assert.Empty(FindGlyphSeparators(document, "fixture.axaml"));
    }
}
