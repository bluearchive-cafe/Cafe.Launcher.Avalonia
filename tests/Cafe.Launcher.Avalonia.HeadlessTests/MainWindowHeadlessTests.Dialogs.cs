using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Controls;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void UpdateDialog_WhenClosingWithMotion_KeepsReleaseNotesVisibleDuringExit()
    {
        var originalDuration = AnimationTimings.ExitAnimationDuration;
        try
        {
            AnimationTimings.ExitAnimationDuration = Timeout.InfiniteTimeSpan;
            using var context = CreateContext();
            context.ViewModel.IsMotionReduced = false;
            context.Window.Show();
            context.ViewModel.Dialogs.ShowUpdateAvailable(
                "1.2.0", [], canSelfUpdate: false, releaseNotes: "## 更新内容");
            Dispatcher.UIThread.RunJobs();

            var viewer = context.Window.GetVisualDescendants()
                .OfType<ReleaseNotesMarkdownViewer>()
                .Single();
            var overlay = viewer.GetVisualAncestors()
                .OfType<Grid>()
                .First(grid => grid.Classes.Contains("motion-overlay"));
            Assert.True(viewer.IsEffectivelyVisible);
            Assert.True(MotionVisibility.GetIsOpen(overlay));

            try
            {
                context.ViewModel.Dialogs.CancelUpdateAvailableCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                Assert.False(MotionVisibility.GetIsOpen(overlay));
                Assert.True(overlay.IsVisible);
                Assert.Contains("motion-exit", overlay.Classes);
                Assert.True(viewer.IsEffectivelyVisible);
                Assert.Equal("## 更新内容", viewer.Markdown);
            }
            finally
            {
                context.ViewModel.Dialogs.ShowUpdateAvailable(
                    "1.2.0", [], canSelfUpdate: false, releaseNotes: "## 更新内容");
            }
        }
        finally
        {
            AnimationTimings.ExitAnimationDuration = originalDuration;
        }
    }

    [AvaloniaFact]
    public void ResourcePanel_HintStrip_IsAPermanentNoteWithoutDismissAffordance()
    {
        // 2026-09-28 用户裁决：UID 提示条是常驻说明行，没有关闭钮；
        // 「每会话关闭一次」的行为与其命令一并退场。
        using var context = CreateContext();
        context.Window.Show();
        ShowResourcePanel(context);
        Dispatcher.UIThread.RunJobs();
        var expectedHint = context.ViewModel.Shell.I18n[LocalizationKeys.ResourcePanelUidGenerationHint];

        var hintStrip = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(border =>
                border.Classes.Contains("info-strip")
                && !border.Classes.Contains("resource-panel-status")
                && border.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => string.Equals(text.Text, expectedHint, StringComparison.Ordinal)));

        Assert.True(hintStrip.IsEffectivelyVisible);
        Assert.Empty(hintStrip.GetVisualDescendants().OfType<Button>());
    }

    [AvaloniaFact]
    public void ResourcePanel_Content_RendersSegmentedSourceSwitchesAndStatusChips()
    {
        // ADR-041 内容解剖的渲染级守卫：UID 展示态 + 分段来源 + 单卡三行（Switch/chip），
        // 状态类随 Status 与消息语调联动。VM 状态机不因视图测试被驱动（不触发任何命令）。
        using var context = CreateContext();
        context.Window.Show();
        ShowResourcePanel(context);
        Dispatcher.UIThread.RunJobs();
        var resourcePanel = context.ViewModel.ResourcePanel;

        // UID 展示态：两个编辑/缺失态输入框隐藏，分段按钮成对渲染且勾选段跟随来源。
        var uidInputs = context.Window.GetVisualDescendants().OfType<TextBox>()
            .Where(box => box.Classes.Contains("uid-input"))
            .ToArray();
        Assert.Equal(2, uidInputs.Length);
        Assert.All(uidInputs, input => Assert.False(input.IsEffectivelyVisible));

        var segments = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .Where(button => button.Classes.Contains("segment-option"))
            .ToArray();
        Assert.Equal(2, segments.Length);
        var autoName = context.ViewModel.Shell.I18n[LocalizationKeys.ResourcePanelUidSourceAuto];
        var autoSegment = Assert.Single(segments, segment =>
            AutomationProperties.GetName(segment) == autoName);
        Assert.True(autoSegment.IsChecked == true);
        Assert.All(segments.Where(segment => segment != autoSegment),
            segment => Assert.False(segment.IsChecked == true));

        // 单卡三行：Switch 跟随条目启用状态，chip 的状态类跟随条目 Status。
        var switches = context.Window.GetVisualDescendants().OfType<ToggleSwitch>()
            .ToArray();
        Assert.Equal(3, switches.Length);
        var chips = context.Window.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("status-chip"))
            .ToArray();
        Assert.Equal(3, chips.Length);
        var items = resourcePanel.ResourcePanelItems;
        for (var index = 0; index < items.Count; index++)
        {
            Assert.Equal(items[index].IsEnabled, switches[index].IsChecked == true);
            Assert.Equal(items[index].IsOperable, switches[index].IsEnabled);
            Assert.Contains("loading", chips[index].Classes);
        }

        items[0].Status = ResourcePanelItemStatus.Ready;
        items[1].IsEnabled = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("ready", chips[0].Classes);
        Assert.DoesNotContain("loading", chips[0].Classes);
        Assert.True(switches[1].IsChecked == true);

        // 消息条随内容显隐、随语调换 danger 类（前导图标同时切换，颜色之外的线索）。
        var statusStrip = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Classes.Contains("resource-panel-status"));
        Assert.False(statusStrip.IsEffectivelyVisible);
        resourcePanel.ResourcePanelMessage = "boom";
        resourcePanel.IsResourcePanelMessageError = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(statusStrip.IsEffectivelyVisible);
        Assert.Contains("danger", statusStrip.Classes);
    }

    [AvaloniaFact]
    public void LogViewer_EmptyState_KeepsConfiguredHeight()
    {
        using var context = CreateContext();
        context.Window.Show();
        ShowLogViewer(context);

        var dialog = context.Window
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>()
            .Single(surface => ReferenceEquals(
                surface.CloseCommand,
                context.ViewModel.LogViewer.CloseCommand));
        var application = Application.Current
            ?? throw new InvalidOperationException("Headless application is not initialised.");
        Assert.True(application.TryGetResource(
            "Launcher.Layout.LogViewer.Height",
            application.ActualThemeVariant,
            out var configuredHeight));
        Assert.Equal(Assert.IsType<double>(configuredHeight), dialog.Bounds.Height);
    }

    [AvaloniaTheory]
    [InlineData("resource-panel")]
    [InlineData("log-viewer")]
    [InlineData("log-export")]
    [InlineData("confirmation")]
    [InlineData("setup-wizard")]
    public void SecondaryOverlay_AtMinimumWindowSize_KeepsCriticalActionsReachable(string overlay)
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.Window.Show();

        Button[] actions = overlay switch
        {
            "resource-panel" => ShowResourcePanel(context),
            "log-viewer" => ShowLogViewer(context),
            "log-export" => ShowLogExport(context),
            "confirmation" => ShowLongConfirmation(context),
            "setup-wizard" => ShowSetupWizard(context),
            _ => throw new ArgumentOutOfRangeException(nameof(overlay))
        };
        Dispatcher.UIThread.RunJobs();

        Assert.NotEmpty(actions);
        Assert.All(actions, action =>
        {
            Assert.True(action.IsEffectivelyVisible);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(action)));
            AssertControlInsideWindow(action, context.Window);
        });
    }

    [AvaloniaFact]
    public void LogExport_WhenOpened_HasTwoAlwaysIncludedContentRows()
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.Window.Show();
        context.ViewModel.LogExport.OpenCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var requiredItems = context.Window.GetVisualDescendants().OfType<CheckBox>()
            .Where(box => box.IsChecked == true && !box.IsEnabled)
            .ToArray();

        Assert.Equal(2, requiredItems.Length);
    }

    [AvaloniaFact]
    public void LogExport_WhenUserDataWarningIsLong_WrapsInsideWarningCard()
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.Window.Show();
        ShowLogExport(context);
        context.ViewModel.LogExport.IncludeUserData = true;
        Dispatcher.UIThread.RunJobs();

        // Anchor on the resource the card actually renders: matching the copy by hand meant a
        // reworded or retranslated warning silently detached this locator from its element.
        var expectedWarning = context.ViewModel.Shell.I18n[LocalizationKeys.LogExportUserDataWarning];
        var warning = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(border =>
                border.Classes.Contains("log-export-warning")
                && border.IsEffectivelyVisible
                && border.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => string.Equals(text.Text, expectedWarning, StringComparison.Ordinal)));
        var warningText = warning.GetVisualDescendants().OfType<TextBlock>().Single();
        // Whether the shipped translation happens to be long is a copy fact, not a layout one.
        // Supply the long warning here so this stays a wrapping test: asserting on the shipped
        // string made it fail whenever the copy was shortened or translated more tersely.
        warningText.Text = string.Concat(Enumerable.Repeat("Wrap this deliberately long warning line. ", 5));
        Dispatcher.UIThread.RunJobs();
        var textTopLeft = warningText.TranslatePoint(default, warning);

        Assert.NotNull(textTopLeft);
        Assert.True(warningText.Bounds.Height > warningText.FontSize * 1.5);
        Assert.True(textTopLeft.Value.X + warningText.Bounds.Width <= warning.Bounds.Width);
    }

    [AvaloniaFact]
    public void ModalIsolation_WhenConfirmationIsVisible_DisablesBackgroundAndRestoresItAfterClose()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ResourcePanelSourceConfirm.Show("switch source");
        Dispatcher.UIThread.RunJobs();
        var settingsButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button =>
                button.Classes.Contains("settings")
                && ReferenceEquals(
                    button.Command,
                    context.ViewModel.WindowChrome.ShowSettingsCommand));
        var startButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .First(button =>
                button.Classes.Contains("primary-operation")
                && ReferenceEquals(
                    button.Command,
                    context.ViewModel.Operations.StartGameCommand));
        var cancelButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .First(button =>
                button.IsEffectivelyVisible
                && ReferenceEquals(
                    button.Command,
                    context.ViewModel.Dialogs.ResourcePanelSourceConfirm.CancelCommand));

        Assert.False(settingsButton.IsEffectivelyEnabled);
        Assert.False(startButton.IsEffectivelyEnabled);
        Assert.True(cancelButton.IsEffectivelyEnabled);

        cancelButton.Command!.Execute(cancelButton.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(settingsButton.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void ModalIsolation_WhenConfirmationCoversSettings_DisablesSettingsLayer()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.WindowChrome.IsSettingsVisible = true;
        context.ViewModel.Dialogs.RepairConfirm.Show("repair confirmation");
        Dispatcher.UIThread.RunJobs();
        var settingsCancelButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button =>
                button.Classes.Contains("dialog-action")
                && ReferenceEquals(
                    button.Command,
                    context.ViewModel.WindowChrome.ShowSettingsCommand));
        var confirmDialog = context.Window
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.ConfirmDialog>()
            .Single(control => control.IsOpen);

        Assert.False(settingsCancelButton.IsEffectivelyEnabled);
        Assert.True(confirmDialog.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void DialogOverlay_WhenRepairIsRequested_BecomesVisible()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.RepairConfirm.Show("repair confirmation");
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(
            context.Window.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Classes.Contains("dialog-overlay")
                && grid.IsEffectivelyVisible);
        Assert.Contains(
            context.Window.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "repair confirmation");
    }

    [AvaloniaFact]
    public void RepairConfirm_WithLongMessage_DoesNotExceedDefaultMaximumWidth()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.RepairConfirm.Show(
            "下载源已切换。Cafe 下载源与官方下载源使用不同的文件清单，因此必须根据当前下载源修复已安装的游戏，才能得到可靠的启动校验结果。现在开始修复吗？");
        Dispatcher.UIThread.RunJobs();

        var dialog = context.Window
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.ConfirmDialog>()
            .Single(control => control.IsOpen);
        var surface = dialog
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>()
            .Single();

        Assert.True(surface.Bounds.Width <= 540);
        Assert.True(surface.Bounds.Height < 480);

        var supportText = surface
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(text => text.Name == "PART_BasicSupportTextBlock");
        Assert.False(supportText.IsVisible);
    }

    [AvaloniaFact]
    public void DesignGallery_WhenOpened_EnumeratesTokenGroupsFromResources()
    {
        using var context = CreateContext();
        context.Window.Show();

        context.ViewModel.Dialogs.Gallery.OpenCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.Gallery.IsVisible);
        Assert.True(context.ViewModel.Dialogs.Gallery.Groups.Count >= 12, $"Expected 12+ families, got {context.ViewModel.Dialogs.Gallery.Groups.Count}.");
        var totalItems = context.ViewModel.Dialogs.Gallery.Groups.Sum(group => group.Items.Count);
        Assert.True(totalItems >= 130, $"Expected 130+ tokens, got {totalItems}.");
        Assert.Contains(context.ViewModel.Dialogs.Gallery.Groups, group => group.Family == "Color");
        Assert.Contains(context.ViewModel.Dialogs.Gallery.Groups, group => group.Family == "Component");
        Assert.Contains(
            context.ViewModel.Dialogs.Gallery.Groups.SelectMany(group => group.Items),
            item => item.Key == "Launcher.Text.Primary");

        var gallerySurface = context.Window
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>()
            .Single(surface => ReferenceEquals(
                surface.CloseCommand,
                context.ViewModel.Dialogs.Gallery.CloseCommand));
        var scrollViewer = gallerySurface
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Name == "PART_ScrollViewer");
        var contentPresenter = gallerySurface
            .GetVisualDescendants()
            .OfType<ContentPresenter>()
            .Single(control => control.Name == "PART_ScrollContentPresenter");
        var galleryContent = Assert.IsType<StackPanel>(contentPresenter.Content);
        var scrollTopLeft = scrollViewer.TranslatePoint(default, gallerySurface);
        var contentTopLeft = galleryContent.TranslatePoint(default, gallerySurface);
        Assert.NotNull(scrollTopLeft);
        Assert.NotNull(contentTopLeft);
        Assert.Equal(
            scrollTopLeft.Value.X + scrollViewer.Padding.Left,
            contentTopLeft.Value.X);

        context.ViewModel.Dialogs.Gallery.CloseCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(context.ViewModel.Dialogs.Gallery.IsVisible);
    }

    private static Button[] ShowResourcePanel(TestContext context)
    {
        context.ViewModel.ResourcePanel.IsResourcePanelVisible = true;
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                ReferenceEquals(button.Command, context.ViewModel.ResourcePanel.CloseResourcePanelCommand)
                || ReferenceEquals(button.Command, context.ViewModel.ResourcePanel.RefreshResourcePanelCommand)
                || ReferenceEquals(button.Command, context.ViewModel.ResourcePanel.SaveResourcePanelCommand))
            .ToArray();
    }

    private static Button[] ShowLogViewer(TestContext context)
    {
        context.ViewModel.LogViewer.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                ReferenceEquals(button.Command, context.ViewModel.LogViewer.CloseCommand)
                || ReferenceEquals(button.Command, context.ViewModel.LogExport.OpenCommand))
            .ToArray();
    }

    private static Button[] ShowLogExport(TestContext context)
    {
        context.ViewModel.LogExport.OpenCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                ReferenceEquals(button.Command, context.ViewModel.LogExport.CloseCommand)
                || ReferenceEquals(button.Command, context.ViewModel.LogExport.ExportCommand))
            .ToArray();
    }

    /// <summary>
    /// A checked content row draws its glyph on the accent fill, and that fill is retinted per
    /// theme (M3 tone 40 in light, tone 80 in dark). Fluent ships a white glyph, which disappears
    /// on the pale dark-theme fill, so the pairing is asserted on the rendered controls rather
    /// than on the style source: the user sees contrast, not selectors.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LogExport_WhenAContentRowIsChecked_KeepsItsGlyphReadable(bool isDark)
    {
        using var context = CreateContext();
        // 本用例自己改主题变体，因此显式还原：无头套件共享 Application，留下的值会决定后面
        // golden 截到亮色还是暗色（AUD-TEST-013）。
        using var themeVariant = ThemeVariantSnapshot.Capture(
            isDark ? ThemeVariant.Dark : ThemeVariant.Light);
        // Applied through the settings view model: that is the path which also replays the M3
        // colour scheme with the tones belonging to the theme. Switching the variant alone would
        // leave the light-tone fill in place and hide the very problem this guard exists for.
        context.ViewModel.Settings.Appearance.ApplyTheme(isDark ? ThemeModes.Dark : ThemeModes.Light);
        Dispatcher.UIThread.RunJobs();

        PrepareGoldenWindow(context, isDark ? ThemeVariant.Dark : ThemeVariant.Light);
        context.Window.Show();
        ShowLogExport(context);
        context.ViewModel.LogExport.IncludeCrashReports = true;
        Dispatcher.UIThread.RunJobs();

        var checkedBox = context.Window.GetVisualDescendants().OfType<CheckBox>()
            .Single(box => box.IsChecked == true && box.IsEnabled);
        var glyph = checkedBox.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Path>()
            .Single(path => path.Name == "CheckGlyph");
        var normalRectangle = checkedBox.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "NormalRectangle");

        var glyphColor = ((ISolidColorBrush)glyph.Fill!).Color;
        var fillColor = ((ISolidColorBrush)normalRectangle.Background!).Color;
        var ratio = ColorUtils.GetContrastRatio(glyphColor, fillColor);

        Assert.True(
            ratio >= 3.0,
            $"[{(isDark ? "Dark" : "Light")}] the checked glyph ({glyphColor}) contrasts {ratio:F2}:1 "
            + $"with its fill ({fillColor}); expected at least 3:1.");
    }

    private static Button[] ShowLongConfirmation(TestContext context)
    {
        context.ViewModel.Dialogs.RepairConfirm.Show(string.Concat(Enumerable.Repeat(
            "下载源已切换，修复前需要重新确认本地文件状态。",
            30)));
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                (ReferenceEquals(button.Command, context.ViewModel.Dialogs.RepairConfirm.CancelCommand)
                    || ReferenceEquals(button.Command, context.ViewModel.Dialogs.RepairConfirm.ConfirmCommand))
                && button.IsEffectivelyVisible)
            .ToArray();
    }

    private static Button[] ShowSetupWizard(TestContext context)
    {
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                ReferenceEquals(button.Command, context.ViewModel.Dialogs.SetupWizardExitConfirm.ShowCommand)
                || ReferenceEquals(button.Command, context.ViewModel.Dialogs.SetupWizard.NextCommand))
            .ToArray();
    }
}
