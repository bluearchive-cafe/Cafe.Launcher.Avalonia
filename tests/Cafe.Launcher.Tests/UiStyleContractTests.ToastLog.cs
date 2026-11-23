using System.Xml.Linq;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

// Toast and log viewer contracts: overlay z-index, hit testing, card layout,
// progress reporting, and the diagnostics log viewer surface.
public sealed partial class UiStyleContractTests
{
    [Theory]
    [InlineData("LauncherStrings.resx")]
    [InlineData("LauncherStrings.ja.resx")]
    [InlineData("LauncherStrings.zh-Hans.resx")]
    [InlineData("LauncherStrings.zh-Hant.resx")]
    public void LogSeverityNames_MatchBetweenViewerFiltersAndSettings(string resxFile)
    {
        var values = TestLocalizationHelper.ReadResx(TestRepository.FromPresentationRoot($"Resources/{resxFile}"));
        Dictionary<string, string> matchingKeys = new(StringComparer.Ordinal)
        {
            ["logFilterVerbose"] = "logLevelVerbose",
            ["logFilterDebug"] = "logLevelDebug",
            ["logFilterInfo"] = "logLevelInformation",
            ["logFilterWarn"] = "logLevelWarning",
            ["logFilterError"] = "logLevelError",
            ["logFilterFatal"] = "logLevelFatal"
        };

        foreach (var (filterKey, settingKey) in matchingKeys)
        {
            Assert.Equal(values[settingKey], values[filterKey]);
        }
    }

    [Fact]
    public void LogViewer_UserFacingTextUsesLocalizationBindings()
    {
        var logViewer = File.ReadAllText(TestRepository.FromPresentationRoot("Views/MainWindowLogViewerOverlay.axaml"));
        var settings = File.ReadAllText(TestRepository.FromPresentationRoot("Views/MainWindowSettingsOverlay.axaml"));

        foreach (var literal in new[]
                 {
                     "Log Viewer",
                     "Search...",
                     "No matching log entries.",
                     "Export Logs",
                     "View Log",
                     "Open Data Directory"
                 })
        {
            Assert.DoesNotContain($"Text=\"{literal}\"", logViewer, StringComparison.Ordinal);
            Assert.DoesNotContain($"Text=\"{literal}\"", settings, StringComparison.Ordinal);
            Assert.DoesNotContain($"PlaceholderText=\"{literal}\"", logViewer, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LogViewer_UsesStableHeightAndBoundedWidth()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowLogViewerOverlay.axaml"));
        var dialog = document
            .Descendants()
            .Single(element => element.Name.LocalName == "DialogSurface");

        Assert.Equal(
            "{StaticResource Launcher.Layout.LogViewer.Width}",
            dialog.Attribute("MaxWidth")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Layout.LogViewer.Height}",
            dialog.Attribute("MaxHeight")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Layout.LogViewer.Height}",
            dialog.Attribute("Height")?.Value);
        Assert.Null(dialog.Attribute("Width"));

        // 过滤栏固定于头带之下：工具行插槽必须由 Toolbar 属性承载。
        Assert.Contains(
            dialog.Elements(),
            element => element.Name.LocalName == "DialogSurface.Toolbar");
    }

