using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    public static IEnumerable<object[]> FloatingTaskCases =>
        from language in new[] { LauncherLanguages.English, LauncherLanguages.SimplifiedChinese,
            LauncherLanguages.TraditionalChinese, LauncherLanguages.Japanese }
        from dark in new[] { false, true }
        select new object[] { language, dark };

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Golden_FloatingTaskCard_MatchesBaseline(bool dark)
    {
        using var theme = ThemeVariantSnapshot.Capture(dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
        using var context = CreateContext();
        PrepareGoldenWindow(context, dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
        context.Provider.GetRequiredService<ThemeApplier>().ApplyThemeMode(dark ? ThemeModes.Dark : ThemeModes.Light);
        context.ViewModel.Shell.ApplyLanguage("zh-Hans", hasSnapshot: false);
        context.ViewModel.Shell.IsBusy = false;
        context.ViewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.UpdateAvailable });
        context.ViewModel.Operations.ApplyProgress(new GameOperationProgress { OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading, Progress = 64, DownloadedSize = 4123168604, TotalSize = 6442450944,
            BytesPerSecond = 19503513, CanPause = true });
        context.Window.Show();
        if (dark) GoldenScreenshot.Compare(context.Window, "floating-task-dark-zh");
        else GoldenScreenshot.Compare(context.Window, "floating-task-light-zh");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GameTask_RevealClosesCleanSettingsButPreservesDraftConfirmation(bool dirty)
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Operations.ApplyProgress(new GameOperationProgress { OperationKind = GameOperationKind.Download });
        OpenSettings(context);
        if (dirty) context.ViewModel.Settings.Editor.Current.GamePath = "unsubmitted-path";
        context.ViewModel.Operations.ShowTaskCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(context.ViewModel.Operations.IsTaskCardVisible);
        Assert.Equal(dirty, context.ViewModel.WindowChrome.IsSettingsVisible);
        Assert.Equal(dirty, context.ViewModel.Settings.IsUnsavedChangesVisible);
    }

    [AvaloniaTheory]
    [MemberData(nameof(FloatingTaskCases))]
    public void GameTask_FloatsAboveSplitButtonWithoutMovingIt(string language, bool dark)
    {
        var themeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        using var theme = ThemeVariantSnapshot.Capture(themeVariant);
        using var context = CreateContext();
        PrepareGoldenWindow(context, themeVariant);
        context.Provider.GetRequiredService<ThemeApplier>().ApplyThemeMode(dark ? ThemeModes.Dark : ThemeModes.Light);
        var window = context.Window;
        var sizes = new[] { (Width: 1300, Height: 754), (Width: 1024, Height: 640) };
        window.Width = sizes[0].Width;
        window.Height = sizes[0].Height;
        context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
        context.ViewModel.Shell.IsBusy = false;
        context.ViewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.UpdateAvailable });
        context.ViewModel.Operations.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download, Stage = GameOperationStage.Downloading,
            Progress = 64, DownloadedSize = 3L * 1024 * 1024 * 1024, TotalSize = 6L * 1024 * 1024 * 1024,
            BytesPerSecond = 18 * 1024 * 1024, CanPause = true,
            ActiveFiles = [new("GameData/Audio/JP/CharacterVoice.bundle", 12, 100, true)]
        });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(themeVariant, window.ActualThemeVariant);
        var card = window.GetVisualDescendants().OfType<GameTaskCard>().Single();
        var split = window.GetVisualDescendants().OfType<SplitButton>().Single();
        var title = card.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("progress-title"));
        var titleFontSize = title.FontSize;
        foreach (var scale in new[] { 1d, 1.25, 1.5 })
        {
            using var typography = scale == 1d ? null : new TypographyScaleSnapshot(window, scale);
            foreach (var (width, height) in sizes)
            {
                window.Width = width;
                window.Height = height;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(new Size(width, height), window.ClientSize);
                Assert.Equal(titleFontSize * scale, title.FontSize);
                AssertScenario($"{language}, {themeVariant}, {width}×{height}, scale {scale}");
            }

            // Start the next scale at the current size.
            Array.Reverse(sizes);
        }

        void AssertScenario(string scenario)
        {
            var initialButton = split.TranslatePoint(default, window)!.Value;
            Assert.True(card.IsEffectivelyVisible);
            var background = PaintColor(card.FindControl<Border>("FloatingTaskCard")!.Background);
            Assert.True(ColorUtils.GetContrastRatio(PaintColor(title.Foreground), background) >= 4.5);
            Assert.Equal(dark, background.R < 128 && background.G < 128 && background.B < 128);
            Assert.Null(context.ViewModel.ModalHost.Top);
            AssertFloatingPlacement(scenario);
            var size = card.FindControl<TextBlock>("TaskSizeSummary")!;
            Assert.True(size.IsEffectivelyVisible);
            Assert.Equal("3GB / 6GB", size.Text);
            var speed = card.FindControl<TextBlock>("TaskDownloadSpeed")!;
            Assert.Equal("18MB/s", speed.Text);
            Assert.DoesNotContain(card.GetVisualDescendants(), control => control is ScrollViewer or ItemsControl);

            context.ViewModel.Operations.PrimaryActionCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(initialButton == split.TranslatePoint(default, window)!.Value,
                $"{scenario}: the primary action moved the split button from {initialButton}.");
            AssertFloatingPlacement(scenario);
            foreach (var button in card.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
                AssertControlInsideWindow(button, window);
        }

        void AssertFloatingPlacement(string scenario)
        {
            AssertControlInsideWindow(card, window);
            var position = card.TranslatePoint(default, window)!.Value;
            var button = split.TranslatePoint(default, window)!.Value;
            var rightEdgeDifference = Math.Abs(position.X + card.Bounds.Width - button.X - split.Bounds.Width);
            var verticalGap = button.Y - position.Y - card.Bounds.Height;
            Assert.True(rightEdgeDifference <= 1 && verticalGap is >= 15 and <= 17,
                $"{scenario}: right-edge difference {rightEdgeDifference}, vertical gap {verticalGap}.");
        }
    }
}
