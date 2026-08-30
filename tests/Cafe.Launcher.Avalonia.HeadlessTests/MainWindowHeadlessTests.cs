using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class MainWindowHeadlessTests
{
    private static readonly (string Code, Type SectionType)[] SettingsSections =
    [
        (SettingsCategoryCodes.General, typeof(SettingsGeneralSection)),
        (SettingsCategoryCodes.Game, typeof(SettingsGameSection)),
        (SettingsCategoryCodes.DownloadNetwork, typeof(SettingsDownloadNetworkSection)),
        (SettingsCategoryCodes.Appearance, typeof(SettingsAppearanceSection)),
        (SettingsCategoryCodes.Advanced, typeof(SettingsAdvancedSection)),
        (SettingsCategoryCodes.About, typeof(SettingsAboutSection))
    ];

    [AvaloniaFact]
    public void DownloadRunningChanged_FromWorkerThread_NotifiesOnUiThread()
    {
        var journey = new ThreadAwareGameOperationJourney();
        using var context = CreateContext(new FixedGameOperationJourneyFactory(journey));
        bool? notificationHasUiAccess = null;
        context.ViewModel.Operations.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GameOperationsViewModel.IsDownloadRunning))
            {
                notificationHasUiAccess = Dispatcher.UIThread.CheckAccess();
            }
        };

        var worker = Task.Run(() => journey.SetDownloadRunning(true));
        Assert.True(worker.Wait(TimeSpan.FromSeconds(5)));

        Assert.Null(notificationHasUiAccess);
        Dispatcher.UIThread.RunJobs();
        Assert.True(notificationHasUiAccess);
        Assert.True(context.ViewModel.Operations.IsDownloadRunning);
    }

    [AvaloniaFact]
    public void DownloadRunningChanged_WhenNewerStateArrives_DropsStaleWorkerNotification()
    {
        var journey = new ThreadAwareGameOperationJourney();
        using var context = CreateContext(new FixedGameOperationJourneyFactory(journey));
        using var workerRaisedNotification = new ManualResetEventSlim();
        using var completeWorker = new ManualResetEventSlim();
        var notificationCount = 0;
        context.ViewModel.Operations.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GameOperationsViewModel.IsDownloadRunning))
            {
                notificationCount++;
            }
        };

        var worker = Task.Run(() =>
        {
            journey.SetDownloadRunning(true);
            workerRaisedNotification.Set();
            completeWorker.Wait();
        });
        Assert.True(workerRaisedNotification.Wait(TimeSpan.FromSeconds(5)));
        journey.SetDownloadRunning(false);
        completeWorker.Set();
        Assert.True(worker.Wait(TimeSpan.FromSeconds(5)));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, notificationCount);
        Assert.False(context.ViewModel.Operations.IsDownloadRunning);
    }

    [AvaloniaFact]
    public void DownloadRunningChanged_AfterOperationsDisposed_IgnoresStaleJourneyCallback()
    {
        var journey = new ThreadAwareGameOperationJourney();
        using var context = CreateContext(new FixedGameOperationJourneyFactory(journey));
        var notificationCount = 0;
        context.ViewModel.Operations.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GameOperationsViewModel.IsDownloadRunning))
            {
                notificationCount++;
            }
        };

        context.ViewModel.Operations.Dispose();
        context.ViewModel.Operations.Dispose();
        journey.RaiseStaleRunningChanged();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, notificationCount);
    }

    [AvaloniaFact]
    public void ApplyProgress_FromWorkerThread_QueuesUiUpdate()
    {
        using var context = CreateContext();
        var worker = Task.Run(() => context.ViewModel.Operations.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading,
            Progress = 50
        }));
        Assert.True(worker.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(context.ViewModel.Operations.IsProgressPanelVisible);

        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Operations.IsProgressPanelVisible);
        Assert.Equal(50, context.ViewModel.Operations.ProgressValue);
    }

    [AvaloniaFact]
    public void Settings_WhenLanguageChanges_RefreshesVisibleCategoryTitle()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var categoryTitle = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("category-title"));
        var english = categoryTitle.Text;

        context.ViewModel.Shell.ApplyLanguage(
            LauncherLanguages.SimplifiedChinese,
            context.ViewModel.Settings,
            context.ViewModel.ResourcePanel,
            hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(english, categoryTitle.Text);
        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.First(option =>
                option.Code == context.ViewModel.Settings.SelectedCategory).DisplayName,
            categoryTitle.Text);
    }

    [AvaloniaFact]
    public void SettingsAbout_InlineLegalLinks_DoNotExpandCaptionLineHeight()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.About;
        Dispatcher.UIThread.RunJobs();

        var links = context.Window
            .GetVisualDescendants()
            .OfType<HyperlinkButton>()
            .Where(control => control.Classes.Contains("inline-legal-link"))
            .ToArray();

        Assert.Equal(2, links.Length);
        Assert.All(links, link =>
        {
            Assert.InRange(link.Bounds.Height, 1, 20);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(link)));

            var caption = link
                .GetVisualAncestors()
                .OfType<TextBlock>()
                .Single();

            Assert.Equal(caption.FontSize, link.FontSize);
        });
    }

    [AvaloniaFact]
    public void MainWindow_WhenShown_LoadsRealXamlAndOverlayBindings()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var settingsOverlay = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-overlay"));
        Assert.False(settingsOverlay.IsVisible);

        context.ViewModel.WindowChrome.IsSettingsVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(settingsOverlay.IsVisible);
        Assert.Single(context.Window.GetVisualDescendants().OfType<MainWindowSettingsOverlay>());
        Assert.Single(context.Window.GetVisualDescendants().OfType<MainWindowDialogsOverlay>());
        Assert.Single(context.Window.GetVisualDescendants().OfType<MainWindowToastOverlay>());
    }

    [AvaloniaFact]
    public void MainWindow_TitleBarDragHitTesting_ExcludesInteractiveControlsAndNonTitleContent()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var titleBar = context.Window.GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Name == "TitleBar");
        var settingsButton = titleBar.GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("settings"));
        var title = titleBar.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Classes.Contains("titlebar-brand"));
        var outsideTitleBar = context.Window.GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("operation-layout"));

        Assert.True(context.Window.IsWithinTitleBar(settingsButton));
        Assert.True(context.Window.IsWithinTitleBar(title));
        Assert.True(MainWindow.IsInteractive(settingsButton));
        Assert.False(MainWindow.IsInteractive(title));
        Assert.False(context.Window.IsWithinTitleBar(outsideTitleBar));
    }

    [AvaloniaFact]
    public void MainWindow_BannerControls_HideAfterPointerLeavesBannerStage()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = false,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/1" },
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/2" }
                    ]
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var bannerStage = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("banner-stage"));
        var navigationButtons = bannerStage
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.Classes.Contains("carousel-navigation"))
            .ToArray();
        var edgeGradients = bannerStage
            .GetVisualDescendants()
            .OfType<Border>()
            .Where(control => control.Classes.Contains("banner-edge-gradient"))
            .ToArray();
        Assert.Equal(2, navigationButtons.Length);
        Assert.Equal(2, edgeGradients.Length);
        Assert.All(navigationButtons, button => Assert.Equal(0, button.Opacity));
        Assert.All(edgeGradients, gradient => Assert.Equal(0, gradient.Opacity));

        var bannerTopLeft = bannerStage.TranslatePoint(default, context.Window);
        Assert.NotNull(bannerTopLeft);
        context.Window.MouseMove(
            bannerTopLeft.Value + new Point(bannerStage.Bounds.Width / 2, bannerStage.Bounds.Height / 2),
            RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.All(navigationButtons, button => Assert.Equal(1, button.Opacity));
        Assert.All(edgeGradients, gradient => Assert.Equal(1, gradient.Opacity));

        var firstButtonTopLeft = navigationButtons[0].TranslatePoint(default, context.Window);
        Assert.NotNull(firstButtonTopLeft);
        var firstButtonCenter = firstButtonTopLeft.Value
            + new Point(navigationButtons[0].Bounds.Width / 2, navigationButtons[0].Bounds.Height / 2);
        context.Window.MouseDown(firstButtonCenter, MouseButton.Left, RawInputModifiers.None);
        context.Window.MouseUp(firstButtonCenter, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        context.Window.MouseMove(new Point(0, 0), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.All(navigationButtons, button => Assert.Equal(0, button.Opacity));
        Assert.All(edgeGradients, gradient => Assert.Equal(0, gradient.Opacity));
    }

    [AvaloniaFact]
    public void MainWindow_BannerIndicators_AreVisualOnlyAndFollowHoverVisibility()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = false,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/1" },
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/2" }
                    ]
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var bannerStage = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("banner-stage"));
        var indicators = bannerStage
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("banner-indicators"));
        var dots = indicators
            .GetVisualDescendants()
            .OfType<Border>()
            .Where(control => control.Classes.Contains("banner-dot"))
            .ToArray();

        Assert.Equal(2, dots.Length);
        Assert.False(indicators.IsHitTestVisible);
        Assert.All(dots, dot => Assert.False(dot.IsHitTestVisible));
        Assert.DoesNotContain(
            indicators.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("dot"));
        Assert.Equal(0, indicators.Opacity);
        Assert.Equal(12, dots[0].Bounds.Width);
        Assert.Equal(4, dots[1].Bounds.Width);

        var firstDotPosition = dots[0].TranslatePoint(default, indicators);
        var secondDotPosition = dots[1].TranslatePoint(default, indicators);
        Assert.NotNull(firstDotPosition);
        Assert.NotNull(secondDotPosition);
        Assert.Equal(
            8,
            secondDotPosition!.Value.X - (firstDotPosition!.Value.X + dots[0].Bounds.Width));

        var bannerTopLeft = bannerStage.TranslatePoint(default, context.Window);
        Assert.NotNull(bannerTopLeft);
        context.Window.MouseMove(
            bannerTopLeft!.Value + new Point(bannerStage.Bounds.Width / 2, bannerStage.Bounds.Height / 2),
            RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, indicators.Opacity);

        context.Window.MouseMove(new Point(0, 0), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, indicators.Opacity);
    }

    [AvaloniaFact]
    public void MainWindow_BannerControls_ShowWhileBannerStageIsFocused()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = false,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/1" },
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/2" }
                    ]
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var bannerStage = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("banner-stage"));
        var navigationButtons = bannerStage
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.Classes.Contains("carousel-navigation"))
            .ToArray();
        Assert.Equal(2, navigationButtons.Length);
        Assert.All(navigationButtons, button => Assert.Equal(0, button.Opacity));

        navigationButtons[0].Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.RemoteContent.IsBannerInteractionActive);
        Assert.True(context.ViewModel.RemoteContent.IsCarouselPaused);
        Assert.All(navigationButtons, button => Assert.Equal(1, button.Opacity));
    }

    [AvaloniaFact]
    public void MainWindow_BannerLink_WhenPressed_PreservesBannerCompositionBounds()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = false,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem
                        {
                            BannerImg = "",
                            JumpUrl = "https://banner.example.invalid/1"
                        },
                        new OperationsBannerItem
                        {
                            BannerImg = "",
                            JumpUrl = "https://banner.example.invalid/2"
                        }
                    ]
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var bannerStage = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("banner-stage"));
        var bannerLink = bannerStage
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("banner-link"));
        var bannerMedia = bannerStage
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("banner-media"));
        Assert.Same(bannerLink.Parent, bannerMedia.Parent);
        var bannerTopLeft = bannerLink.TranslatePoint(default, context.Window);
        Assert.NotNull(bannerTopLeft);
        var bannerCenter = bannerTopLeft.Value
            + new Point(bannerLink.Bounds.Width / 2, bannerLink.Bounds.Height / 2);
        var initialMediaBounds = bannerMedia.Bounds;

        context.Window.MouseMove(bannerCenter, RawInputModifiers.None);
        context.Window.MouseDown(bannerCenter, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(initialMediaBounds, bannerMedia.Bounds);

        context.Window.MouseUp(bannerCenter, MouseButton.Left, RawInputModifiers.None);
    }

    [AvaloniaFact]
    public void MainWindow_SocialActions_AddTopRightVerticalButtonsInSourceOrder()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                SocialMediaResource = new SocialMediaResourceResponse
                {
                    SocialMediaResourceOpen = true,
                    SocialMediaResourceList =
                    [
                        new SocialMediaResourceItem
                        {
                            SocialMediaChannel = "YouTube",
                            JumpUrl = "https://youtube.example.invalid"
                        },
                        new SocialMediaResourceItem
                        {
                            SocialMediaChannel = "Discord",
                            JumpUrl = "https://discord.example.invalid"
                        }
                    ]
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var actionButtons = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.Classes.Contains("social-action"))
            .ToArray();

        var officialSiteButton = Assert.Single(
            actionButtons,
            control => control.Classes.Contains("official-site"));
        var itemButtons = actionButtons
            .Where(control => control.DataContext is RemoteContentItem)
            .ToArray();

        Assert.Equal(2, itemButtons.Length);
        Assert.Equal(["YouTube", "Discord"], itemButtons.Select(button => ((RemoteContentItem)button.DataContext!).Title));
        Assert.DoesNotContain(
            context.Window.GetVisualDescendants().OfType<Border>(),
            control => control.Classes.Contains("social-media-card"));
        Assert.All(actionButtons, button =>
        {
            Assert.Equal(36, button.Bounds.Width);
            Assert.Equal(36, button.Bounds.Height);
            AssertControlInsideWindow(button, context.Window);
            Assert.True(AutomationProperties.GetName(button) is not null);
        });

        var officialTop = officialSiteButton.TranslatePoint(default, context.Window);
        var firstTop = itemButtons[0].TranslatePoint(default, context.Window);
        var secondTop = itemButtons[1].TranslatePoint(default, context.Window);
        Assert.NotNull(officialTop);
        Assert.NotNull(firstTop);
        Assert.NotNull(secondTop);
        Assert.True(officialTop.Value.X > context.Window.ClientSize.Width / 2);
        Assert.True(officialTop.Value.Y < firstTop.Value.Y);
        Assert.True(firstTop.Value.Y < secondTop.Value.Y);

        var socialActions = context.Window
            .GetVisualDescendants()
            .OfType<StackPanel>()
            .Single(control => control.Classes.Contains("social-actions"));
        var socialItems = socialActions
            .GetVisualDescendants()
            .OfType<ItemsControl>()
            .Single(control => control.ItemsSource is not null);
        Assert.True(socialActions.IsEffectivelyVisible);
        Assert.True(socialItems.IsEffectivelyVisible);

        context.ViewModel.RemoteContent.UpdateRemoteContentVisibility(false);
        Dispatcher.UIThread.RunJobs();
        Assert.True(socialActions.IsEffectivelyVisible);
        Assert.False(socialItems.IsEffectivelyVisible);
        Assert.False(officialSiteButton.IsEffectivelyVisible);

        context.ViewModel.RemoteContent.UpdateRemoteContentVisibility(true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(socialActions.IsEffectivelyVisible);
        Assert.True(socialItems.IsEffectivelyVisible);
        Assert.True(officialSiteButton.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Toast_WithActions_RendersTitlePrimaryFirstAndDisablesControlsWhileExecuting()
    {
        using var context = CreateContext();
        var release = new TaskCompletionSource<ToastActionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var toastService = context.Provider.GetRequiredService<ToastService>();
        context.Window.Show();
        toastService.Show(new ToastOptions
        {
            Title = "Install failed",
            Message = "Offline",
            Severity = ToastSeverity.Error,
            PrimaryAction = new ToastAction("Retry", _ => release.Task),
            SecondaryAction = new ToastAction(
                "View log",
                _ => Task.FromResult(ToastActionResult.Success()))
        });
        Dispatcher.UIThread.RunJobs();

        var title = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("toast-title"));
        var actionButtons = context.Window.GetVisualDescendants().OfType<Button>()
            .Where(control =>
                control.Classes.Contains("toast-primary-action")
                || control.Classes.Contains("toast-secondary-action"))
            .ToArray();
        var closeButton = context.Window.GetVisualDescendants().OfType<Button>()
            .Single(control => control.Classes.Contains("toast-close"));
        var actionProgress = context.Window.GetVisualDescendants().OfType<ProgressBar>()
            .Single(control => control.Classes.Contains("toast-progress"));

        Assert.Equal("Install failed", title.Text);
        Assert.Equal(2, actionButtons.Length);
        Assert.Contains("toast-primary-action", actionButtons[0].Classes);
        Assert.Contains("toast-secondary-action", actionButtons[1].Classes);
        Assert.Equal("Retry", AutomationProperties.GetName(actionButtons[0]));
        Assert.Equal("View log", AutomationProperties.GetName(actionButtons[1]));
        Assert.True(actionProgress.IsIndeterminate);
        Assert.False(actionProgress.IsVisible);

        var executeTask = context.ViewModel.Toasts.ExecutePrimaryToastActionCommand.ExecuteAsync(
            context.ViewModel.Toasts.ActiveToasts.Single().Id);
        Dispatcher.UIThread.RunJobs();

        Assert.All(actionButtons, button => Assert.False(button.IsEffectivelyEnabled));
        Assert.True(closeButton.IsEffectivelyEnabled);
        Assert.True(actionProgress.IsEffectivelyVisible);

        release.SetResult(ToastActionResult.Failure("Still offline", "Retry failed"));
        await executeTask;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Toast_WithoutActions_ShowsNoProgressBar()
    {
        using var context = CreateContext();
        var toastService = context.Provider.GetRequiredService<ToastService>();
        context.Window.Show();
        toastService.Show(new ToastOptions
        {
            Title = "Updated",
            Message = "You are up to date.",
            DurationMs = 60000
        });
        Dispatcher.UIThread.RunJobs();

        var visibleProgress = context.Window.GetVisualDescendants().OfType<ProgressBar>()
            .Where(control => control.Classes.Contains("toast-progress") && control.IsVisible)
            .ToArray();

        Assert.Empty(visibleProgress);
    }

    [AvaloniaFact]
    public void ConfigureViewModel_WiresAndUnwiresPlatformCapabilities()
    {
        var context = CreateContext();
        var viewModel = context.ViewModel;

        Assert.NotNull(viewModel.Settings.PickGameFolderAsync);
        Assert.NotNull(viewModel.Settings.PickBackgroundImageAsync);
        Assert.NotNull(viewModel.Settings.PickBackgroundFolderAsync);
        Assert.NotNull(viewModel.Background.PickBackgroundImageAsync);
        Assert.NotNull(viewModel.Background.PickBackgroundFolderAsync);
        Assert.NotNull(viewModel.LogViewer.PickExportDirectoryAsync);
        Assert.NotNull(viewModel.LogViewer.OpenExportDirectory);
        Assert.NotNull(viewModel.Debug.PickExportDirectoryAsync);
        Assert.NotNull(viewModel.Debug.OpenDirectory);

        context.Dispose();

        Assert.Null(viewModel.Settings.PickGameFolderAsync);
        Assert.Null(viewModel.Settings.PickBackgroundImageAsync);
        Assert.Null(viewModel.Settings.PickBackgroundFolderAsync);
        Assert.Null(viewModel.Background.PickBackgroundImageAsync);
        Assert.Null(viewModel.Background.PickBackgroundFolderAsync);
        Assert.Null(viewModel.LogViewer.PickExportDirectoryAsync);
        Assert.Null(viewModel.LogViewer.OpenExportDirectory);
        Assert.Null(viewModel.Debug.PickExportDirectoryAsync);
        Assert.Null(viewModel.Debug.OpenDirectory);
    }

    [AvaloniaFact]
    public void DebugEntry_VisibilityMatchesBuildConfiguration()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
        var button = context.Window.GetVisualDescendants().OfType<Button>().Single(control =>
            ReferenceEquals(control.Command, context.ViewModel.WindowChrome.OpenDebugPanelCommand));

#if DEBUG
        Assert.True(button.IsEffectivelyVisible);
#else
        Assert.False(button.IsEffectivelyVisible);
#endif
    }

    [AvaloniaFact]
    public void DebugResetSettings_WhenRequested_ShowsConfirmationDialog()
    {
        using var context = CreateContext();

        context.ViewModel.Debug.ResetSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.IsDebugResetConfirmationVisible);
    }

    [AvaloniaFact]
    public void SettingsWorkspace_WhenShown_AppliesWorkspaceStyle()
    {
        using var context = CreateContext();
        context.ViewModel.WindowChrome.IsSettingsVisible = true;

        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var workspace = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-workspace"));
        Assert.Equal(new Thickness(0), workspace.Margin);
    }

    [AvaloniaFact]
    public void SettingsWorkspace_HorizontalInsetsStayAligned()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var settingsOverlay = context.Window.GetVisualDescendants()
            .OfType<MainWindowSettingsOverlay>()
            .Single();
        var dialog = settingsOverlay.GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>()
            .Single();
        var navigation = GetSettingsNavigation(context.Window);
        var selectedItem = navigation.ContainerFromIndex(navigation.SelectedIndex)
            ?? throw new InvalidOperationException("Selected settings item was not realized.");
        var content = settingsOverlay.GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-content"));
        var contentViewport = settingsOverlay.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("dialog-scroll"));

        var dialogTopLeft = dialog.TranslatePoint(default, context.Window);
        var selectedItemTopLeft = selectedItem.TranslatePoint(default, context.Window);
        var contentTopLeft = content.TranslatePoint(default, context.Window);
        var contentViewportTopLeft = contentViewport.TranslatePoint(default, context.Window);

        Assert.NotNull(dialogTopLeft);
        Assert.NotNull(selectedItemTopLeft);
        Assert.NotNull(contentTopLeft);
        Assert.NotNull(contentViewportTopLeft);

        var dialogLeftInset = selectedItemTopLeft!.Value.X - dialogTopLeft!.Value.X;
        var contentLeftInset = contentViewportTopLeft!.Value.X - contentTopLeft!.Value.X;
        var contentRightInset = contentViewport.Padding.Right;

        Assert.InRange(Math.Abs(dialogLeftInset - 16), 0, 1);
        Assert.InRange(Math.Abs(contentLeftInset - dialogLeftInset), 0, 1);
        Assert.InRange(Math.Abs(contentRightInset - dialogLeftInset), 0, 1);
    }

    [AvaloniaFact]
    public void SettingsTypography_WhenShown_AppliesNormalAndStrongWeights()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var textBlocks = context.Window.GetVisualDescendants().OfType<TextBlock>().ToArray();
        Assert.Equal(
            FontWeight.SemiBold,
            textBlocks.Single(control =>
                control.Classes.Contains("dialog-title")
                && control.IsEffectivelyVisible).FontWeight);
        Assert.Equal(
            FontWeight.SemiBold,
            textBlocks.Single(control => control.Classes.Contains("category-title")).FontWeight);
        Assert.All(
            textBlocks.Where(control => control.Classes.Contains("group-title")),
            control => Assert.Equal(FontWeight.SemiBold, control.FontWeight));
        Assert.Equal(
            FontWeight.Normal,
            textBlocks.First(control => control.Classes.Contains("caption")).FontWeight);

        var navigation = GetSettingsNavigation(context.Window);
        var selectedItem = navigation.ContainerFromIndex(navigation.SelectedIndex)
            ?? throw new InvalidOperationException("Selected settings item was not realized.");
        var selectedText = selectedItem.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Classes.Contains("settings-navigation-item"));
        Assert.Equal(FontWeight.SemiBold, selectedText.FontWeight);
    }

    [AvaloniaFact]
    public void LanguageFont_WhenLanguageChanges_UpdatesWindowAndInheritedText()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var visibleText = context.Window
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .First(control => control.IsEffectivelyVisible);

        Assert.Equal("Segoe UI", context.Window.FontFamily.Name);
        Assert.Equal("Segoe UI", visibleText.FontFamily.Name);

        context.ViewModel.Shell.ApplyLanguage(
            LauncherLanguages.TraditionalChinese,
            context.ViewModel.Settings,
            context.ViewModel.ResourcePanel,
            hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Microsoft JhengHei UI", context.Window.FontFamily.Name);
        Assert.Equal("Microsoft JhengHei UI", visibleText.FontFamily.Name);

        context.ViewModel.Shell.ApplyLanguage(
            LauncherLanguages.Japanese,
            context.ViewModel.Settings,
            context.ViewModel.ResourcePanel,
            hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Yu Gothic UI", context.Window.FontFamily.Name);
        Assert.Equal("Yu Gothic UI", visibleText.FontFamily.Name);
    }

    [AvaloniaFact]
    public void SettingsWorkspace_WhenOpened_ShowsOnlyGeneralSection()
    {
        using var context = CreateContext();
        OpenSettings(context);

        Assert.Equal(SettingsCategoryCodes.General, context.ViewModel.Settings.SelectedCategory);
        AssertVisibleSettingsSection(context.Window, typeof(SettingsGeneralSection));
    }

    [AvaloniaFact]
    public void SettingsNavigation_WhenOpened_FocusesNavigationAndShowsSelectedState()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var navigation = GetSettingsNavigation(context.Window);
        Assert.Equal(SettingsCategoryCodes.General, context.ViewModel.Settings.SelectedCategory);
        Assert.True(navigation.IsKeyboardFocusWithin);
        AssertNavigationSelectionVisual(navigation, SettingsCategoryCodes.General);
    }

    [AvaloniaFact]
    public async Task SettingsNavigation_AfterSave_KeepsSelectedItemVisuallySelected()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var navigation = GetSettingsNavigation(context.Window);

        context.ViewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;
        await context.ViewModel.Settings.SaveSettingsCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(navigation.IsKeyboardFocusWithin);
        AssertNavigationSelectionVisual(navigation, SettingsCategoryCodes.General);
    }

    [AvaloniaFact]
    public void SettingsWorkspace_WhenEachExactCodeIsSelected_ShowsOnlyItsSection()
    {
        using var context = CreateContext();
        OpenSettings(context);

        foreach (var (code, sectionType) in SettingsSections)
        {
            context.ViewModel.Settings.SelectedCategory = code;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(code, context.ViewModel.Settings.SelectedCategory);
            AssertVisibleSettingsSection(context.Window, sectionType);
        }
    }

    [AvaloniaFact]
    public void SettingsNavigation_PreservesDraftDirtyStateWithoutSaving()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var savedCount = 0;
        context.ViewModel.Settings.SettingsSaved += () =>
        {
            savedCount++;
            return Task.CompletedTask;
        };
        context.ViewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;
        Assert.True(context.ViewModel.Settings.IsSettingsDirty);

        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.General;

        Assert.Equal(LauncherLanguages.Japanese, context.ViewModel.Settings.Editor.Current.Language);
        Assert.True(context.ViewModel.Settings.IsSettingsDirty);
        Assert.Equal(0, savedCount);
    }

    [AvaloniaFact]
    public void SettingsCategory_IsSessionScopedAndNewViewModelDefaultsToGeneral()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.DownloadNetwork;
        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);
        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);

        Assert.Equal(SettingsCategoryCodes.DownloadNetwork, context.ViewModel.Settings.SelectedCategory);

        using var newContext = CreateContext();
        Assert.Equal(SettingsCategoryCodes.General, newContext.ViewModel.Settings.SelectedCategory);
    }

    [AvaloniaFact]
    public void SettingRow_RendersAllFields_WithExplicitActionProperty()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.General;
        Dispatcher.UIThread.RunJobs();

        var rows = context.Window.GetVisualDescendants().OfType<global::Cafe.Launcher.Avalonia.Controls.SettingRow>().ToArray();
        Assert.NotEmpty(rows);

        foreach (var row in rows)
        {
            var titleText = row.FindControl<TextBlock>("RowTitle");
            Assert.NotNull(titleText);
            Assert.Equal(row.Title, titleText!.Text);

            var descText = row.FindControl<TextBlock>("RowDescription");
            Assert.NotNull(descText);
            Assert.Equal(row.Description, descText!.Text);

            var actionPresenter = row.FindControl<ContentPresenter>("ActionPresenter");
            Assert.NotNull(actionPresenter);
            Assert.Equal(row.Action, actionPresenter!.Content);
        }
    }

    [AvaloniaTheory]
    [InlineData(1300, 754)]
    [InlineData(1024, 640)]
    public void SettingsAdvanced_AtSupportedWindowSizes_AlignsDedicatedLogActionRow(
        double width,
        double height)
    {
        using var context = CreateContext();
        context.Window.Width = width;
        context.Window.Height = height;
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Advanced;
        Dispatcher.UIThread.RunJobs();

        var section = context.Window
            .GetVisualDescendants()
            .OfType<SettingsAdvancedSection>()
            .Single();
        var rows = section
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.SettingRow>()
            .Where(row => row.IsEffectivelyVisible)
            .ToArray();

        // 日志级别 / 日志文件操作 / 重置设置三行。
        Assert.Equal(3, rows.Length);
        var levelRow = Assert.Single(rows, row => row.GetVisualDescendants().OfType<ComboBox>().Any());
        var levelControl = levelRow.GetVisualDescendants().OfType<ComboBox>().Single();
        var logRow = Assert.Single(rows, row => row.GetVisualDescendants().OfType<Button>().Count() == 3);
        var logButtons = logRow
            .GetVisualDescendants()
            .OfType<Button>()
            .ToArray();
        Assert.Equal(3, logButtons.Length);
        var resetButton = rows
            .SelectMany(row => row.GetVisualDescendants().OfType<Button>())
            .Single(button => button.Classes.Contains("danger-action"));
        Assert.Contains("flat-action", resetButton.Classes);
        Assert.Equal(logButtons[0].Bounds.Height, resetButton.Bounds.Height);

        var levelTopLeft = levelControl.TranslatePoint(default, context.Window);
        Assert.NotNull(levelTopLeft);
        var levelRight = levelTopLeft.Value.X + levelControl.Bounds.Width;
        var logPresenter = logRow.FindControl<ContentPresenter>("ActionPresenter");
        Assert.NotNull(logPresenter);
        var logPresenterTopLeft = logPresenter!.TranslatePoint(default, context.Window);
        Assert.NotNull(logPresenterTopLeft);
        var logPresenterRight = logPresenterTopLeft.Value.X + logPresenter.Bounds.Width;
        Assert.InRange(Math.Abs(levelRight - logPresenterRight), 0, 1);

        var description = rows[1].FindControl<TextBlock>("RowDescription");
        Assert.NotNull(description);
        var descriptionTopLeft = description!.TranslatePoint(default, context.Window);
        var firstButtonTopLeft = logButtons[0].TranslatePoint(default, context.Window);
        Assert.NotNull(descriptionTopLeft);
        Assert.NotNull(firstButtonTopLeft);
        Assert.True(
            descriptionTopLeft.Value.X + description.Bounds.Width
            <= firstButtonTopLeft.Value.X);

        AssertControlInsideWindow(levelControl, context.Window);
        Assert.All(logButtons, button => AssertControlInsideWindow(button, context.Window));
    }

    [AvaloniaFact]
    public void SettingsGame_DangerAction_MatchesSiblingFlatActionHeight()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Game;
        Dispatcher.UIThread.RunJobs();

        var section = context.Window
            .GetVisualDescendants()
            .OfType<SettingsGameSection>()
            .Single();
        var managementRow = section
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.SettingRow>()
            .Single(row => row.GetVisualDescendants().OfType<Button>().Count() == 2);
        var buttons = managementRow.GetVisualDescendants().OfType<Button>().ToArray();
        var repairButton = Assert.Single(
            buttons,
            button => button.Classes.Contains("flat-action") && !button.Classes.Contains("danger-action"));
        var uninstallButton = Assert.Single(buttons, button => button.Classes.Contains("danger-action"));

        Assert.Contains("flat-action", uninstallButton.Classes);
        Assert.Equal(repairButton.Bounds.Height, uninstallButton.Bounds.Height);
    }

    [AvaloniaFact]
    public void SettingsSaving_DisablesNavigationButKeepsFooterVisible()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var navigation = GetSettingsNavigation(context.Window);
        var settingsOverlay = context.Window.GetVisualDescendants()
            .OfType<MainWindowSettingsOverlay>().Single();
        var footer = settingsOverlay.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("settings-content-actions"));

        context.ViewModel.Settings.IsSaving = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(navigation.IsEnabled);
        Assert.True(footer.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void SettingsAppearance_WhenCustomBackgroundOptionsOverflow_ShowsScrollableContent()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.Editor.Current.ThemeColorMode = ThemeColorModes.Wallpaper;
        context.ViewModel.Settings.Editor.Current.BackgroundSource = BackgroundSources.Custom;
        context.ViewModel.Settings.Editor.Current.BackgroundFit = BackgroundFits.Uniform;
        Dispatcher.UIThread.RunJobs();

        var scrollViewer = context.Window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("dialog-scroll"));

        Assert.True(scrollViewer.Extent.Height > scrollViewer.Viewport.Height);

        var verticalScrollBar = scrollViewer.GetVisualDescendants()
            .OfType<ScrollBar>()
            .Single(control => control.Orientation == Orientation.Vertical);
        Assert.True(verticalScrollBar.IsEffectivelyVisible);

        var settingsOverlay = context.Window.GetVisualDescendants()
            .OfType<MainWindowSettingsOverlay>()
            .Single();
        var dialog = settingsOverlay.GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>()
            .Single();
        var dialogTopLeft = dialog.TranslatePoint(default, context.Window);
        var scrollBarTopLeft = verticalScrollBar.TranslatePoint(default, context.Window);
        Assert.NotNull(dialogTopLeft);
        Assert.NotNull(scrollBarTopLeft);

        var dialogRight = dialogTopLeft!.Value.X + dialog.Bounds.Width;
        var scrollBarRight = scrollBarTopLeft!.Value.X + verticalScrollBar.Bounds.Width;
        Assert.InRange(Math.Abs(dialogRight - scrollBarRight), 0, 1);
    }

    [AvaloniaFact]
    public void SettingsOverlay_AtMinimumWindowSize_KeepsDialogAndFooterVisible()
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        OpenSettings(context);

        var settingsOverlay = context.Window
            .GetVisualDescendants()
            .OfType<MainWindowSettingsOverlay>()
            .Single();
        var dialog = settingsOverlay
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>()
            .Single();
        var footer = dialog
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("settings-content-actions"));
        var footerTopLeft = footer.TranslatePoint(default, dialog);

        Assert.True(dialog.Bounds.Width <= context.Window.ClientSize.Width - 48);
        Assert.True(dialog.Bounds.Height <= context.Window.ClientSize.Height - 48);
        Assert.True(dialog.Bounds.Height > 0);
        Assert.True(footer.IsEffectivelyVisible);
        Assert.NotNull(footerTopLeft);
        Assert.True(footerTopLeft.Value.Y + footer.Bounds.Height <= dialog.Bounds.Height);
    }

    [AvaloniaFact]
    public void SettingsFooterActions_AlignWithContentHeaderRightEdge()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var headerCloseButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("content-header-action"));
        var saveButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(control => ReferenceEquals(
                control.Command,
                context.ViewModel.Settings.SaveSettingsCommand));
        var headerTopLeft = headerCloseButton.TranslatePoint(default, context.Window);
        var saveTopLeft = saveButton.TranslatePoint(default, context.Window);

        Assert.NotNull(headerTopLeft);
        Assert.NotNull(saveTopLeft);
        var headerRight = headerTopLeft.Value.X + headerCloseButton.Bounds.Width;
        var saveRight = saveTopLeft.Value.X + saveButton.Bounds.Width;
        Assert.InRange(Math.Abs(headerRight - saveRight), 0, 1);
    }

    [AvaloniaTheory]
    [InlineData("resource-panel")]
    [InlineData("log-viewer")]
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
    public void SetupWizard_InJapaneseAtMinimumWindowSize_KeepsScrollableContentAndNavigationReachable()
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Shell.ApplyLanguage(
            LauncherLanguages.Japanese,
            context.ViewModel.Settings,
            context.ViewModel.ResourcePanel,
            hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        var wizard = context.Window.GetVisualDescendants().OfType<SetupWizardOverlay>().Single();
        var content = wizard.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("scroll-pad"));
        var next = GetWizardNextButton(context.Window, context.ViewModel);

        Assert.True(content.IsEffectivelyVisible);
        Assert.True(content.Viewport.Height > 0);
        Assert.True(next.IsEffectivelyVisible);
        AssertControlInsideWindow(next, context.Window);
    }

    [AvaloniaTheory]
    [InlineData("install")]
    [InlineData("progress")]
    [InlineData("control")]
    public void MainWindow_AtMinimumWindowSize_RemoteContentDoesNotOverlapOperationPanel(
        string panelMode)
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.ViewModel.RemoteContent.IsPanelVisible = true;
        context.ViewModel.Operations.PanelMode = panelMode switch
        {
            "install" => GameOperationPanelMode.Install,
            "progress" => GameOperationPanelMode.Progress,
            "control" => GameOperationPanelMode.Control,
            _ => throw new ArgumentOutOfRangeException(nameof(panelMode)),
        };
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var remotePanel = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("remote-surface"));
        // ADR-016：三种状态共享同一任务容器，底栏对遥测面板的让位约束对所有模式一致。
        var operationPanel = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));

        Assert.True(remotePanel.IsEffectivelyVisible);
        Assert.True(
            remotePanel.Bounds.Bottom <= operationPanel.Bounds.Top,
            $"Remote panel bottom {remotePanel.Bounds.Bottom} overlaps operation panel top {operationPanel.Bounds.Top}.");
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenPanelModeChanges_TaskSurfaceAnimatesThenSettles()
    {
        using var context = CreateContext();
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));
        Assert.True(surface.Bounds.Height > 0);
        var installedHeight = surface.Bounds.Height;

        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        // 形变完成后回到自动尺寸、恢复全不透明，且控制态自然高度大于安装态（156 对 132），
        // 证明转换走过了"测新状态自然高度"的管线而非瞬切。
        var settled = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { });
            await Task.Delay(10);
            if (double.IsNaN(surface.Height) && surface.Opacity >= 1d)
            {
                settled = true;
                break;
            }
        }

        Assert.True(settled, "Operation surface did not settle back to auto height and full opacity.");
        Assert.True(
            surface.Bounds.Height > installedHeight,
            $"Control state height {surface.Bounds.Height} did not grow past install state {installedHeight}.");
    }

    [AvaloniaFact]
    public void MainWindow_WhenMotionReduced_TaskSurfaceSwitchesWithoutAnimation()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = true;
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));
        Assert.True(surface.Bounds.Height > 0);

        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        Dispatcher.UIThread.RunJobs();

        Assert.True(double.IsNaN(surface.Height));
        Assert.Equal(1d, surface.Opacity);
    }

    [AvaloniaFact]
    public async Task MainWindow_BackgroundThreadSwitchAndRuntimeMotionReduction_KeepTaskSurfaceConsistent()
    {
        using var context = CreateContext();
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        // 后台线程（真实应用中进度回调的常见来源）触发面板切换，须经 Dispatcher 汇入 UI 线程。
        await Task.Run(() => context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control);
        var controlState = default(Border);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { });
            controlState = context.Window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(control => control.Classes.Contains("operation-state")
                    && control.Classes.Contains("control-panel"));
            if (controlState?.IsVisible == true)
            {
                break;
            }

            await Task.Delay(10);
        }

        Assert.True(controlState?.IsVisible == true, "Control state did not surface after background switch.");

        // 运行期关闭动效：形变立即落定（自动高度、全不透明），状态本身不受影响。
        context.ViewModel.IsMotionReduced = true;
        Dispatcher.UIThread.RunJobs();

        var surface = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));
        Assert.True(double.IsNaN(surface.Height));
        Assert.Equal(1d, surface.Opacity);
        Assert.True(controlState.IsVisible);

        // 关闭窗口触发退订与表面落定，保证无泄漏的取消源残留。
        context.Window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenWallpaperCrossFadeEnds_CrossFadeSourceIsCleared()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var crossFade = context.Window.GetVisualDescendants().OfType<Image>()
            .Single(image => image.Name == "BackgroundCrossFade");

        // 切换到自定义壁纸触发 ADR-016 交叉淡化：旧图所有权在 ViewModel（宽限期后释放），
        // 视图层必须在淡化结束后摘除引用，否则视觉树残留已释放位图——DevTools 悬停/选择
        // 元素读取 Image.Source 的 PixelSize 会抛 ObjectDisposedException 使进程崩溃。
        var wallpaperPath = Path.Combine(
            Path.GetTempPath(),
            $"launcher-wallpaper-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(
            wallpaperPath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="));
        try
        {
            var settings = new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = wallpaperPath,
                BackgroundFit = BackgroundFits.UniformToFill,
                ThemeColorMode = ThemeColorModes.Default
            };
            await context.ViewModel.Background.UpdateBackgroundImageAsync(
                settings,
                snapshot: null,
                CancellationToken.None);

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (crossFade.Source is not null && DateTime.UtcNow < deadline)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { });
                await Task.Delay(10);
            }

            Assert.Null(crossFade.Source);
        }
        finally
        {
            File.Delete(wallpaperPath);
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenPanelModeSwitchesRapidly_SurfaceSettlesAtLatestStateNaturalHeight()
    {
        using var context = CreateContext();
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));
        var installHeight = surface.Bounds.Height;
        Assert.True(installHeight > 0);

        // 单次切换参考值：控制态自然高度大于安装态，落定后回到自动高度与全不透明。
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        var controlHeight = await WaitUntilOperationSurfaceSettledAsync(surface);
        Assert.True(controlHeight > installHeight,
            $"Control state height {controlHeight} did not grow past install state {installHeight}.");

        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        var reSettledHeight = await WaitUntilOperationSurfaceSettledAsync(surface);
        Assert.True(Math.Abs(reSettledHeight - installHeight) < 0.5,
            $"Re-settled install height {reSettledHeight} deviates from initial {installHeight}.");

        // 快速连续切换（ADR-016：高频变化以最新状态为准，不排队）：Headless 动画不按墙钟
        // 推进、无法采样形变中途，但最终几何必须收敛到最新状态，且不得残留冻结高度或
        // 下沉透明度。
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;

        var finalHeight = await WaitUntilOperationSurfaceSettledAsync(surface);
        Assert.True(Math.Abs(finalHeight - controlHeight) < 0.5,
            $"Height after rapid switches {finalHeight} deviates from control natural height {controlHeight}.");
    }

    [AvaloniaFact]
    public async Task MainWindow_AfterEntranceWindow_EntranceAnchorsAreRetired()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = FindOperationSurface(context.Window);
        var shellRoot = FindShellRoot(context.Window);
        Assert.Contains("motion-enter", surface.Classes);
        Assert.Contains("motion-enter", shellRoot.Classes);

        // 入场窗期（快速档/标准档时长）结束后两类锚点必须摘除：壳层与操作表面恢复
        // 全不透明，且操作表面上升位移归零；此后重启动效偏好时 motion-* 选择器不会
        // 重新匹配而重放入场。
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline
            && (surface.Classes.Contains("motion-enter") || shellRoot.Classes.Contains("motion-enter")))
        {
            await Dispatcher.UIThread.InvokeAsync(() => { });
            await Task.Delay(10);
        }

        Assert.DoesNotContain("motion-enter", surface.Classes);
        Assert.DoesNotContain("motion-enter", shellRoot.Classes);
        Assert.Equal(1d, surface.Opacity);
        Assert.Equal(1d, shellRoot.Opacity);
        var translate = Assert.IsType<TranslateTransform>(surface.RenderTransform);
        Assert.Equal(0, translate.Y);
    }

    [AvaloniaFact]
    public void MainWindow_WhenMotionDisabledDuringEntranceWindow_ShellAnchorRetiresAndOpacityRestores()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var shellRoot = FindShellRoot(context.Window);
        Assert.Contains("motion-enter", shellRoot.Classes);

        // 入场窗期内关闭动效：壳层锚点立即摘除、透明度回到 1，
        // 不得停留在 PlayShellEntranceOnce 入场前写入的 0（否则整壳不可见）。
        context.ViewModel.IsMotionReduced = true;
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("motion-enter", shellRoot.Classes);
        Assert.Equal(1d, shellRoot.Opacity);
    }

    [AvaloniaFact]
    public async Task MainWindow_AfterAnchorsRetired_MotionPreferenceToggleDoesNotRestoreEntranceClasses()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = FindOperationSurface(context.Window);
        var shellRoot = FindShellRoot(context.Window);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline
            && (surface.Classes.Contains("motion-enter") || shellRoot.Classes.Contains("motion-enter")))
        {
            await Dispatcher.UIThread.InvokeAsync(() => { });
            await Task.Delay(10);
        }

        // 运行期关闭再重开动效偏好：锚点类不得回流，否则 motion-enabled 重新匹配会重放入场。
        context.ViewModel.IsMotionReduced = true;
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.IsMotionReduced = false;
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("motion-enter", surface.Classes);
        Assert.DoesNotContain("motion-enter", shellRoot.Classes);
        Assert.Equal(1d, surface.Opacity);
        Assert.Equal(1d, shellRoot.Opacity);
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenPanelModeChangesDuringEntranceWindow_AnchorRetiresEarlyAndSurfaceSettles()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = FindOperationSurface(context.Window);
        Assert.Contains("motion-enter", surface.Classes);

        // 入场窗期内的状态切换必须立即摘除锚点：锚点类动画持有 Opacity，会与转换的
        // 下沉写入/恢复段互相覆盖；摘除后由转换动画独占透明度并正常结算。
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("motion-enter", surface.Classes);

        await WaitUntilOperationSurfaceSettledAsync(surface);
        Assert.Equal(1d, surface.Opacity);
    }

    private static Border FindOperationSurface(Window window) =>
        window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));

    private static Grid FindShellRoot(Window window) =>
        window.GetVisualDescendants().OfType<Grid>()
            .Single(control => control.Name == "ShellRoot");

    private static async Task<double> WaitUntilOperationSurfaceSettledAsync(Border surface)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { });
            await Task.Delay(10);
            if (double.IsNaN(surface.Height) && surface.Opacity >= 1d)
            {
                return surface.Bounds.Height;
            }
        }

        Assert.Fail("Operation surface did not settle back to auto height and full opacity.");
        return double.NaN;
    }

    [AvaloniaFact]
    public async Task MainWindow_BackgroundThreadSwitch_MorphsToLatestStateNaturalHeight()
    {
        using var context = CreateContext();
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var surface = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));
        var installHeight = surface.Bounds.Height;
        Assert.True(installHeight > 0);

        // 后台线程（真实进度回调的常见来源）触发切换时，posted 转换必须在可见性绑定
        // 刷新之后测量，收敛高度应落在控制态自然高度而非起飞前高度。
        await Task.Run(() => context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control);

        var settled = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { });
            await Task.Delay(10);
            if (double.IsNaN(surface.Height) && surface.Opacity >= 1d)
            {
                settled = true;
                break;
            }
        }

        Assert.True(settled, "Operation surface did not settle after background switch.");
        Assert.True(
            surface.Bounds.Height > installHeight,
            $"Control state height {surface.Bounds.Height} did not grow past install state {installHeight}.");
    }

    [AvaloniaFact]
    public void MainWindow_NewsList_WithMoreThanThreeItems_KeepsAllItemsAccessibleByScrolling()
    {
        using var context = CreateContext();
        const string longTitle =
            "This deliberately long launcher news title must be truncated to one visible line without being clipped by the fixed-height clickable row";
        var rows = Enumerable.Range(1, 5)
            .Select(index => new NewsRowItem
            {
                Title = index == 1 ? longTitle : $"News item {index}",
                Link = $"https://news.example.invalid/{index}",
                PublishTime = 1_700_000_000_000 + index
            })
            .ToList();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    NewsList = new NewsListEnvelope
                    {
                        Code = 0,
                        Data = new NewsListData
                        {
                            News =
                            [
                                new NewsTypeItem
                                {
                                    TypeLabel = "News",
                                    Rows = rows
                                }
                            ]
                        }
                    }
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewport = context.Window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("news-viewport"));
        var rowButtons = viewport
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.Classes.Contains("news-row"))
            .ToArray();

        Assert.Equal(5, context.ViewModel.RemoteContent.SelectedNewsCategory?.Items.Count);
        Assert.Equal(5, rowButtons.Length);
        Assert.Equal(184, viewport.Viewport.Height);
        Assert.All(rowButtons, row => Assert.Equal(36, row.Bounds.Height));
        Assert.True(viewport.Extent.Height > viewport.Viewport.Height);
        Assert.Equal(longTitle, ToolTip.GetTip(rowButtons[0]));

        var longTitleText = rowButtons[0]
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Text == longTitle);
        var longTitleTop = longTitleText.TranslatePoint(default, rowButtons[0]);
        Assert.Single(longTitleText.TextLayout.TextLines);
        Assert.True(longTitleText.TextLayout.Height <= longTitleText.Bounds.Height);
        Assert.NotNull(longTitleTop);
        Assert.True(longTitleTop.Value.Y >= 0);
        Assert.True(longTitleTop.Value.Y + longTitleText.Bounds.Height <= rowButtons[0].Bounds.Height);

        viewport.Offset = new Vector(0, viewport.Extent.Height - viewport.Viewport.Height);
        Dispatcher.UIThread.RunJobs();

        var lastRowTop = rowButtons[^1].TranslatePoint(default, viewport);
        Assert.NotNull(lastRowTop);
        Assert.InRange(lastRowTop.Value.Y, 0, viewport.Viewport.Height - rowButtons[^1].Bounds.Height);
    }

    [AvaloniaFact]
    public void MainWindow_NewsList_WithThreeItems_SizesViewportToContent()
    {
        using var context = CreateContext();
        var rows = Enumerable.Range(1, 3)
            .Select(index => new NewsRowItem
            {
                Title = $"News item {index}",
                Link = $"https://news.example.invalid/{index}",
                PublishTime = 1_700_000_000_000 + index
            })
            .ToList();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    NewsList = new NewsListEnvelope
                    {
                        Code = 0,
                        Data = new NewsListData
                        {
                            News =
                            [
                                new NewsTypeItem
                                {
                                    TypeLabel = "News",
                                    Rows = rows
                                }
                            ]
                        }
                    }
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewport = context.Window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("news-viewport"));
        var rowButtons = viewport
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.Classes.Contains("news-row"))
            .ToArray();

        Assert.Equal(3, rowButtons.Length);
        Assert.All(rowButtons, row => Assert.Equal(36, row.Bounds.Height));
        Assert.Equal(124, viewport.Viewport.Height);
        Assert.Equal(viewport.Viewport.Height, viewport.Extent.Height);
    }

    [AvaloniaFact]
    public void MainWindow_RemoteContent_WithOverflow_ScrollsOuterTransparentContainer()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                BaseConfig = new BaseConfigResponse
                {
                    NoticePopOpen = true,
                    NoticeContent = new string('N', 500)
                },
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem
                        {
                            BannerImg = "",
                            JumpUrl = "https://news.example.invalid/banner"
                        }
                    ],
                    NewsList = new NewsListEnvelope
                    {
                        Code = 0,
                        Data = new NewsListData
                        {
                            News =
                            [
                                new NewsTypeItem
                                {
                                    TypeLabel = "News",
                                    Rows = Enumerable.Range(1, 3)
                                        .Select(index => new NewsRowItem
                                        {
                                            Title = $"News item {index}",
                                            Link = $"https://news.example.invalid/{index}",
                                            PublishTime = 1_700_000_000_000 + index
                                        })
                                        .ToList()
                                }
                            ]
                        }
                    }
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var layoutHost = context.Window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("remote-content-layout-host"));

        Assert.True(layoutHost.Extent.Height > layoutHost.Viewport.Height);
    }

    [AvaloniaFact]
    public void MainWindow_NewsTabs_KeepVisualGapBetweenHeaderAndSelectedIndicator()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    NewsList = new NewsListEnvelope
                    {
                        Code = 0,
                        Data = new NewsListData
                        {
                            News =
                            [
                                new NewsTypeItem
                                {
                                    TypeLabel = "News",
                                    Rows =
                                    [
                                        new NewsRowItem
                                        {
                                            Title = "News item",
                                            Link = "https://news.example.invalid/1",
                                            PublishTime = 1_700_000_000_000
                                        }
                                    ]
                                },
                                new NewsTypeItem
                                {
                                    TypeLabel = "Events",
                                    Rows =
                                    [
                                        new NewsRowItem
                                        {
                                            Title = "Event item",
                                            Link = "https://news.example.invalid/2",
                                            PublishTime = 1_700_000_000_001
                                        }
                                    ]
                                }
                            ]
                        }
                    }
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var selectedTab = context.Window.GetVisualDescendants().OfType<TabItem>().Single(tab => tab.IsSelected);
        var header = selectedTab.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "News");
        var indicator = selectedTab.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "PART_SelectedPipe" && border.IsEffectivelyVisible);
        var headerTop = header.TranslatePoint(default, selectedTab);
        var indicatorTop = indicator.TranslatePoint(default, selectedTab);

        Assert.NotNull(headerTop);
        Assert.NotNull(indicatorTop);
        var gap = indicatorTop.Value.Y - (headerTop.Value.Y + header.Bounds.Height);
        // The measured gap includes the header's own text height, whose metrics vary
        // by ±1px between the real Skia font rasterization (golden era) and the classic
        // headless drawing path. The design intends a visible non-zero gap; 3px is the
        // real-font floor, so assert the widened gap stays >= 3px.
        Assert.True(gap >= 3, $"Expected at least 3 px between tab header and indicator, but measured {gap} px.");
    }

    [AvaloniaTheory]
    [InlineData("install", 4, 1)]
    [InlineData("progress", 2, 0)]
    [InlineData("control", 2, 1)]
    public void MainWindow_AtMinimumWindowSize_KeepsOperationActionsInsideWindow(
        string panelMode,
        int expectedActionCount,
        int expectedPrimaryActionCount)
    {
        using var context = CreateContext();
        context.ViewModel.Settings.Editor.Current.StatusDetailMode = StatusDetailModes.Compact;
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.ViewModel.Operations.PanelMode = panelMode switch
        {
            "install" => GameOperationPanelMode.Install,
            "progress" => GameOperationPanelMode.Progress,
            "control" => GameOperationPanelMode.Control,
            _ => throw new ArgumentOutOfRangeException(nameof(panelMode)),
        };
        context.ViewModel.Operations.CanPauseOperation = panelMode == "progress";
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var actions = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control =>
                control.IsEffectivelyVisible
                && (control.Classes.Contains("primary-operation")
                    || control.Classes.Contains("secondary-operation")))
            .ToArray();

        Assert.Equal(expectedActionCount, actions.Length);
        Assert.Equal(
            expectedPrimaryActionCount,
            actions.Count(control => control.Classes.Contains("primary-operation")));
        Assert.All(actions, control =>
        {
            Assert.True(control.IsEffectivelyVisible);
            AssertControlInsideWindow(control, context.Window);
        });
    }

    [AvaloniaTheory]
    [InlineData(1300, 754)]
    [InlineData(1024, 640)]
    public void MainWindow_InstallPanel_AtDefaultAndMinimumWindowSizes_KeepsPathAndActionsReachable(
        double width,
        double height)
    {
        using var context = CreateContext();
        context.Window.Width = width;
        context.Window.Height = height;
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var pathField = context.Window
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("path-field"));
        var actions = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.IsEffectivelyVisible
                && (control.Classes.Contains("primary-operation")
                    || control.Classes.Contains("secondary-operation")))
            .ToArray();

        Assert.Equal(4, actions.Length);
        Assert.True(pathField.Bounds.Width > 0);
        AssertControlInsideWindow(pathField, context.Window);
        Assert.All(actions, action => AssertControlInsideWindow(action, context.Window));
    }

    [AvaloniaTheory]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public void MainWindow_InstallButton_CombinesDiskSpaceCommandAndBusyBinding(
        bool isBlockedByDiskSpace,
        bool isBusy,
        bool expectedEnabled)
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Shell.IsInstallBlockedByDiskSpace = isBlockedByDiskSpace;
        context.ViewModel.Shell.InstallDiskSpaceMessage = isBlockedByDiskSpace
            ? "磁盘空间不足：需要 10GB，可用 6GB。"
            : "";
        context.ViewModel.Shell.IsBusy = isBusy;
        context.ViewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.NotInstalled
        });
        Dispatcher.UIThread.RunJobs();

        // The compact install button carries the disk-space tooltip.
        var installButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(button => ReferenceEquals(
                button.Command,
                context.ViewModel.Operations.InstallOrUpdateCommand))
            .Single(button => button.IsEffectivelyVisible);

        Assert.Equal(expectedEnabled, installButton.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void SettingsControls_UseMinimumAccessibleInteractionHeight()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var settingControls = context.Window
            .GetVisualDescendants()
            .OfType<ComboBox>()
            .Where(control => control.Classes.Contains("setting-control"))
            .ToArray();

        Assert.NotEmpty(settingControls);
        Assert.All(settingControls, control => Assert.True(control.Bounds.Height >= 36));
    }

    [AvaloniaFact]
    public void SettingsNavigation_WhenFocused_UsesDownAndUpToChangeSelection()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var navigation = GetSettingsNavigation(context.Window);
        context.Window.Activate();
        navigation.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.True(navigation.IsKeyboardFocusWithin);

        context.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
        context.Window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(SettingsCategoryCodes.Game, context.ViewModel.Settings.SelectedCategory);
        AssertVisibleSettingsSection(context.Window, typeof(SettingsGameSection));

        context.Window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
        context.Window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(SettingsCategoryCodes.General, context.ViewModel.Settings.SelectedCategory);
        AssertVisibleSettingsSection(context.Window, typeof(SettingsGeneralSection));
    }

    [AvaloniaFact]
    public void SettingsContent_ShowsLocalizedTitleAndSubtitleForCurrentCategory()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var title = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("category-title"));
        var subtitle = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("category-subtitle"));

        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.Single(
                option => option.Code == SettingsCategoryCodes.General).DisplayName,
            title.Text);
        Assert.Equal(title.Text, AutomationProperties.GetName(title));
        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.Single(
                option => option.Code == SettingsCategoryCodes.General).Description,
            subtitle.Text);

        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.Single(
                option => option.Code == SettingsCategoryCodes.Appearance).DisplayName,
            title.Text);
        Assert.Equal(title.Text, AutomationProperties.GetName(title));
        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.Single(
                option => option.Code == SettingsCategoryCodes.Appearance).Description,
            subtitle.Text);
    }

    [AvaloniaFact]
    public void SettingsNavigation_WhenTabIsPressed_FocusesCurrentContent()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var navigation = GetSettingsNavigation(context.Window);
        var generalSection = context.Window.GetVisualDescendants()
            .OfType<SettingsGeneralSection>().Single();
        context.Window.Activate();
        navigation.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.True(navigation.IsKeyboardFocusWithin);

        context.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "");
        context.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "");
        Dispatcher.UIThread.RunJobs();

        var contentHeaderClose = context.Window.GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("content-header-action"));
        Assert.True(contentHeaderClose.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void SettingsContent_WhenScrolledToEnd_KeepsCloseButtonInsideDialog()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var closeButton = context.Window.GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("content-header-action"));
        var contentScroll = context.Window.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("settings-content-scroll"));
        var maxScrollOffset = 0d;
        foreach (var option in context.ViewModel.Settings.Options.SettingsCategories)
        {
            context.ViewModel.Settings.SelectedCategory = option.Code;
            Dispatcher.UIThread.RunJobs();
            maxScrollOffset = Math.Max(
                maxScrollOffset,
                Math.Max(0, contentScroll.Extent.Height - contentScroll.Viewport.Height));
        }

        Assert.True(maxScrollOffset > 0);
        contentScroll.Offset = new Vector(contentScroll.Offset.X, maxScrollOffset);
        Dispatcher.UIThread.RunJobs();

        var closeTransform = closeButton.TransformToVisual(context.Window);
        Assert.NotNull(closeTransform);
        var closeTopLeft = closeTransform.Value.Transform(closeButton.Bounds.TopLeft);
        var closeBottomRight = closeTransform.Value.Transform(closeButton.Bounds.BottomRight);
        Assert.True(closeButton.IsEffectivelyVisible);
        Assert.True(closeTopLeft.Y >= 0);
        Assert.True(closeBottomRight.Y <= context.Window.Bounds.Height);
    }

    [AvaloniaFact]
    public void MainWindow_SettingsButtonHasBoundCommandAndAutomationName()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
        var button = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("settings"));

        Assert.Same(context.ViewModel.WindowChrome.ShowSettingsCommand, button.Command);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));

        button.Command!.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
    }

    [AvaloniaFact]
    public void SettingsFooter_BindsTransactionalSaveAndCancelCommands()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.WindowChrome.IsSettingsVisible = true;
        context.ViewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;
        Dispatcher.UIThread.RunJobs();
        var footerButtons = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control =>
                control.Classes.Contains("dialog-action")
                && (ReferenceEquals(
                        control.Command,
                        context.ViewModel.Settings.SaveSettingsCommand)
                    || ReferenceEquals(
                        control.Command,
                        context.ViewModel.WindowChrome.ShowSettingsCommand)))
            .ToArray();
        Assert.Equal(2, footerButtons.Length);
        var save = footerButtons.Single(button =>
            ReferenceEquals(button.Command, context.ViewModel.Settings.SaveSettingsCommand));
        var cancel = footerButtons.Single(button =>
            ReferenceEquals(button.Command, context.ViewModel.WindowChrome.ShowSettingsCommand));

        Assert.True(save.IsEnabled);
        cancel.Command!.Execute(cancel.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
        Assert.True(context.ViewModel.Settings.IsUnsavedChangesVisible);
    }

    [AvaloniaFact]
    public void ModalIsolation_WhenConfirmationIsVisible_DisablesBackgroundAndRestoresItAfterClose()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowResourcePanelSourceConfirm("switch source");
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
                    context.ViewModel.Dialogs.CancelResourcePanelSourceSwitchCommand));

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
        context.ViewModel.Dialogs.ShowRepairConfirm("repair confirmation");
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
        context.ViewModel.Dialogs.ShowRepairConfirm("repair confirmation");
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
        context.ViewModel.Dialogs.ShowRepairConfirm(
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
    public void MainWindow_WhenEscapeRouteIsInvoked_ClosesSettingsOverlay()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.IsDownloadRunningCloseConfirmVisible = false;
        context.ViewModel.Dialogs.IsStopConfirmVisible = false;
        context.ViewModel.Settings.IsUnsavedChangesVisible = false;
        context.ViewModel.Dialogs.IsRepairConfirmVisible = false;
        context.ViewModel.Dialogs.IsResourcePanelSourceConfirmVisible = false;
        context.ViewModel.Dialogs.IsUninstallConfirmVisible = false;
        context.ViewModel.Dialogs.IsNoticeDialogVisible = false;
        context.ViewModel.ResourcePanel.IsResourcePanelVisible = false;
        context.ViewModel.Settings.Editor.ApplySnapshot(
            context.ViewModel.Settings.Editor.GetSnapshot());
        context.ViewModel.WindowChrome.IsSettingsVisible = true;
        Dispatcher.UIThread.RunJobs();

        var handled = context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        Assert.True(handled);
        Assert.False(context.ViewModel.WindowChrome.IsSettingsVisible);
    }

    [AvaloniaFact]
    public void SettingsOverlay_WhenOpenedWithoutChanges_RemainsClean()
    {
        using var context = CreateContext();
        context.Window.Show();
        var changedProperties = new List<string>();
        context.ViewModel.Settings.Editor.CurrentPropertyChanged += (_, args) =>
            changedProperties.Add(args.PropertyName ?? "<null>");

        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
        Assert.False(
            context.ViewModel.Settings.IsSettingsDirty,
            string.Join(", ", changedProperties));
    }

    [AvaloniaFact]
    public void MainWindow_CloseCommand_WhenConfiguredToMinimize_UsesWindowFallback()
    {
        using var context = CreateContext();
        context.ViewModel.Settings.Editor.ApplySnapshot(new LauncherSettings
        {
            CloseBehavior = CloseBehaviors.Minimize
        });
        context.Window.Show();

        context.ViewModel.WindowChrome.CloseCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(WindowState.Minimized, context.Window.WindowState);
    }

    [AvaloniaFact]
    public void MainWindow_WindowStateSetting_RestoresAndCapturesNormalWindowBounds()
    {
        using var context = CreateContext();
        context.Window.Show();

        context.Window.ApplySavedWindowState(new LauncherSettings
        {
            RememberWindowPositionAndSize = true,
            WindowPositionX = 120,
            WindowPositionY = 240,
            WindowWidth = 1200,
            WindowHeight = 700
        });

        Assert.Equal(new PixelPoint(120, 240), context.Window.Position);
        Assert.Equal(1200, context.Window.Width);
        Assert.Equal(700, context.Window.Height);

        var captured = new LauncherSettings { RememberWindowPositionAndSize = true };
        context.Window.CaptureWindowState(captured);

        Assert.Equal(120, captured.WindowPositionX);
        Assert.Equal(240, captured.WindowPositionY);
        Assert.Equal(1200, captured.WindowWidth);
        Assert.Equal(700, captured.WindowHeight);
    }

    [AvaloniaFact]
    public void MainWindow_WindowStateSetting_WhenDisabledLeavesBoundsUnchanged()
    {
        using var context = CreateContext();
        context.Window.Show();
        var originalPosition = context.Window.Position;
        var originalWidth = context.Window.Width;
        var originalHeight = context.Window.Height;

        context.Window.ApplySavedWindowState(new LauncherSettings
        {
            RememberWindowPositionAndSize = false,
            WindowPositionX = 120,
            WindowPositionY = 240,
            WindowWidth = 1200,
            WindowHeight = 700
        });

        var captured = new LauncherSettings { RememberWindowPositionAndSize = false };
        context.Window.CaptureWindowState(captured);

        Assert.Equal(originalPosition, context.Window.Position);
        Assert.Equal(originalWidth, context.Window.Width);
        Assert.Equal(originalHeight, context.Window.Height);
        Assert.Null(captured.WindowPositionX);
        Assert.Null(captured.WindowPositionY);
        Assert.Null(captured.WindowWidth);
        Assert.Null(captured.WindowHeight);
    }

    [AvaloniaFact]
    public void MainWindow_CloseCommand_WhenTrayIsConfigured_HidesWindow()
    {
        using var context = CreateContext();
        context.ViewModel.Settings.Editor.ApplySnapshot(new LauncherSettings
        {
            CloseBehavior = CloseBehaviors.Minimize
        });
        using var trayService = new SystemTrayService(
            context.Window,
            new LocalizationService(),
            new TestTrayPlatform());
        context.Window.SetSystemTray(trayService);
        context.Window.Show();

        context.ViewModel.WindowChrome.CloseCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.Window.IsVisible);
        Assert.NotEqual(WindowState.Minimized, context.Window.WindowState);
    }

    [AvaloniaFact]
    public void MainWindow_WhenEscapeKeyIsPressed_ClosesSettingsOverlay()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var isDirty = context.ViewModel.Settings.IsSettingsDirty;
        var isUnsaved = context.ViewModel.Settings.IsUnsavedChangesVisible;

        context.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        context.Window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.WindowChrome.IsSettingsVisible,
            $"IsDirty={isDirty} IsUnsaved={isUnsaved} IsVisible={context.ViewModel.WindowChrome.IsSettingsVisible}");
    }

    [AvaloniaFact]
    public void ConfigureViewModel_WhenCalledAgain_UnsubscribesPreviousViewModel()
    {
        using var first = CreateContext();
        using var second = CreateContext();
        first.Window.ConfigureViewModel(second.ViewModel);
        first.Window.WindowState = WindowState.Normal;

        first.ViewModel.WindowChrome.MinimizeCommand.Execute(null);
        Assert.Equal(WindowState.Normal, first.Window.WindowState);

        second.ViewModel.WindowChrome.MinimizeCommand.Execute(null);
        Assert.Equal(WindowState.Minimized, first.Window.WindowState);
    }

    [AvaloniaFact]
    public async Task SetupWizard_WhenGamePathStatusChanges_UpdatesStatusLineAndNextAvailability()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        // Simulate first-launch trigger (settings.json missing)
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsFirstStep);

        var installationBasePath = Path.Combine(context.TempDir, "available-installation");
        context.ViewModel.Dialogs.SetupWizard.GamePath = installationBasePath;

        // Step 0 → 1 detects only the preconfigured test path.
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, context.ViewModel.Dialogs.SetupWizard.Step);
        Assert.Equal(installationBasePath, context.ViewModel.Dialogs.SetupWizard.GamePath);
        Assert.True(GetWizardGamePathStatus(context.Window).IsEffectivelyVisible);

        await WaitForGamePathStatusAsync(
            context.ViewModel.Dialogs.SetupWizard,
            SetupWizardGamePathStatus.AvailableForInstallation);
        Dispatcher.UIThread.RunJobs();

        var statusLine = GetWizardGamePathStatus(context.Window);
        var nextButton = GetWizardNextButton(context.Window, context.ViewModel);
        Assert.Equal(
            context.ViewModel.Shell.I18n["setupWizardGamePathAvailable"],
            statusLine.Text);
        Assert.True(context.ViewModel.Dialogs.SetupWizard.CanGoNext);
        Assert.True(nextButton.IsEnabled);

        var corruptedInstallationPath = new GameInstallationPath().NormalizeGamePath(
            Path.Combine(context.TempDir, "corrupted-installation"));
        Directory.CreateDirectory(corruptedInstallationPath);
        await File.WriteAllTextAsync(
            Path.Combine(corruptedInstallationPath, GamePaths.ManifestFileName),
            "{}");
        context.ViewModel.Dialogs.SetupWizard.GamePath = corruptedInstallationPath;
        await WaitForGamePathStatusAsync(
            context.ViewModel.Dialogs.SetupWizard,
            SetupWizardGamePathStatus.CorruptedInstallation);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            context.ViewModel.Shell.I18n["setupWizardGamePathCorrupted"],
            statusLine.Text);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.CanGoNext);
        Assert.False(nextButton.IsEnabled);

        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, context.ViewModel.Dialogs.SetupWizard.Step);
    }

    [AvaloniaFact]
    public async Task SetupWizard_ReviewList_EditButtonsNavigateToTheirSteps()
    {
        using var context = CreateContext();
        var wizard = context.ViewModel.Dialogs.SetupWizard;
        wizard.GamePath = Path.Combine(context.TempDir, "available-installation");

        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        wizard.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(wizard, SetupWizardGamePathStatus.AvailableForInstallation);
        while (!wizard.IsLastStep)
        {
            wizard.NextCommand.Execute(null);
        }
        Dispatcher.UIThread.RunJobs();

        var editButtons = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => AutomationProperties.GetName(control)
                == context.ViewModel.Shell.I18n["setupWizardEditStep"])
            .ToArray();

        Assert.Equal(4, editButtons.Length);

        foreach (var (editButton, expectedStep) in editButtons.Zip([0, 2, 1, 3]))
        {
            new ButtonAutomationPeer(editButton).Invoke();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(expectedStep, wizard.Step);

            while (!wizard.IsLastStep)
            {
                if (wizard.IsStep1)
                {
                    await WaitForGamePathStatusAsync(
                        wizard,
                        SetupWizardGamePathStatus.AvailableForInstallation);
                }

                wizard.NextCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    [AvaloniaFact]
    public void SetupWizard_RadioChoices_KeepGroupsIndependent()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Step = 2;
        Dispatcher.UIThread.RunJobs();

        var cafe = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["downloadSourceCafe"]);
        var official = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["downloadSourceOfficial"]);

        official.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupOfficial);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupCafe);
        Assert.False(cafe.IsChecked);
        Assert.True(official.IsChecked);

        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var auto = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["proxyAuto"]);
        var direct = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["proxyDirect"]);
        var system = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["proxySystem"]);

        direct.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsProxyDirect);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsProxySystem);
        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupOfficial);
        Assert.False(auto.IsChecked);
        Assert.True(direct.IsChecked);
        Assert.False(system.IsChecked);

        system.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsProxySystem);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsProxyAuto);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsProxyDirect);
        Assert.True(system.IsChecked);
    }

    [AvaloniaFact]
    public void ShellRuntime_FirstLaunchMotionPreference_AppliesSystemResolution()
    {
        // 首启分支不执行完整初始化（快照由向导驱动后再加载）：动效偏好必须按默认
        // System 档先行解析并应用，否则 IsMotionReduced 停留在默认 true，首启向导全程瞬切。
        using var context = CreateContext();
        var runtime = context.Provider
            .GetRequiredService<Cafe.Launcher.Avalonia.Features.Shell.IShellRuntime>();
        var windowsAnimationsEnabled = new WindowsAnimationSettingsProvider()
            .GetWindowsAnimationsEnabled();
        var expectedReduced = Cafe.Launcher.Avalonia.Helpers.MotionSettingsResolver.ShouldReduceMotion(
            MotionModes.System,
            windowsAnimationsEnabled);

        runtime.ApplyFirstLaunchMotionPreference();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(expectedReduced, runtime.IsMotionReduced);
        Assert.Equal(!expectedReduced, context.ViewModel.IsMotionEnabled);
    }

    [AvaloniaFact]
    public void SetupWizard_OptionRows_StretchToAlignRadioCircles()
    {
        // ADR-017：wizard-option 行等宽拉伸（HorizontalAlignment=Stretch）——
        // 列宽大于行宽时行若按内容自适应，非左对齐排布会使圆圈左缘错位。
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Step = 2;
        Dispatcher.UIThread.RunJobs();

        var radios = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .Where(control => control.Classes.Contains("wizard-option") && control.IsEffectivelyVisible)
            .ToList();
        Assert.Equal(2, radios.Count);
        Assert.All(
            radios,
            radio => Assert.Equal(radios[0].Bounds.Width, radio.Bounds.Width));
        Assert.All(
            radios,
            radio => Assert.Equal(radios[0].Bounds.X, radio.Bounds.X));

        var circleOffsets = radios.Select(radio =>
        {
            var circle = radio.GetVisualDescendants()
                .First(child => child.Name == "OuterEllipse");
            return radio.TranslatePoint(circle.Bounds.Position, radio)!.Value.X;
        }).ToList();
        Assert.Equal(circleOffsets[0], circleOffsets[1]);
    }

    [AvaloniaFact]
    public void SetupWizard_StepSwitch_LeavesOnlyFinalStepVisible()
    {
        // ADR-017：步骤切换 = 顺序换页（后置代码编排）；降动效下瞬切换面。
        // 快速连续切换后最新状态生效：任何时刻只有一个步骤面板可见且视觉已定格。
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = true;
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        var overlay = context.Window.GetVisualDescendants()
            .First(control => control.Classes.Contains("setup-wizard-overlay"));
        var steps = overlay.GetVisualDescendants()
            .OfType<StackPanel>()
            .Where(control => control.Classes.Contains("wizard-step"))
            .ToList();
        Assert.Equal(5, steps.Count);

        foreach (var stepIndex in new[] { 3, 1, 4 })
        {
            context.ViewModel.Dialogs.SetupWizard.Step = stepIndex;
            Dispatcher.UIThread.RunJobs();

            var visibleStep = Assert.Single(steps, control => control.IsVisible);
            Assert.Equal(stepIndex, steps.IndexOf(visibleStep));
            Assert.Equal(1d, visibleStep.Opacity);
            var transform = Assert.IsType<TranslateTransform>(visibleStep.RenderTransform);
            Assert.Equal(0d, transform.X);
        }
    }

    [AvaloniaFact]
    public async Task SetupWizard_StepSwitchWithMotion_SequentialSwapSettlesOnFinalStep()
    {
        // ADR-017 + FluentMotionLab ChangeWizardAsync：旧内容先淡出、新内容按方向滑入；
        // 快速连点只保留最新状态，最终目标面板必须定格在 Opacity=1、X=0。
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = false;
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        var overlay = context.Window.GetVisualDescendants()
            .First(control => control.Classes.Contains("setup-wizard-overlay"));
        var steps = overlay.GetVisualDescendants()
            .OfType<StackPanel>()
            .Where(control => control.Classes.Contains("wizard-step"))
            .ToList();
        Assert.Equal(5, steps.Count);

        foreach (var stepIndex in new[] { 2, 0, 4 })
        {
            context.ViewModel.Dialogs.SetupWizard.Step = stepIndex;

            var sawFade = false;
            var sawSlide = false;
            var settled = false;
            while (!settled)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { });
                await Task.Delay(10);
                // 精确判定：动画完成后的所有权结算会精确置 Opacity=1、X=0，
                // 容差判定会在最后一帧插值期间误报已定格。同时采样中间帧，
                // 保证淡入与方向滑入确实经历过渡而不是瞬变。
                settled = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (steps[stepIndex].IsVisible && steps[stepIndex].Opacity is > 0.05 and < 0.95)
                    {
                        sawFade = true;
                    }

                    if (steps[stepIndex].RenderTransform is TranslateTransform movingTransform
                        && Math.Abs(movingTransform.X) is > 0.5 and < 13.5)
                    {
                        sawSlide = true;
                    }

                    return steps[stepIndex].IsVisible
                        && steps[stepIndex].Opacity == 1d
                        && steps[stepIndex].RenderTransform is TranslateTransform settledTransform
                        && settledTransform.X == 0d;
                });
            }

            Dispatcher.UIThread.RunJobs();
            Assert.True(sawFade, "未观察到淡入中间帧，入场透明度疑似瞬变。");
            Assert.True(sawSlide, "未观察到方向滑入中间帧，位移疑似瞬变。");
            var visibleStep = Assert.Single(steps, control => control.IsVisible);
            Assert.Equal(stepIndex, steps.IndexOf(visibleStep));
            Assert.Equal(1d, visibleStep.Opacity);
        }
    }

    [AvaloniaFact]
    public void SetupWizard_StepSwitch_ResetsScrollToTop()
    {
        // 五步共用一个 ScrollViewer：换面时滚动必须复位到顶部，不得把旧偏移带入新步骤。
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        var overlay = context.Window.GetVisualDescendants()
            .First(control => control.Classes.Contains("setup-wizard-overlay"));
        var scroll = overlay.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("scroll-pad"));
        // 压缩视口强制内容溢出，使偏移可被置为非零。
        scroll.MaxHeight = 120;
        Dispatcher.UIThread.RunJobs();
        scroll.Offset = new Vector(0, 80);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.Y > 0, "测试前置：内容需在压缩视口内溢出以产生非零滚动偏移。");

        context.ViewModel.Dialogs.SetupWizard.Step = 4;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0d, scroll.Offset.Y);
    }

    [AvaloniaTheory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public async Task SetupWizard_WhenLanguageChanges_LocalizesStatusLineAndStepTitle(
        string language)
    {
        using var context = CreateContext();
        var installationBasePath = Path.Combine(context.TempDir, "available-installation");
        context.ViewModel.Dialogs.SetupWizard.GamePath = installationBasePath;
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Language = language;
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(
            context.ViewModel.Dialogs.SetupWizard,
            SetupWizardGamePathStatus.AvailableForInstallation);
        Dispatcher.UIThread.RunJobs();

        var statusLine = GetWizardGamePathStatus(context.Window);
        var stepHeadline = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("wizard-step-title") && control.IsEffectivelyVisible);
        var progress = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Text == context.ViewModel.Dialogs.SetupWizard.StepProgress
                && control.IsEffectivelyVisible);

        Assert.Equal(
            context.ViewModel.Shell.I18n["setupWizardGamePathAvailable"],
            statusLine.Text);
        Assert.Equal(statusLine.Text, AutomationProperties.GetName(statusLine));
        // 居中单列解剖：步骤标题随语言本地化，进度行始终可见。
        Assert.Equal(
            context.ViewModel.Shell.I18n["setupWizardGamePath"],
            stepHeadline.Text);
        Assert.Equal("2 / 5", progress.Text);
    }

    [AvaloniaFact]
    public void SetupWizard_WhenEscapeIsPressed_RequiresExitConfirmation()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        var firstHandled = context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        Assert.True(firstHandled);
        Assert.True(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.True(context.ViewModel.Dialogs.IsSetupWizardExitConfirmVisible);

        var secondHandled = context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        Assert.True(secondHandled);
        Assert.True(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.False(context.ViewModel.Dialogs.IsSetupWizardExitConfirmVisible);
    }

    [AvaloniaFact]
    public async Task SetupWizard_WhenExitIsConfirmed_AppliesSkipAndClosesWizard()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        await context.ViewModel.Dialogs.ConfirmSetupWizardExitCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.Dialogs.IsSetupWizardExitConfirmVisible);
        Assert.False(context.ViewModel.Dialogs.IsSetupWizardVisible);
    }

    [AvaloniaFact]
    public async Task SetupWizard_WhenSkipped_HidesOverlay()
    {
        using var context = CreateContext();
        LauncherSettings? applied = null;
        context.ViewModel.Dialogs.SetupWizard.SettingsApplied += settings =>
        {
            applied = settings;
            // Simulate the parent ViewModel's behavior: hide wizard on completion
            context.ViewModel.Dialogs.IsSetupWizardVisible = false;
            return Task.CompletedTask;
        };
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        await context.ViewModel.Dialogs.SetupWizard.SkipCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.NotNull(applied);
        Assert.Equal("auto", applied!.Language);
    }

    [AvaloniaFact]
    public async Task SetupWizard_WhenCompleted_BuildsSettingsAndHidesOverlay()
    {
        using var context = CreateContext();
        LauncherSettings? applied = null;
        context.ViewModel.Dialogs.SetupWizard.SettingsApplied += settings =>
        {
            applied = settings;
            // Simulate the parent ViewModel's behavior: hide wizard on completion
            context.ViewModel.Dialogs.IsSetupWizardVisible = false;
            return Task.CompletedTask;
        };
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        // Navigate to step 1 (GamePath) and set a path
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.GamePath = @"C:\Games\YostarGames\BlueArchive_JP";
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsLastStep);

        await context.ViewModel.Dialogs.SetupWizard.CompleteCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.NotNull(applied);
        Assert.Contains(@"BlueArchive_JP", applied!.GamePath);
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
                || ReferenceEquals(button.Command, context.ViewModel.LogViewer.ExportCommand))
            .ToArray();
    }

    private static Button[] ShowLongConfirmation(TestContext context)
    {
        context.ViewModel.Dialogs.ShowRepairConfirm(string.Concat(Enumerable.Repeat(
            "下载源已切换，修复前需要重新确认本地文件状态。",
            30)));
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                (ReferenceEquals(button.Command, context.ViewModel.Dialogs.CancelRepairCommand)
                    || ReferenceEquals(button.Command, context.ViewModel.Dialogs.ConfirmRepairCommand))
                && button.IsEffectivelyVisible)
            .ToArray();
    }

    private static Button[] ShowSetupWizard(TestContext context)
    {
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                ReferenceEquals(button.Command, context.ViewModel.Dialogs.RequestSetupWizardExitCommand)
                || ReferenceEquals(button.Command, context.ViewModel.Dialogs.SetupWizard.NextCommand))
            .ToArray();
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

    [AvaloniaFact]
    public void Golden_ShellDefault_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        GoldenScreenshot.Compare(context.Window, "shell-default");
    }

    [AvaloniaFact]
    public void Golden_ProgressPanel_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Progress;
        context.Window.Show();
        GoldenScreenshot.Compare(context.Window, "progress-panel");
    }

    [AvaloniaFact]
    public void Golden_SettingsOverlay_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        OpenSettings(context);
        GoldenScreenshot.Compare(context.Window, "settings-overlay");
    }

    [AvaloniaFact]
    public void Golden_ConfirmDialog_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        context.ViewModel.Dialogs.ShowRepairConfirm("golden repair confirmation");
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "confirm-dialog");
    }

    [AvaloniaFact]
    public void Golden_Toast_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        context.ViewModel.Debug.TestToastCommand.Execute("Info");
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "toast");
    }

    private static void PrepareGoldenWindow(TestContext context)
    {
        context.ViewModel.IsMotionReduced = true;
        context.Window.FontFamily = new FontFamily("Segoe UI");
    }

    private static TestContext CreateContext(IGameOperationJourneyFactory? journeyFactory = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var services = new ServiceCollection();
        services.AddLauncherServices();
        if (journeyFactory is not null)
        {
            services.AddSingleton(journeyFactory);
        }

        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        var provider = services.BuildServiceProvider();
        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        viewModel.Shell.ApplyLanguage(
            LauncherLanguages.English,
            viewModel.Settings,
            viewModel.ResourcePanel,
            hasSnapshot: false);
        viewModel.Settings.Editor.ApplySnapshot(
            viewModel.Settings.Editor.GetSnapshot());
        // Apply the default M3 dynamic scheme so navigation selection visual
        // matches the real app's initialization behavior.
        SettingsAppearanceViewModel.ApplyScheme(
            Color.Parse("#FF2E7DF6"));
        var window = new MainWindow { DataContext = viewModel };
        window.ConfigureViewModel(viewModel);
        return new TestContext(tempDir, provider, window, viewModel);
    }

    private static void OpenSettings(TestContext context)
    {
        context.Window.Show();
        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock GetWizardGamePathStatus(MainWindow window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Single(control =>
            control.Classes.Contains("wizard-game-path-status"));

    private static Button GetWizardNextButton(MainWindow window, MainWindowViewModel viewModel) =>
        window.GetVisualDescendants().OfType<Button>().Single(control =>
            ReferenceEquals(control.Command, viewModel.Dialogs.SetupWizard.NextCommand));

    private static async Task WaitForGamePathStatusAsync(
        SetupWizardViewModel viewModel,
        SetupWizardGamePathStatus expectedStatus)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == nameof(SetupWizardViewModel.GamePathStatus)
                && viewModel.GamePathStatus == expectedStatus)
            {
                completion.TrySetResult();
            }
        };
        viewModel.PropertyChanged += handler;
        try
        {
            if (viewModel.GamePathStatus == expectedStatus)
            {
                return;
            }

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            viewModel.PropertyChanged -= handler;
        }
    }

    private static ListBox GetSettingsNavigation(MainWindow window) =>
        window.GetVisualDescendants().OfType<ListBox>().Single(control =>
            AutomationProperties.GetAutomationId(control) == "SettingsNavigation");

    private static IReadOnlyList<ListBoxItem> GetNavigationItems(ListBox navigation) =>
        navigation.GetVisualDescendants().OfType<ListBoxItem>().ToArray();

    private static void AssertNavigationSelectionVisual(ListBox navigation, string expectedCode)
    {
        var selectedItem = GetNavigationItems(navigation).Single(item => item.IsSelected);
        var presenter = selectedItem.GetVisualDescendants().OfType<ContentPresenter>().Single();

        Assert.Equal(expectedCode, ((SettingOption)selectedItem.DataContext!).Code);
        Assert.Equal(new Thickness(0), selectedItem.BorderThickness);
        var secondaryContainer = Assert.IsType<SolidColorBrush>(
            Application.Current!.Resources["Launcher.Color.SecondaryContainer"]).Color;
        var onSecondaryContainer = Assert.IsType<SolidColorBrush>(
            Application.Current!.Resources["Launcher.Color.OnSecondaryContainer"]).Color;
        Assert.Equal(secondaryContainer, Assert.IsType<SolidColorBrush>(selectedItem.Background).Color);
        Assert.Equal(secondaryContainer, Assert.IsType<SolidColorBrush>(presenter.Background).Color);
        Assert.Equal(onSecondaryContainer, Assert.IsType<SolidColorBrush>(selectedItem.Foreground).Color);
    }

    private static void AssertVisibleSettingsSection(MainWindow window, Type expectedType)
    {
        var sections = window.GetVisualDescendants()
            .Where(control => SettingsSections.Any(section => section.SectionType == control.GetType()))
            .ToArray();
        Assert.Equal(SettingsSections.Length, sections.Length);
        Assert.Single(sections, control => control.GetType() == expectedType && control.IsEffectivelyVisible);
        Assert.All(
            sections.Where(control => control.GetType() != expectedType),
            control => Assert.False(control.IsEffectivelyVisible));
    }

    private static void AssertControlInsideWindow(Control control, Window window)
    {
        var topLeft = control.TranslatePoint(default, window);
        Assert.NotNull(topLeft);
        Assert.True(control.Bounds.Width > 0);
        Assert.True(control.Bounds.Height > 0);
        Assert.True(topLeft.Value.X >= 0);
        Assert.True(topLeft.Value.Y >= 0);
        Assert.True(topLeft.Value.X + control.Bounds.Width <= window.ClientSize.Width);
        Assert.True(topLeft.Value.Y + control.Bounds.Height <= window.ClientSize.Height);
    }

    private sealed record TestContext(
        string TempDir,
        ServiceProvider Provider,
        MainWindow Window,
        MainWindowViewModel ViewModel) : IDisposable
    {
        public void Dispose()
        {
            Window.Close();
            Provider.Dispose();
            if (Directory.Exists(TempDir))
            {
                Directory.Delete(TempDir, recursive: true);
            }
        }
    }

    private sealed class TestTrayPlatform : ISystemTrayPlatform
    {
        public bool Initialize(
            SystemTrayMenuText text,
            Action showWindow,
            Action exitApplication) => true;

        public void UpdateText(SystemTrayMenuText text)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FixedGameOperationJourneyFactory(IGameOperationJourney journey)
        : IGameOperationJourneyFactory
    {
        public IGameOperationJourney Create(IGameOperationJourneyHost host) => journey;
    }

    private sealed class ThreadAwareGameOperationJourney : IGameOperationJourney
    {
        private bool isDownloadRunning;
        private Action? isRunningChanged;
        private Action? staleIsRunningChanged;

        public event Func<GameOperationsRefreshMode, Task>? RefreshRequested { add { } remove { } }
        public event Func<Task>? OpenLogViewerRequested { add { } remove { } }
        public event Action? MinimizeRequested { add { } remove { } }
        public event Action? IsRunningChanged
        {
            add
            {
                isRunningChanged += value;
                staleIsRunningChanged = value;
            }
            remove => isRunningChanged -= value;
        }

        public bool IsDownloadRunning => isDownloadRunning;
        public bool IsPaused => false;

        public void SetDownloadRunning(bool value)
        {
            isDownloadRunning = value;
            isRunningChanged?.Invoke();
        }

        public void RaiseStaleRunningChanged() => staleIsRunningChanged?.Invoke();

        public Task StartGameAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task CheckForUpdateAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task InstallOrUpdateAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task CreateDesktopShortcutAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public void OpenGameFolder(LauncherStatusSnapshot snapshot)
        {
        }
        public Task RequestRepairAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task RepairAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task RequestUninstallAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task ConfirmUninstallAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task ResumePersistedAsync(
            LauncherStatusSnapshot snapshot,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void RequestStop()
        {
        }

        public void PerformStop()
        {
        }

        public void Stop(bool clearPersistedState)
        {
        }

        public void Pause()
        {
        }

        public void Resume()
        {
        }
    }
}