    [Fact]
    public void LogViewer_UsesVirtualizedListBox()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowLogViewerOverlay.axaml"));
        var list = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ListBox"
                && HasClass(element, "log-entry-list"));

        Assert.Equal(
            "{Binding LogViewer.FilteredEntries}",
            list.Attribute("ItemsSource")?.Value);
        Assert.DoesNotContain(
            list.Elements(),
            element => element.Name.LocalName == "ListBox.ItemsPanel");
    }

    [Fact]
    public void ToastLayer_UsesLauncherConstantsZIndex()
    {
        var mainWindow = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.axaml"));
        var toast = mainWindow
            .Descendants()
            .Single(element => element.Name.LocalName == "MainWindowToastOverlay");

        // 层级声明在 MainWindow 的兄弟节点上；写在 Toast 覆盖层自己的根 Grid 里不会生效。
        Assert.Equal(
            "{x:Static constants:LauncherConstants.ZIndexToast}",
            toast.Attribute("ZIndex")?.Value);

        var toastOverlay = File.ReadAllText(TestRepository.FromPresentationRoot("Views/MainWindowToastOverlay.axaml"));
        Assert.DoesNotContain("ZIndex=", toastOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("ZIndex=\"1000\"", mainWindow.Root!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToastHost_AllowsHitTestingSoDismissButtonCanReceiveClicks()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/Toast.axaml"));

        Assert.Equal(
            "True",
            GetStyleSetters(document, "Grid.toast-host")["IsHitTestVisible"]);
        var view = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowToastOverlay.axaml"));
        var toast = Assert.Single(view.Descendants(), element => element.Name.LocalName == "FAInfoBar");
        Assert.Contains("DismissToastCommand", (string?)toast.Attribute("CloseButtonCommand"), StringComparison.Ordinal);
    }

    [Fact]
    public void LogViewer_ContentUsesSingleDialogBodyInset()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowLogViewerOverlay.axaml"));
        var loadEarlier = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "log-load-earlier"));
        Assert.Equal(
            "{StaticResource Launcher.Component.Dialog.Content.BottomMargin}",
            loadEarlier.Attribute("Margin")?.Value);

        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/Diagnostics.axaml"));
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.None}",
            GetStyleSetters(styles, "ListBox.log-entry-list")["Margin"]);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Thickness.None}",
            GetStyleSetters(styles, "Border.log-empty-state")["Margin"]);
    }

    [Fact]
    public void ToastAndDebugOverlay_NewMeasurements_UseLauncherTokens()
    {
        var debugOverlay = File.ReadAllText(TestRepository.FromPresentationRoot("Views/MainWindowDebugOverlay.axaml"));
        var toastStyles = File.ReadAllText(TestRepository.FromPresentationRoot("Views/Styles/Toast.axaml"));

        Assert.DoesNotContain("Width=\"720\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Height=\"540\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"300\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"110\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight=\"160\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight\" Value=\"30\"", toastStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("Padding\" Value=\"12,8\"", toastStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void ToastCards_UseNeutralSurfaceWithSeverityAccentBar()
    {
        var toastStyles = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/Toast.axaml"));

        // 外壳现在是中性描边 + 中性卡片底色：严重级别不再铺满整卡换色（ADR-070 2026-11-21 修订）。
        // 底色与资讯级 InfoBar 同源，左缘语义条与信息区因此落在同一表面上。
        var toastCardStyle = toastStyles
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Border.toast-card");
        var cardSetters = toastCardStyle
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")?.Value);
        Assert.Equal("{DynamicResource InfoBarInformationalSeverityBackgroundBrush}", cardSetters["Background"]);
        Assert.Equal("{DynamicResource DividerStrokeColorDefaultBrush}", cardSetters["BorderBrush"]);
        Assert.Equal("{StaticResource Launcher.Border.Thickness.Default}", cardSetters["BorderThickness"]);
        Assert.Equal("{StaticResource OverlayCornerRadius}", cardSetters["CornerRadius"]);

        // 四档严重级只染左缘语义条，卡片底色保持一致。
        var severityBars = new Dictionary<string, string>
        {
            ["toast-severity-informational"] = "Launcher.Color.Info",
            ["toast-severity-error"] = "Launcher.Color.Danger",
            ["toast-severity-warning"] = "Launcher.Color.Warning",
            ["toast-severity-success"] = "Launcher.Color.Success"
        };
        foreach (var (severityClass, resourceKey) in severityBars)
        {
            var style = Assert.Single(
                toastStyles.Descendants(),
                element =>
                    element.Name.LocalName == "Style"
                    && element.Attribute("Selector")?.Value
                        == $"Border.toast-card.{severityClass} Border.toast-accent-bar");
            var setters = style
                .Elements()
                .Where(element => element.Name.LocalName == "Setter")
                .ToDictionary(
                    element => element.Attribute("Property")!.Value,
                    element => element.Attribute("Value")?.Value);
            Assert.Equal($"{{DynamicResource {resourceKey}}}", setters["Background"]);
        }

        // 通知卡固定资讯级表面（中性），严重级别由外部语义条与本地化标题表达。
        var overlay = XDocument.Load(
            TestRepository.FromPresentationRoot("Views/MainWindowToastOverlay.axaml"));
        var infoBar = Assert.Single(overlay.Descendants(), element => element.Name.LocalName == "FAInfoBar");
        Assert.Equal("Informational", infoBar.Attribute("Severity")?.Value);
        Assert.Equal("False", infoBar.Attribute("IsIconVisible")?.Value);
        Assert.Single(overlay.Descendants(),
            element => element.Name.LocalName == "Border" && HasClass(element, "toast-accent-bar"));

        var app = XDocument.Load(TestRepository.InHost("App.axaml"));
        var infoBarBorderThickness = Assert.Single(
            app.Descendants(),
            element =>
                element.Name.LocalName == "Thickness"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "InfoBarBorderThickness"));
        Assert.Equal("0", infoBarBorderThickness.Value);
    }

    [Fact]
    public void ToastCards_DoNotUseBoxShadowInStyleSetters()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/Styles/Toast.axaml"));
        var toastCardStyle = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Border.toast-card");

        // Avalonia 12.1.1 无法在 Setter 里解析字符串型阴影资源，而魔法值被
        // BoxShadowSetters_ConsumeElevationTokensInsteadOfLiterals 拦截；
        // 通知卡因此只用 1 DIP 描边表达高度，不加阴影。
        Assert.DoesNotContain(
            toastCardStyle.Elements(),
            element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "BoxShadow");
    }

    [Fact]
    public void ToastProgress_ShowsOnlyActionExecutingIndeterminateBar()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowToastOverlay.axaml"));
        var progressElements = document.Descendants()
            .Where(element => HasClass(element, "toast-progress")).ToArray();
        Assert.Single(progressElements);

        var actionExecuting = progressElements[0];
        Assert.Equal("{Binding IsActionExecuting}", actionExecuting.Attribute("IsVisible")?.Value);
        Assert.Equal("True", actionExecuting.Attribute("IsIndeterminate")?.Value);
    }

    [Fact]
    public void DebugPanel_ProvidesLocalizedActionToastEntry()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowDebugOverlay.axaml"));
        var button = document.Descendants().Single(element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Command")?.Value == "{Binding Debug.TestActionToastCommand}");

        Assert.Equal(
            "{Binding Shell.I18n[debugTestActionToast]}",
            button.Attribute("AutomationProperties.Name")?.Value);
        var text = button.Descendants().Single(element => element.Name.LocalName == "TextBlock");
        Assert.Equal("{Binding Shell.I18n[debugTestActionToast]}", text.Attribute("Text")?.Value);
    }
}
