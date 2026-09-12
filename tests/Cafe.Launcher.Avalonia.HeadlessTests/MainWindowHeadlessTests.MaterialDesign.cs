using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

// M3 color-role contract in the rendered tree. Two families are pinned here: the
// checked-state fill/foreground pair of controls that keep the Fluent base
// template (Fluent hardcodes #FF0078D7, which does not follow the dynamic
// scheme), and the error text drawn on a dialog surface, which must stay
// readable in both themes and under both neutral strategies.
public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void ResourcePanel_CheckedRowCheckBox_UsesLauncherPrimary()
    {
        using var context = CreateContext();
        context.Window.Show();
        ShowResourcePanel(context);
        Dispatcher.UIThread.RunJobs();
        var checkBox = context.Window.GetVisualDescendants().OfType<CheckBox>()
            .First(control => control.IsEffectivelyVisible);
        checkBox.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var rectangle = Assert.IsType<Border>(checkBox.GetVisualDescendants()
            .First(control => control is Border && control.Name == "NormalRectangle"));
        var expected = Assert.IsType<SolidColorBrush>(Application.Current!.FindResource("Launcher.Color.Primary"));
        Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(rectangle.Background).Color);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SetupWizard_EmptyPathInEitherTheme_KeepsErrorTextReadable(bool isDark, bool seedFollowing)
    {
        using var context = CreateContext();
        var application = Application.Current!;
        var previousTheme = application.RequestedThemeVariant;
        try
        {
            context.ViewModel.IsMotionReduced = true;
            context.Window.Show();
            context.ViewModel.Dialogs.ShowSetupWizard();
            context.ViewModel.Dialogs.SetupWizard.Step = 1;
            context.ViewModel.Dialogs.SetupWizard.GamePath = "";
            Dispatcher.UIThread.RunJobs();
            context.Window.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
            application.RequestedThemeVariant = context.Window.RequestedThemeVariant;
            SettingsAppearanceViewModel.ApplyScheme(
                Color.Parse("#6750A4"),
                isDark: isDark,
                neutralStrategy: seedFollowing ? NeutralColorStrategies.SeedFollowing : NeutralColorStrategies.BrandBlue);
            Dispatcher.UIThread.RunJobs();

            var error = context.Window.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.IsEffectivelyVisible
                    && control.Text == context.ViewModel.Shell.I18n["setupWizardGamePathEmpty"]);
            var surface = error.GetVisualAncestors().OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>().First();
            var foreground = Assert.IsType<SolidColorBrush>(error.Foreground).Color;
            var background = Assert.IsType<SolidColorBrush>(surface.Background).Color;
            double contrast = ColorUtils.GetContrastRatio(foreground, background);
            Assert.True(contrast >= 4.5, $"Error text contrast is {contrast:F2}:1.");
        }
        finally
        {
            application.RequestedThemeVariant = previousTheme;
            SettingsAppearanceViewModel.ApplyScheme(Color.Parse("#FF2E7DF6"));
        }
    }
}
