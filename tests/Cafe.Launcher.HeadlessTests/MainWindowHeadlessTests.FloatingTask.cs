using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
        from language in new[] { "en", "zh-Hans", "zh-Hant", "ja" }
        from dark in new[] { false, true }
        from scale in new[] { 1d, 1.25, 1.5 }
        from minimum in new[] { false, true }
        select new object[] { language, dark, scale, minimum };

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
    public void GameTask_FloatsAboveSplitButtonWithoutMovingIt(string language, bool dark, double scale, bool minimum)
    {
        using var theme = ThemeVariantSnapshot.Capture(dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
        using var context = CreateContext();
        PrepareGoldenWindow(context, dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
        context.Provider.GetRequiredService<ThemeApplier>().ApplyThemeMode(dark ? ThemeModes.Dark : ThemeModes.Light);
        context.Window.Width = minimum ? 1024 : 1300;
        context.Window.Height = minimum ? 640 : 754;
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
        context.Window.Show();
        using var typography = new TypographyScaleSnapshot(context.Window, scale);
        Dispatcher.UIThread.RunJobs();
        var card = context.Window.GetVisualDescendants().OfType<GameTaskCard>().Single();
        var split = context.Window.GetVisualDescendants().OfType<SplitButton>().Single();
        var initialButton = split.TranslatePoint(default, context.Window)!.Value;
        Assert.True(card.IsEffectivelyVisible);
        var background = PaintColor(card.FindControl<Border>("FloatingTaskCard")!.Background);
        var title = card.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("progress-title"));
        Assert.True(ColorUtils.GetContrastRatio(PaintColor(title.Foreground), background) >= 4.5);
        Assert.Equal(dark, background.R < 128 && background.G < 128 && background.B < 128);
        Assert.Null(context.ViewModel.ModalHost.Top);
        AssertFloatingPlacement();
        var size = card.FindControl<TextBlock>("TaskSizeSummary")!;
        Assert.True(size.IsEffectivelyVisible);
        Assert.Equal("3GB / 6GB", size.Text);
        var speed = card.FindControl<TextBlock>("TaskDownloadSpeed")!;
        Assert.Equal("18MB/s", speed.Text);
        Assert.DoesNotContain(card.GetVisualDescendants(), control => control is ScrollViewer or ItemsControl);
        context.ViewModel.Operations.PrimaryActionCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(initialButton, split.TranslatePoint(default, context.Window)!.Value);
        AssertFloatingPlacement();
        foreach (var button in card.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
            AssertControlInsideWindow(button, context.Window);

        void AssertFloatingPlacement()
        {
            AssertControlInsideWindow(card, context.Window);
            var position = card.TranslatePoint(default, context.Window)!.Value;
            var button = split.TranslatePoint(default, context.Window)!.Value;
            Assert.InRange(Math.Abs(position.X + card.Bounds.Width - button.X - split.Bounds.Width), 0, 1);
            Assert.InRange(button.Y - position.Y - card.Bounds.Height, 15, 17);
        }
    }
}
