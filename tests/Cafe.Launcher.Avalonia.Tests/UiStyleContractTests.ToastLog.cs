using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

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
        var values = TestLocalizationHelper.ReadResx(ProjectFile($"Resources/{resxFile}"));
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
        var logViewer = File.ReadAllText(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
        var settings = File.ReadAllText(ProjectFile("Views/MainWindowSettingsOverlay.axaml"));

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
    public void LogViewer_EmptyStateUsesExplicitViewModelStateAndContainer()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
        var emptyState = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "log-empty-state"));

        Assert.Equal(
            "{Binding LogViewer.IsEmpty}",
            emptyState.Attribute("IsVisible")?.Value);
        Assert.Contains(
            emptyState.Descendants(),
            element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.I18n[logNoMatchingEntries]}");

        var styles = XDocument.Load(ProjectFile("Views/Styles/Diagnostics.axaml"));
        var emptyStateTextStyle = styles
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value
                    == "Border.log-empty-state TextBlock.media-placeholder-text");
        Assert.Contains(
            emptyStateTextStyle.Elements(),
            element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "Foreground"
                && element.Attribute("Value")?.Value
                    == "{DynamicResource Launcher.Text.Secondary}");
    }

    [Fact]
    public void LogViewer_UsesStableHeightAndBoundedWidth()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
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
        var document = XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
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
        var toastOverlay = File.ReadAllText(ProjectFile("Views/MainWindowToastOverlay.axaml"));

        Assert.Contains(
            "ZIndex=\"{x:Static constants:LauncherConstants.ZIndexToast}\"",
            toastOverlay,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ZIndex=\"1000\"", toastOverlay, StringComparison.Ordinal);
    }

    [Fact]
    public void ToastHost_AllowsHitTestingSoDismissButtonCanReceiveClicks()
    {
        var document = XDocument.Load(ProjectFile("Views/Styles/Toast.axaml"));

        Assert.Equal(
            "True",
            GetStyleSetters(document, "Grid.toast-host")["IsHitTestVisible"]);
        Assert.Equal("1", GetStyleSetters(document, "Button.toast-close")["Opacity"]);
    }

    [Fact]
    public void LogViewer_FilterControlsShareHeightAndSingleBottomGap()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
        var filterButtons = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "log-filter"))
            .ToList();
        Assert.Equal(7, filterButtons.Count);

        var search = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBox"
                && HasClass(element, "log-search"));
        Assert.Equal(
            "{StaticResource Launcher.Control.Height.Setting}",
            search.Attribute("Height")?.Value);

        var styles = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        Assert.Equal(
            "{StaticResource Launcher.Control.Height.Setting}",
            GetStyleSetters(styles, "Button.filter-tab.log-filter")["Height"]);
        Assert.Equal(
            "{StaticResource Launcher.Component.LogViewer.FilterBar.Margin}",
            GetStyleSetters(styles, "StackPanel.log-filter-bar")["Margin"]);

        var app = XDocument.Load(ProjectFile("App.axaml"));
        var xKey = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key";
        string TokenValue(string key) => app
            .Descendants()
            .Single(element => (string?)element.Attribute(xKey) == key)
            .Value
            .Trim();
        var filterMargin = TokenValue("Launcher.Component.LogViewer.FilterBar.Margin").Split(',');
        var bodyPadding = TokenValue("Launcher.Component.Dialog.Panel.Body.Padding").Split(',');
        Assert.Equal(bodyPadding[0], filterMargin[0]);
        Assert.Equal(bodyPadding[2], filterMargin[2]);
    }

    [Fact]
    public void LogViewer_ContentUsesSingleDialogBodyInset()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowLogViewerOverlay.axaml"));
        var loadEarlier = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "log-load-earlier"));
        Assert.Equal(
            "{StaticResource Launcher.Component.Dialog.Content.BottomMargin}",
            loadEarlier.Attribute("Margin")?.Value);

        var styles = XDocument.Load(ProjectFile("Views/Styles/Diagnostics.axaml"));
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
        var debugOverlay = File.ReadAllText(ProjectFile("Views/MainWindowDebugOverlay.axaml"));
        var toastStyles = File.ReadAllText(ProjectFile("Views/Styles/Toast.axaml"));

        Assert.DoesNotContain("Width=\"720\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Height=\"540\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"300\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"110\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight=\"160\"", debugOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight\" Value=\"30\"", toastStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("Padding\" Value=\"12,8\"", toastStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void ToastCards_DoNotUseOverlappingBoxShadows()
    {
        var document = XDocument.Load(ProjectFile("Views/Styles/Toast.axaml"));
        var toastCardStyle = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Border.toast-card");

        Assert.DoesNotContain(
            toastCardStyle.Elements(),
            element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "BoxShadow");
    }

    [Fact]
    public void ToastActions_UseTitleAlignedGridAndPrimaryFirstLeftAlignedLayout()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowToastOverlay.axaml"));
        var layout = document.Descendants().Single(element => HasClass(element, "toast-layout"));
        Assert.Equal("Auto,*,Auto", layout.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("Auto,Auto,Auto", layout.Attribute("RowDefinitions")?.Value);

        var title = document.Descendants().Single(element => HasClass(element, "toast-title"));
        Assert.Equal("{Binding Title}", title.Attribute("Text")?.Value);
        Assert.Equal("0", title.Attribute("Grid.Row")?.Value);
        Assert.Equal("1", title.Attribute("Grid.Column")?.Value);

        var icon = document.Descendants().Single(element => HasClass(element, "toast-icon"));
        Assert.Equal("0", icon.Attribute("Grid.Row")?.Value);
        Assert.Equal("0", icon.Attribute("Grid.Column")?.Value);
        Assert.Equal("Center", icon.Attribute("VerticalAlignment")?.Value);
        Assert.Null(icon.Attribute("Margin"));
        Assert.DoesNotContain(document.Descendants(), element => HasClass(element, "toast-rail"));

        var actions = document.Descendants().Single(element => HasClass(element, "toast-actions"));
        Assert.Equal("2", actions.Attribute("Grid.Row")?.Value);
        Assert.Equal("1", actions.Attribute("Grid.Column")?.Value);
        Assert.Equal("Left", actions.Attribute("HorizontalAlignment")?.Value);

        var actionButtons = actions.Elements().Where(element => element.Name.LocalName == "Button").ToArray();
        Assert.Equal(2, actionButtons.Length);
        Assert.True(HasClass(actionButtons[0], "toast-primary-action"));
        Assert.True(HasClass(actionButtons[1], "toast-secondary-action"));
        Assert.Equal(
            "{Binding #ToastOverlayRoot.((vm:MainWindowViewModel)DataContext).Toasts.ExecutePrimaryToastActionCommand}",
            actionButtons[0].Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding #ToastOverlayRoot.((vm:MainWindowViewModel)DataContext).Toasts.ExecuteSecondaryToastActionCommand}",
            actionButtons[1].Attribute("Command")?.Value);
        Assert.Equal("{Binding Id}", actionButtons[0].Attribute("CommandParameter")?.Value);
        Assert.Equal("{Binding Id}", actionButtons[1].Attribute("CommandParameter")?.Value);
        Assert.Equal("{Binding PrimaryActionLabel}", actionButtons[0].Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("{Binding SecondaryActionLabel}", actionButtons[1].Attribute("AutomationProperties.Name")?.Value);

        var styles = XDocument.Load(ProjectFile("Views/Styles/Toast.axaml"));
        var titleStyle = styles.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "TextBlock.toast-title");
        Assert.Contains(titleStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "FontSize"
            && element.Attribute("Value")?.Value == "{StaticResource Launcher.Typography.FontSize.Body.Md}");
        // toast-title no longer sets FontWeight (removed to match the lighter title + button styling).
    }

    [Fact]
    public void ToastProgress_ShowsOnlyActionExecutingIndeterminateBar()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowToastOverlay.axaml"));
        var progressElements = document.Descendants()
            .Where(element => HasClass(element, "toast-progress")).ToArray();
        Assert.Single(progressElements);

        var actionExecuting = progressElements[0];
        Assert.Equal("1", actionExecuting.Attribute("Grid.Row")?.Value);
        Assert.Equal("{Binding IsActionExecuting}", actionExecuting.Attribute("IsVisible")?.Value);
        Assert.Equal("True", actionExecuting.Attribute("IsIndeterminate")?.Value);

        var styles = XDocument.Load(ProjectFile("Views/Styles/Toast.axaml"));
        var progressStyle = styles.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "ProgressBar.toast-progress");
        Assert.Contains(progressStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Height"
            && element.Attribute("Value")?.Value
                == "{StaticResource Launcher.Component.Toast.Action.Progress.Height}");
        Assert.DoesNotContain(progressStyle.Descendants(), element =>
            element.Name.LocalName == "DoubleTransition"
            && element.Attribute("Property")?.Value == "Value");

        var toastCardStyle = styles.Descendants().Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "Border.toast-card");
        foreach (var property in new[] { "MinWidth", "MaxWidth" })
        {
            Assert.Contains(toastCardStyle.Elements(), element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == property
                && element.Attribute("Value")?.Value == "{StaticResource Launcher.Component.Toast.Width}");
        }
    }

    [Fact]
    public void DebugPanel_ProvidesLocalizedActionToastEntry()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindowDebugOverlay.axaml"));
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
