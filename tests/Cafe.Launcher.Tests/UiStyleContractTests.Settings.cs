using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

// Settings workspace contracts: category sections, navigation list, surface
// blueprint, appearance/about sections, and transactional footer actions.
public sealed partial class UiStyleContractTests
{
    [Theory]
    [InlineData("SettingsDownloadNetworkSection",
        "settingsGroupDownload|settingsGroupNetwork",
        "downloadSpeedLimit|proxy|enableHttp2")]
    [InlineData("SettingsAdvancedSection",
        "settingsGroupDiagnostics|settingsGroupNetworkCompatibility|settingsGroupDataRecovery",
        "logLevel|logFiles|allowBlockedNetworkAddresses|settingsDataDirectory|debugResetSettingsConfirm")]
    public void SettingsGroups_KeepCommonTasksBeforeConnectionAndRecovery(string section, string groupKeys, string rowKeys)
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot($"Views/{section}.axaml"));
        var groups = document.Descendants().Where(element => HasClass(element, "settings-group")).ToArray();
        var expectedGroups = groupKeys.Split('|');
        Assert.Equal(expectedGroups.Length, groups.Length);
        Assert.Equal(expectedGroups.Select(key => $"{{Binding Shell.I18n[{key}]}}"),
            groups.Select(group => group.Elements().First().Attribute("Text")?.Value));
        // ADR-070 第二批：行收纳进分组卡片，行序仍按文档序锁定。
        Assert.Equal(rowKeys.Split('|').Select(key => $"{{Binding Shell.I18n[{key}]}}"),
            groups.SelectMany(group => group.Descendants().Where(element => element.Name.LocalName is "SettingRow" or "SettingSelect"))
                .Select(row => row.Attribute("Title")?.Value));
    }

    [Fact]
    public void SettingsSections_OwnEachBindingExactlyOnceAndUseCompiledBindings()
    {
        Dictionary<string, string[]> expectedBindings = new(StringComparer.Ordinal)
        {
            ["SettingsGeneralSection"] =
            [
                "Settings.Editor.Current.Language",
                "Settings.Editor.Current.CloseBehavior",
                "Settings.Editor.Current.AfterLaunchBehavior",
                "Settings.Editor.Current.StatusDetailMode",
                "Settings.Editor.Current.ShowRemoteContentCard",
                "Settings.Editor.Current.UpdateChannel",
                "Settings.Editor.Current.EnableStartupUpdateCheck",
                "Settings.Editor.Current.RememberWindowPositionAndSize"
            ],
            ["SettingsGameSection"] =
            [
                "Settings.GamePathDisplay",
                "Settings.ChooseGamePathCommand",
                "Operations.GameManagementPath",
                "ManageGameFromSettingsCommand"
            ],
            ["SettingsGameLaunchSection"] =
            [
                "Settings.Game.LaunchCheckMode",
                "Settings.Game.AfterLaunchBehavior",
                "Settings.Game.GameRuntime.Runner",
                "Settings.Game.GameRuntime.RunnerPath",
                "Settings.Game.GameRuntime.PrefixPath",
                "Settings.Game.GameRuntime.ProtonPath",
                "Settings.Game.GameRuntime.EnableMangoHud",
                "Settings.GameRuntime.ChoosePrefixPathCommand",
                "Settings.GameRuntime.ChooseProtonPathCommand",
                "Settings.GameRuntime.HasBuilds",
                "Settings.GameRuntime.Builds",
                "Settings.GameRuntime.SelectedBuild",
                "Settings.GameRuntime.BuildDiscoveryFailed",
                "Settings.GameRuntime.IsStatusRefreshing",
                "Settings.GameRuntime.RefreshStatusCommand",
                "Settings.GameRuntime.IsRunnerPathEnabled",
                "Settings.GameRuntime.StatusSummary"
            ],
            ["SettingsGameDownloadSection"] =
            [
                "Settings.Game.PatchUrlGroup"
            ],
            ["SettingsDownloadNetworkSection"] =
            [
                "Settings.Editor.Current.ProxyMode",
                "Settings.Editor.Current.DownloadSpeedLimit",
                "Settings.Editor.Current.EnableHttp2"
            ],
            ["SettingsAppearanceSection"] =
            [
                "Settings.Editor.Current.ThemeMode",
                "Settings.Editor.Current.MotionMode",
                "Settings.Editor.Current.ThemeColorMode",
                "Settings.Editor.Current.ThemeColorExtractionAlgorithm",
                "Settings.Editor.Current.ThemeColorVariant",
                "Settings.Editor.Current.NeutralColorStrategy",
                "Settings.Appearance.ThemeColorPaletteItems",
                "Settings.Appearance.SelectedCustomThemeColor",
                "Settings.Appearance.IsThemeColorPaletteVisible",
                "Settings.Appearance.IsThemeColorExtractionAlgorithmSettingsVisible",
                "Settings.Appearance.IsCustomThemeColorPickerVisible",
                "Settings.Appearance.IsSeedFollowingNeutralStrategySelected",
                "Settings.Appearance.RefreshThemeColorPaletteCommand",
                "Settings.Appearance.ThemeColorPreviewBrush"
            ],
            ["SettingsBackgroundSection"] =
            [
                "Settings.BackgroundEditor.BackgroundSource",
                "Settings.BackgroundEditor.BackgroundFit",
                "Settings.BackgroundEditor.BackgroundVideoMuted",
                "Settings.BackgroundEditor.BackgroundVideoVolume",
                "Settings.BackgroundEditor.CanEdit",
                "Settings.BackgroundEditor.UseBackgroundOverride",
                "Settings.Appearance.SelectedBackgroundFillColor",
                "Settings.Appearance.BackgroundFillColorPreviewBrush",
                "Settings.Appearance.IsBackgroundFillColorVisible",
                "Settings.Appearance.IsCustomBackgroundSettingsVisible",
                "Settings.ChooseBackgroundFileCommand",
                "Settings.ChooseBackgroundFolderCommand",
                "Settings.ClearBackgroundCommand"
            ],
            ["SettingsAdvancedSection"] =
            [
                "Settings.Editor.Current.LogLevel",
                "Settings.Editor.Current.AllowBlockedNetworkAddresses",
                "LogViewer.OpenCommand",
                "LogExport.OpenCommand",
                "WindowChrome.OpenDataDirectoryCommand",
                "Settings.RequestResetSettingsCommand"
            ],
            ["SettingsAboutSection"] =
            [
                "Settings.CheckForUpdatesCommand",
                "WindowChrome.OpenAboutOfficialSiteCommand",
                "WindowChrome.OpenHelpDocsCommand",
                "WindowChrome.OpenGitHubRepositoryCommand",
                "WindowChrome.OpenGitHubReleaseRepositoryCommand",
                "WindowChrome.OpenIssueTrackerCommand",
                "WindowChrome.OpenPrivacyPolicyCommand",
                "WindowChrome.OpenDefaultBackgroundArtworkCommand"
            ]
        };

        var allSectionText = string.Join(
            Environment.NewLine,
            expectedBindings.Keys.Select(name => File.ReadAllText(TestRepository.FromPresentationRoot($"Views/{name}.axaml"))));

        // 分段按钮让同一个草稿属性在组内每个段上各出现一次；分组卡片（ADR-070 第二批）
        // 的条件分隔线与行共用同一可见性绑定，也各计一次；除此之外仍要求每个绑定
        // 只由一个控件拥有，避免同一设置出现两行。
        Dictionary<string, int> segmentedBindings = new(StringComparer.Ordinal)
        {
            ["Settings.Editor.Current.CloseBehavior"] = 2,
            ["Settings.Editor.Current.StatusDetailMode"] = 2,
            ["Settings.Editor.Current.UpdateChannel"] = 2,
            ["Settings.Editor.Current.ThemeMode"] = 3,
            // Runner 是 RunnerPath 的前缀，运行器选择与路径输入各命中一次。
            ["Settings.Game.GameRuntime.Runner"] = 2,
            ["Settings.Game.PatchUrlGroup"] = 2,
            ["Settings.BackgroundEditor.BackgroundSource"] = 3,
            ["Settings.BackgroundEditor.BackgroundFit"] = 3,
            // 音量滑杆与百分比读数是同一设置的两个呈现，仍归一行。
            ["Settings.BackgroundEditor.BackgroundVideoVolume"] = 2,
            ["Settings.Editor.Current.NeutralColorStrategy"] = 2,
            // 外观/背景/运行环境里条件行与它前方的条件分隔线成对出现。
            ["Settings.Appearance.IsThemeColorPaletteVisible"] = 2,
            ["Settings.Appearance.IsCustomThemeColorPickerVisible"] = 2,
            ["Settings.Appearance.IsCustomBackgroundSettingsVisible"] = 2,
            ["Settings.GameRuntime.HasBuilds"] = 2,
            // 游戏分区拆分后，管理命令同时出现在「游戏文件」与「游戏下载」两页。
            ["ManageGameFromSettingsCommand"] = 7
        };

        foreach (var (sectionName, bindings) in expectedBindings)
        {
            var text = File.ReadAllText(TestRepository.FromPresentationRoot($"Views/{sectionName}.axaml"));
            Assert.Contains("x:DataType=\"vm:MainWindowViewModel\"", text, StringComparison.Ordinal);

            foreach (var binding in bindings)
            {
                Assert.Contains(binding, text, StringComparison.Ordinal);
                Assert.Equal(
                    segmentedBindings.GetValueOrDefault(binding, 1),
                    Regex.Count(allSectionText, Regex.Escape(binding), RegexOptions.CultureInvariant));
            }
        }
    }

    [Fact]
    public void SettingsOverlay_ReferencesCategorySectionsWithoutOwningSettingsRows()
    {
        var overlay = File.ReadAllText(TestRepository.FromPresentationRoot("Views/MainWindowSettingsOverlay.axaml"));
        var document = XDocument.Parse(overlay);
        Dictionary<string, string> sectionVisibility = new(StringComparer.Ordinal)
        {
            ["SettingsGeneralSection"] = "Settings.IsGeneralCategorySelected",
            ["SettingsGameSection"] = "Settings.IsGameCategorySelected",
            ["SettingsGameLaunchSection"] = "Settings.IsGameLaunchCategorySelected",
            ["SettingsGameDownloadSection"] = "Settings.IsGameDownloadCategorySelected",
            ["SettingsDownloadNetworkSection"] = "Settings.IsDownloadNetworkCategorySelected",
            // 外观页同时承载默认背景与游戏背景两个分类，三者共用同一整页视图。
            ["SettingsAppearanceSection"] = "Settings.IsBackgroundEditorCategorySelected",
            ["SettingsAdvancedSection"] = "Settings.IsAdvancedCategorySelected",
            ["SettingsAboutSection"] = "Settings.IsAboutCategorySelected"
        };

        foreach (var (sectionName, visibility) in sectionVisibility)
        {
            Assert.Single(
                document.Descendants(),
                element =>
                    element.Name.LocalName == sectionName
                    && element.Attribute("IsVisible")?.Value == $"{{Binding {visibility}}}");
        }

        Assert.DoesNotContain("Settings.Editor.Current", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("settings-row", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsOverlay_UsesResponsiveTwoColumnCategoryWorkspace()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowSettingsOverlay.axaml"));
        // ADR-070 第二批：外壳是标题栏以下、图标栏右侧的整窗页面，不再是 DialogSurface 浮层。
        var pageRoot = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "settings-overlay"));
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Page.Offset}",
            pageRoot.Attribute("Margin")?.Value);
        Assert.Equal(
            "{Binding ModalHost.IsSettingsInteractive}",
            pageRoot.Attribute("IsEnabled")?.Value);
        Assert.Equal(
            "{Binding WindowChrome.IsSettingsVisible}",
            pageRoot.Attributes().Single(attribute => attribute.Name.LocalName == "MotionVisibility.IsOpen").Value);
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "DialogSurface");

        var workspace = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "settings-workspace"));
        Assert.Equal("Auto,*", workspace.Attribute("ColumnDefinitions")?.Value);

        var navigationPane = workspace
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "settings-navigation-pane"));
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Navigation.Width}",
            navigationPane.Attribute("Width")?.Value);

        var navigation = workspace
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ListBox"
                && HasClass(element, "settings-navigation"));
        Assert.Equal(
            "{Binding Settings.ActiveCategories}",
            navigation.Attribute("ItemsSource")?.Value);
        Assert.Equal(
            "{Binding Settings.SelectedCategoryOption, Mode=TwoWay}",
            navigation.Attribute("SelectedItem")?.Value);
        Assert.Null(navigation.Attribute("FocusAdorner"));
        Assert.Equal(
            "Auto",
            navigation.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value);
        Assert.Equal(
            "Disabled",
            navigation.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value);
        Assert.Equal(
            "{Binding Settings.IsSaving, Converter={x:Static BoolConverters.Not}}",
            navigation.Attribute("IsEnabled")?.Value);

        var itemTemplate = navigation
            .Descendants()
            .Single(element => element.Name.LocalName == "DataTemplate");
        var categoryName = itemTemplate
            .Descendants()
            .Single(element => element.Name.LocalName == "TextBlock");
        Assert.Equal("{Binding DisplayName}", categoryName.Attribute("Text")?.Value);
        Assert.Equal(
            "{Binding DisplayName}",
            categoryName.Attributes()
                .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                .Value);

        var content = workspace
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "settings-content"));
        var categoryTitle = content
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock"
                && HasClass(element, "category-title"));
        var categoryHeadingCopy = content
            .Descendants()
            .Single(element => HasClass(element, "settings-content-heading-copy"));
        Assert.Equal(
            "{Binding Settings.SelectedCategoryOption}",
            categoryHeadingCopy.Attribute("DataContext")?.Value);
        Assert.Equal(
            "models:SettingOption",
            categoryHeadingCopy.Attributes()
                .Single(attribute => attribute.Name.LocalName == "DataType")
                .Value);
        Assert.Equal(
            "{Binding DisplayName}",
            categoryTitle.Attribute("Text")?.Value);
        Assert.Equal(
            "{Binding DisplayName}",
            categoryTitle.Attributes()
                .Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                .Value);
        Assert.DoesNotContain(document.Descendants(), element => HasClass(element, "category-subtitle"));
        Assert.Single(
            content.Descendants(),
            element => element.Name.LocalName == "ScrollViewer");
        Assert.DoesNotContain(
            content.Descendants(),
            element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "settings-status-summary"));
    }

    [Fact]
    public void SettingsOverlay_KeepsScopeHeaderAboveCategoryNavigation()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowSettingsOverlay.axaml"));
        // ADR-070 第二批：整窗页由根网格承担显隐与入场动效；表面不再引用 DialogSurface。
        var pageRoot = document.Descendants().Single(element =>
            element.Name.LocalName == "Grid" && HasClass(element, "settings-overlay"));
        Assert.Equal(
            "{Binding IsMotionEnabled}",
            pageRoot.Attribute("Classes.motion-enabled")?.Value);
        Assert.Equal(
            "{Binding WindowChrome.IsSettingsVisible}",
            pageRoot.Attributes().Single(attribute => attribute.Name.LocalName == "MotionVisibility.IsOpen").Value);
        Assert.Equal(
            "{Binding IsMotionEnabled}",
            pageRoot.Attributes().Single(attribute => attribute.Name.LocalName == "MotionVisibility.IsMotionEnabled").Value);

        // 导航只有一枚字形：选中态靠行底色与加粗文字表达，不像 Material 那样切换描边/填充两枚图标。
        var navigationIcon = document
            .Descendants()
            .Single(element => element.Name.LocalName == "FluentIcon" && element.Attribute("Glyph")?.Value == "{Binding CategoryGlyph}");
        Assert.Equal("{StaticResource Launcher.Icon.Md}", navigationIcon.Attribute("FontSize")?.Value);

        var header = document.Descendants().Single(element => HasClass(element, "settings-header"));
        Assert.Equal("2", header.Attribute("Grid.ColumnSpan")?.Value);
        Assert.Single(header.Descendants(), element =>
            element.Attribute("Text")?.Value == "{Binding Settings.DialogTitle}");
        Assert.Single(header.Descendants(), element =>
            element.Name.LocalName == "Button" && HasClass(element, "content-header-action"));
        Assert.DoesNotContain(document.Descendants(), element => HasClass(element, "settings-content-header"));
        Assert.DoesNotContain(document.Descendants(), element => HasClass(element, "dialog-header"));
    }

    [Fact]
    public void GeneralSection_UsesThreeSettingsGroupsInFixedOrder()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsGeneralSection.axaml"));
        var groups = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "settings-group"))
            .ToList();

        Assert.Equal(3, groups.Count);

        Assert.Equal(
            [
                "{Binding Shell.I18n[settingsGroupAppPreferences]}",
                "{Binding Shell.I18n[settingsGroupDisplay]}",
                "{Binding Shell.I18n[settingsGroupUpdates]}"
            ],
            groups.Select(group => group
                .Elements()
                .First(element => element.Name.LocalName == "TextBlock")
                .Attribute("Text")?.Value));
    }

    [Fact]
    public void AppearanceSection_GroupsCommonSettingsBeforeOptionalColorDetails()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsAppearanceSection.axaml"));
        var background = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsBackgroundSection.axaml"));
        var groups = document.Descendants()
            .Where(element => element.Name.LocalName == "StackPanel" && HasClass(element, "settings-group"))
            .ToArray();
        Assert.Equal(
            ["{Binding Shell.I18n[settingsGroupThemeMotion]}", "{Binding Shell.I18n[settingsGroupThemeColor]}"],
            groups.Select(group => group.Elements().First().Attribute("Text")?.Value));
        // 背景组随多游戏拆分出独立视图，仍排在主题与颜色之后（游戏背景页复用同一视图）。
        Assert.Contains(
            background.Descendants(),
            element => element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value == "{Binding Shell.I18n[settingsGroupBackground]}");
        var advanced = groups[1].Descendants().Single(element => element.Name.LocalName == "Expander");
        Assert.Equal("{Binding Settings.Appearance.IsAdvancedColorExpanded, Mode=TwoWay}",
            advanced.Attribute("IsExpanded")?.Value);
        Assert.Equal(2, advanced.Descendants().Count(element => element.Name.LocalName == "SettingSelect"));
        Assert.Single(advanced.Descendants(), element => element.Name.LocalName == "SettingRow"
            && element.Attribute("Title")?.Value == "{Binding Shell.I18n[neutralColorStrategy]}");
        Assert.Single(document.Descendants(), element => element.Name.LocalName == "Expander");
        Assert.Single(background.Descendants(), element => element.Name.LocalName == "Image");
    }

    [Fact]
    public void AppearanceSection_ShortEnumsUseNativeRadioGroups()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsAppearanceSection.axaml"));
        var radios = document.Descendants()
            .Where(element => element.Name.LocalName == "RadioButton" && HasClass(element, "setting-radio"))
            .ToArray();

        // 背景来源/填充方式的单选组随背景视图拆出，外观页只剩主题与中性色两组。
        Assert.Equal(5, radios.Length);
        Assert.Equal(
            ["SettingsThemeMode", "SettingsNeutralColorStrategy"],
            radios.Select(radio => radio.Attribute("GroupName")?.Value).Distinct());
        Assert.All(radios, radio =>
        {
            Assert.Contains(
                "SegmentOptionConverter.Instance",
                radio.Attribute("IsChecked")?.Value ?? "",
                StringComparison.Ordinal);
            Assert.Contains(
                "Shell.I18n[",
                radio.Attribute("AutomationProperties.Name")?.Value ?? "",
                StringComparison.Ordinal);
        });
        Assert.Equal(2, document.Descendants()
            .Count(element => element.Name.LocalName == "WrapPanel" && HasClass(element, "setting-radio-group")));
        // 分段按钮样式与类名已随 ADR-070 退场，不再回来自绘段。
        Assert.DoesNotContain(document.Descendants(), element => HasClass(element, "segment-option") || HasClass(element, "segmented"));

        var themeColorCombo = document.Descendants().Single(element => element.Name.LocalName == "ComboBox");
        Assert.Contains(
            themeColorCombo.Descendants(),
            element => element.Name.LocalName == "Border" && HasClass(element, "theme-color-dot"));
    }

    [Fact]
    public void GeneralSection_ShortEnumsUseNativeRadioGroups()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsGeneralSection.axaml"));
        var radios = document.Descendants()
            .Where(element => element.Name.LocalName == "RadioButton" && HasClass(element, "setting-radio"))
            .ToArray();

        Assert.Equal(6, radios.Length);
        Assert.Equal(
            ["SettingsCloseBehavior", "SettingsStatusDetailMode", "SettingsUpdateChannel"],
            radios.Select(radio => radio.Attribute("GroupName")?.Value).Distinct());
        Assert.All(radios, radio =>
        {
            Assert.Contains(
                "SegmentOptionConverter.Instance",
                radio.Attribute("IsChecked")?.Value ?? "",
                StringComparison.Ordinal);
            Assert.Contains(
                "Shell.I18n[",
                radio.Attribute("AutomationProperties.Name")?.Value ?? "",
                StringComparison.Ordinal);
        });
        Assert.Equal(3, document.Descendants()
            .Count(element => element.Name.LocalName == "WrapPanel" && HasClass(element, "setting-radio-group")));

        // 三项目标行为仍留在下拉：选项文案长于单选组可承载的短标签。
        Assert.Contains(
            document.Descendants(),
            element => element.Name.LocalName == "SettingSelect"
                && element.Attribute("ItemsSource")?.Value == "{Binding Settings.Options.AfterLaunchBehavior}");
    }

    [Fact]
    public void DownloadSection_ShortEnumsUseNativeRadioGroups()
    {
        // 下载源是每游戏设置，留在 GameDownload 分类；全局分类只保留下载速度与网络参数。
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsGameDownloadSection.axaml"));
        var radios = document.Descendants()
            .Where(element => element.Name.LocalName == "RadioButton" && HasClass(element, "setting-radio"))
            .ToArray();

        Assert.Equal(2, radios.Length);
        Assert.All(radios, radio =>
        {
            Assert.Equal("SettingsGameDownloadSource", radio.Attribute("GroupName")?.Value);
            Assert.Contains(
                "SegmentOptionConverter.Instance",
                radio.Attribute("IsChecked")?.Value ?? "",
                StringComparison.Ordinal);
        });
        Assert.Single(document.Descendants(),
            element => element.Name.LocalName == "WrapPanel" && HasClass(element, "setting-radio-group"));

        // 代理与限速的选项文案较长或数量多于短枚举门槛，继续用下拉。
        var network = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsDownloadNetworkSection.axaml"));
        Assert.DoesNotContain(network.Descendants(),
            element => element.Name.LocalName == "RadioButton" && HasClass(element, "setting-radio"));
        Assert.Equal(2, network.Descendants().Count(element => element.Name.LocalName == "SettingSelect"));
    }

    [Fact]
    public void AppearancePalette_ReservesWidthForFiveInteractiveSwatches()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsAppearanceSection.axaml"));
        var palette = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ItemsControl"
                && element.Attribute("ItemsSource")?.Value
                    == "{Binding Settings.Appearance.ThemeColorPaletteItems}");

        Assert.Equal(
            "{StaticResource Launcher.Layout.Appearance.Preview.Width}",
            palette.Attribute("Width")?.Value);
    }

    [Fact]
    public void AboutSection_UsesSettingsGroupForTopLevelRhythm()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsAboutSection.axaml"));
        var root = document.Root?.Elements().Single(element => element.Name.LocalName == "StackPanel");

        Assert.NotNull(root);
        Assert.True(HasClass(root!, "settings-group"));
    }

    [Fact]
    public void AboutSection_LegalLinks_ShareRowsWithTheirCopyrightText()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsAboutSection.axaml"));
        var links = document
            .Descendants()
            .Where(element => element.Name.LocalName == "HyperlinkButton")
            .ToList();

        Assert.Equal(2, links.Count);
        Assert.All(links, link =>
        {
            Assert.True(HasClass(link, "inline-legal-link"));
            Assert.False(HasClass(link, "text-link"));
            Assert.Equal("Grid", link.Parent?.Name.LocalName);
            Assert.Contains(link.Parent!.Elements(), element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value.Contains("Shell.I18n[", StringComparison.Ordinal) == true);
        });
        Assert.Equal(
            [
                "{Binding WindowChrome.OpenPrivacyPolicyCommand}",
                "{Binding WindowChrome.OpenDefaultBackgroundArtworkCommand}"
            ],
            links.Select(link => link.Attribute("Command")?.Value));
    }

    [Fact]
    public void AboutSection_WithRedesignedLayout_RendersIdentityCardAndKeyValueRows()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsAboutSection.axaml"));

        // ADR-018 融合变体：身份卡 = 应用图标 + 产品名 + 版本 caption + 副标题，只留一个主操作。
        Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Border" && HasClass(element, "about-identity-card"));
        var appIcon = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Image" && HasClass(element, "about-app-icon"));
        Assert.Equal("/Assets/app-icon.png", appIcon.Attribute("Source")?.Value);
        Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && HasClass(element, "about-product-name"));

        // 版本详细信息 = 5 个 key-value 行（版本/构建时间已在身份卡 caption，不重复）。
        var kvRows = document.Descendants()
            .Where(element => element.Name.LocalName == "Grid" && HasClass(element, "about-kv-row"))
            .ToList();
        Assert.Equal(5, kvRows.Count);
        Assert.All(kvRows, row => Assert.Single(
            row.Elements(),
            element => HasClass(element, "about-kv-value")));
        Assert.Equal(
            [
                "{Binding Shell.CommitShaValue}",
                "{Binding Shell.BuildConfigValue}",
                "{Binding Shell.FrameworkVersionText}",
                "{Binding Shell.AvaloniaVersionText}",
                "{Binding Shell.PlatformValue}"
            ],
            kvRows.SelectMany(row => row.Elements())
                .Where(element => HasClass(element, "about-kv-value"))
                .Select(element => element.Attribute("Text")?.Value));

    }

    [Fact]
    public void SettingsRuntimePaths_OnLinux_LiveOnlyInGameSection()
    {
        var gameDocument = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsGameLaunchSection.axaml"));
        var advancedDocument = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsAdvancedSection.axaml"));
        var runtimeBindings = new[]
        {
            "{Binding Settings.Game.GameRuntime.RunnerPath, Mode=TwoWay}",
            "{Binding Settings.Game.GameRuntime.PrefixPath, Mode=TwoWay}",
            "{Binding Settings.Game.GameRuntime.ProtonPath, Mode=TwoWay}"
        };

        foreach (var binding in runtimeBindings)
        {
            Assert.DoesNotContain(
                advancedDocument.Descendants(),
                element => element.Attribute("Text")?.Value == binding);
            Assert.Contains(
                gameDocument.Descendants(),
                element => element.Attribute("Text")?.Value == binding);
        }

        var runnerPathInput = gameDocument
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBox"
                && element.Attribute("Text")?.Value == runtimeBindings[0]);
        Assert.Equal(
            "{Binding Settings.GameRuntime.IsRunnerPathEnabled}",
            runnerPathInput.Attribute("IsEnabled")?.Value);

        var runtimeInputs = gameDocument
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "TextBox"
                && runtimeBindings.Contains(element.Attribute("Text")?.Value))
            .ToArray();
        Assert.Equal(3, runtimeInputs.Length);
        Assert.All(
            runtimeInputs,
            input => Assert.Equal(
                "{StaticResource Launcher.Component.Settings.Control.MinWidth}",
                input.Attribute("Width")?.Value));
        // 路径字段是普通文本输入：字段外观经 field-input；等宽（uid-input 叠层）
        // 只属于 UID 字段，不得随字段外观捆绑回归。
        Assert.All(
            runtimeInputs,
            input =>
            {
                Assert.True(HasClass(input, "field-input"));
                Assert.False(HasClass(input, "uid-input"));
            });

        // 运行环境路径与运行器选择同组（兼容运行环境），组内不再依赖诊断分区的位置。
        var runtimeGroup = runnerPathInput
            .Ancestors()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "settings-group"));
        Assert.Contains(
            runtimeGroup.Descendants(),
            element => element.Name.LocalName == "SettingSelect"
                && element.Attribute("Title")?.Value == "{Binding Shell.I18n[gameRuntimeRunner]}");

        var styles = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));
        var fieldInputStyle = GetStyleSetters(styles, "TextBox.field-input");
        Assert.Equal(
            "Center",
            fieldInputStyle["VerticalAlignment"]);
    }

    [Fact]
    public void AppearanceSection_NeutralStrategyHint_IsLocalizedAndConditionallyVisible() // ADR-010
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsAppearanceSection.axaml"));
        // The hint renders inside the neutral-strategy row (SettingRow.Hint) so
        // multi-line copy keeps the row's internal spacing instead of an
        // out-of-row margin stacking on the row's min-height padding.
        var neutralRow = document
            .Descendants()
            .Single(element => element.Name.LocalName == "SettingRow"
                && element.Attribute("Title")?.Value
                    == "{Binding Shell.I18n[neutralColorStrategy]}");
        Assert.Equal(
            "{Binding Shell.I18n[neutralColorStrategySeedFollowingHint]}",
            neutralRow.Attribute("Hint")?.Value);
        Assert.Equal(
            "{Binding Settings.Appearance.IsSeedFollowingNeutralStrategySelected}",
            neutralRow.Attribute("IsHintVisible")?.Value);
        Assert.DoesNotContain(document.Descendants(), element => HasClass(element, "settings-neutral-hint"));
    }

    [Fact]
    public void SettingsPanel_UsesTransactionalSaveAndCancelActions()
    {
        var settingsOverlay = File.ReadAllText(TestRepository.FromPresentationRoot("Views/MainWindowSettingsOverlay.axaml"));
        var gameSection = File.ReadAllText(TestRepository.FromPresentationRoot("Views/SettingsGameSection.axaml"));
        var mainWindowCodeBehind = File.ReadAllText(TestRepository.FromPresentationRoot("Views/MainWindow.axaml.cs"));

        Assert.Contains(
            "Description=\"{Binding Settings.GamePathDisplay}\"",
            gameSection,
            StringComparison.Ordinal);
        Assert.Contains(
            "Command=\"{Binding WindowChrome.ToggleSettingsCommand}\"",
            settingsOverlay,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsEnabled=\"{Binding Settings.CanSaveSettings}\"",
            settingsOverlay,
            StringComparison.Ordinal);
        // ADR-070 第二批：底栏「取消」改称「放弃更改」，旧取消文案不再回到设置页脚。
        Assert.Contains(
            "{Binding Shell.I18n[settingsDiscardChanges]}",
            settingsOverlay,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "{Binding Shell.I18n[cancel]}",
            settingsOverlay,
            StringComparison.Ordinal);
        var settingsDocument = XDocument.Parse(settingsOverlay);
        var settingsFooterButtons = settingsDocument
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "dialog-action")
                && element.Attribute("Command")?.Value
                    is "{Binding WindowChrome.ToggleSettingsCommand}"
                    or "{Binding Settings.SaveSettingsCommand}")
            .ToList();
        Assert.Equal(2, settingsFooterButtons.Count);
        Assert.DoesNotContain(
            settingsDocument.Descendants(),
            element => element.Name.LocalName == "DialogSurface.Footer");
        var settingsContentActions = settingsDocument
            .Descendants()
            .Single(element => element.Name.LocalName == "Border" && HasClass(element, "settings-content-actions"));
        Assert.Equal("2", settingsContentActions.Attribute("Grid.Row")?.Value);
        Assert.Equal("2", settingsContentActions.Attribute("Grid.ColumnSpan")?.Value);
        Assert.DoesNotContain(
            settingsContentActions.Ancestors(),
            element => HasClass(element, "settings-content-padding"));
        Assert.Equal(
            new[]
            {
                "{Binding Settings.PreviewMainWindowCommand}",
                "{Binding WindowChrome.ToggleSettingsCommand}",
                "{Binding Settings.SaveSettingsCommand}"
            },
            settingsContentActions.Descendants()
                .Where(element => element.Name.LocalName == "Button" && HasClass(element, "dialog-action"))
                .Select(element => element.Attribute("Command")?.Value));
        Assert.Contains(settingsContentActions.Descendants(), element => element.Name.LocalName == "WrapPanel");
        Assert.Contains(
            "Glyph=\"{x:Static glyphs:FluentGlyphs.Save}\"",
            settingsOverlay,
            StringComparison.Ordinal);
        // The escape-key resolution for the settings modal is a ModalRegistration
        // (ADR-023): kind, visibility source, content, and escape command colocated
        // in the modal catalog.
        var modalCatalog = File.ReadAllText(TestRepository.FromPresentationRoot("Features/Shell/ShellModalCatalog.cs"));
        Assert.Contains(
            "ModalKind.Settings,",
            modalCatalog,
            StringComparison.Ordinal);
        Assert.Contains(
            "WindowChrome.ToggleSettingsCommand",
            modalCatalog,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "vm.WindowChrome.IsSettingsVisible",
            mainWindowCodeBehind,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Views/SettingsGameSection.axaml", "{Binding ManageGameFromSettingsCommand}", "uninstall")]
    [InlineData("Views/SettingsAdvancedSection.axaml", "{Binding Settings.RequestResetSettingsCommand}", null)]
    public void SettingsDangerActions_WithDestructiveCommands_UseDangerActionStyle(
        string sectionPath,
        string command,
        string? commandParameter)
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot(sectionPath));
        var button = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == command
                && element.Attribute("CommandParameter")?.Value == commandParameter);

        Assert.True(HasClass(button, "flat-action"), $"{command} must inherit flat-action geometry.");
        Assert.True(HasClass(button, "danger-action"), $"{command} must use danger-action.");
    }

    [Fact]
    public void SettingsOverlay_RemovesTopStatusSummaryAndUsesInlineContentHeading()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindowSettingsOverlay.axaml"));
        var settingsContent = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid"
                && HasClass(element, "settings-content"));

        Assert.Equal("Auto,*", settingsContent.Attribute("RowDefinitions")?.Value);
        var contentHeading = settingsContent.Descendants().Single(element =>
            HasClass(element, "settings-content-heading"));
        // 分类标题固定在滚动区上方。
        Assert.Equal(
            "Auto,*",
            contentHeading.Parent!.Attribute("RowDefinitions")?.Value);
        var contentScrollViewer = settingsContent.Descendants().Single(element =>
            element.Name.LocalName == "ScrollViewer");
        Assert.DoesNotContain(contentScrollViewer.Descendants(), element =>
            HasClass(element, "settings-content-heading"));
        Assert.DoesNotContain(
            contentScrollViewer.Descendants(),
            element => element.Name.LocalName == "Button" && HasClass(element, "dialog-close"));
        Assert.Single(contentScrollViewer.Descendants(), element =>
            HasClass(element, "settings-sections"));
        Assert.Single(settingsContent.Descendants(), element => HasClass(element, "settings-content-heading"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => HasClass(element, "settings-status-summary"));
    }

    [Fact]
    public void SettingsGroups_UseAvailableContentWidth()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/MainWindow.Styles.axaml"));
        var settingsGroup = GetStyleSetters(document, "StackPanel.settings-group");

        Assert.DoesNotContain("MaxWidth", settingsGroup.Keys);
        Assert.DoesNotContain("HorizontalAlignment", settingsGroup.Keys);
    }

    [Fact]
    public void SettingsSections_InteractiveControlsHaveLocalizedAutomationNames()
    {
        var sectionPaths = new[]
        {
            "Views/SettingsGeneralSection.axaml",
            "Views/SettingsGameSection.axaml",
            "Views/SettingsDownloadNetworkSection.axaml",
            "Views/SettingsAppearanceSection.axaml",
            "Views/SettingsAdvancedSection.axaml",
            "Views/SettingsAboutSection.axaml"
        };
        var interactiveControlNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Button",
            "ColorPicker",
            "ComboBox",
            "RadioButton",
            "SettingSelect",
            "TextBox",
            "ToggleSwitch"
        };

        foreach (var sectionPath in sectionPaths)
        {
            var document = XDocument.Load(TestRepository.FromPresentationRoot(sectionPath));
            var controls = document
                .Descendants()
                .Where(element => interactiveControlNames.Contains(element.Name.LocalName))
                .ToList();

            Assert.NotEmpty(controls);
            Assert.All(
                controls,
                control =>
                {
                    var automationName = control
                        .Attributes()
                        .SingleOrDefault(attribute =>
                            attribute.Name.LocalName == (control.Name.LocalName == "SettingSelect"
                                ? "AutomationName"
                                : "AutomationProperties.Name"))
                        ?.Value;

                    Assert.False(
                        string.IsNullOrWhiteSpace(automationName),
                        $"{sectionPath}: {control.Name.LocalName} is missing AutomationProperties.Name.");
                    // Item-templated controls (e.g. palette swatches) bind a unique,
                    // language-neutral name from the item model instead of a resource key.
                    Assert.True(
                        automationName.Contains("Shell.I18n[", StringComparison.Ordinal)
                        || automationName.StartsWith("{Binding ", StringComparison.Ordinal),
                        $"{sectionPath}: {control.Name.LocalName} AutomationProperties.Name is neither localized nor item-bound.");
                });
        }
    }

    [Fact]
    public void AdvancedSettings_KeepsLogActionsInDiagnosticsRow()
    {
        var document = XDocument.Load(TestRepository.FromPresentationRoot("Views/SettingsAdvancedSection.axaml"));
        var group = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && HasClass(element, "settings-group")
                && element.Elements().Any(child =>
                    child.Name.LocalName == "TextBlock"
                    && child.Attribute("Text")?.Value == "{Binding Shell.I18n[settingsGroupDiagnostics]}"));
        var rows = group
            .Descendants()
            .Where(element => element.Name.LocalName is "SettingRow" or "SettingSelect")
            .ToList();

        Assert.Equal(2, rows.Count);
        var logFilesRow = Assert.Single(
            rows,
            row => row.Attribute("Title")?.Value == "{Binding Shell.I18n[logFiles]}");
        Assert.Equal(
            "{Binding Shell.I18n[logFilesDescription]}",
            logFilesRow.Attribute("Description")?.Value);

        var action = logFilesRow
            .Elements()
            .Single(element => element.Name.LocalName == "SettingRow.Action");
        var actionPanel = action
            .Elements()
            .Single(element => element.Name.LocalName == "WrapPanel");
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Sm}",
            actionPanel.Attribute("ItemSpacing")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Spacing.Sm}",
            actionPanel.Attribute("LineSpacing")?.Value);
        Assert.Equal(
            "{StaticResource Launcher.Component.Settings.Row.Action.MaxWidth}",
            actionPanel.Attribute("MaxWidth")?.Value);

        var app = XDocument.Load(TestRepository.FromHostRoot("App.axaml"));
        var actionMaxWidth = app
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "Launcher.Component.Settings.Row.Action.MaxWidth"));
        Assert.Equal("440", actionMaxWidth.Value);

        var commands = actionPanel
            .Elements()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element =>
                element.Attribute("Command")?.Value
                ?? throw new InvalidDataException("Advanced log action is missing Command."))
            .ToArray();
        Assert.Equal(
            [
                "{Binding LogViewer.OpenCommand}",
                "{Binding LogExport.OpenCommand}"
            ],
            commands);
        Assert.DoesNotContain(
            group.Elements(),
            element => element.Name.LocalName == "WrapPanel");
    }

    [Fact]
    public void SettingsAboutAndAdvancedActions_UsePurposeBasedOrderAndExclusiveOwnership()
    {
        var aboutText = File.ReadAllText(TestRepository.FromPresentationRoot("Views/SettingsAboutSection.axaml"));
        var advancedText = File.ReadAllText(TestRepository.FromPresentationRoot("Views/SettingsAdvancedSection.axaml"));
        var overlay = File.ReadAllText(TestRepository.FromPresentationRoot("Views/MainWindowSettingsOverlay.axaml"));

        // 身份卡 → 链接 → 贡献者 → 特别感谢 → 构建与运行信息 → 法律信息。
        AssertOrdered(
            aboutText,
            "Shell.VersionCaptionText",
            "Settings.CheckForUpdatesCommand",
            "WindowChrome.OpenAboutOfficialSiteCommand",
            "WindowChrome.OpenHelpDocsCommand",
            "Shell.I18n[aboutBuildInformation]",
            "Shell.CommitShaValue",
            "Shell.BuildConfigValue",
            "Shell.FrameworkVersionText",
            "Shell.AvaloniaVersionText",
            "Shell.PlatformValue");
        AssertOrdered(
            aboutText,
            "Settings.CheckForUpdatesCommand",
            "WindowChrome.OpenAboutOfficialSiteCommand",
            "WindowChrome.OpenHelpDocsCommand",
            "WindowChrome.OpenGitHubRepositoryCommand",
            "WindowChrome.OpenGitHubReleaseRepositoryCommand",
            "WindowChrome.OpenIssueTrackerCommand",
            "Shell.I18n[aboutContributors]",
            "Shell.I18n[aboutSpecialThanks]",
            "Shell.I18n[aboutCommunityThanks]",
            "Shell.I18n[aboutBuildInformation]",
            "Shell.I18n[aboutCopyrightText]",
            "WindowChrome.OpenPrivacyPolicyCommand",
            "Shell.I18n[defaultBackgroundCopyrightText]",
            "WindowChrome.OpenDefaultBackgroundArtworkCommand",
            "Shell.I18n[aboutDisclaimerText]");
        AssertOrdered(
            advancedText,
            "LogViewer.OpenCommand",
            "LogExport.OpenCommand",
            "WindowChrome.OpenDataDirectoryCommand");

        Assert.DoesNotContain("LogViewer.OpenCommand", aboutText, StringComparison.Ordinal);
        Assert.DoesNotContain("LogExport.OpenCommand", aboutText, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowChrome.OpenDataDirectoryCommand", aboutText, StringComparison.Ordinal);
        Assert.Contains("Shell.I18n[settingsGroupDiagnostics]", advancedText, StringComparison.Ordinal);

        var document = XDocument.Parse(overlay);
        var footerButtons = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "Button"
                && HasClass(element, "dialog-action")
                && element.Attribute("Command")?.Value
                    is "{Binding WindowChrome.ToggleSettingsCommand}"
                    or "{Binding Settings.SaveSettingsCommand}")
            .ToList();
        Assert.Equal(2, footerButtons.Count);
        Assert.Equal(
            "{Binding WindowChrome.ToggleSettingsCommand}",
            footerButtons[0].Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding Settings.SaveSettingsCommand}",
            footerButtons[1].Attribute("Command")?.Value);
    }
}
