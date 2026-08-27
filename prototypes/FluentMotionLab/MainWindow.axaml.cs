using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;

namespace FluentMotionLab;

/// <summary>
/// PROTOTYPE ONLY. This code intentionally favors visible behavior over production architecture.
/// Question: does Fluent motion feel calmer, more connected, and more responsive than Legacy?
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string[] SettingsTitles = ["General", "Game", "Appearance", "Advanced"];
    private static readonly string[] SettingsDescriptions =
    [
        "Language, startup, and motion preferences.",
        "Installation path, launch options, and repair controls.",
        "Wallpaper, theme color, and surface preferences.",
        "Diagnostics, network overrides, and experimental features."
    ];

    private static readonly (string Title, string Description)[] WizardSteps =
    [
        ("Choose language", "Select the language used by the launcher."),
        ("Locate the game", "Choose an existing installation or a new destination."),
        ("Pick an appearance", "Preview wallpaper and theme color preferences."),
        ("Setup complete", "Your launcher is ready to use.")
    ];

    private static readonly (string Kicker, string Title, string Description, string Brush)[] CarouselItems =
    [
        ("NEWS 01", "A new journey begins", "Explore the latest launcher update.", "#24456A"),
        ("EVENT 02", "Weekend challenge", "Complete missions to unlock new rewards.", "#513965"),
        ("GUIDE 03", "Prepare your squad", "Review the newest gameplay guide.", "#315443")
    ];

    private static readonly OperationState[] OperationStates =
    [
        new("Ready to install", "12.4 GB · Version 2.6", "INSTALL", 0, 148, false, "#78A9FF"),
        new("Downloading game files", "4.8 GB of 12.4 GB · 38 MB/s", "38%", 38, 176, true, "#78A9FF"),
        new("Verifying installation", "Checking file 827 of 1,420", "VERIFY", 72, 176, true, "#78A9FF"),
        new("Installation complete", "Version 2.6 is ready to play", "✓ DONE", 100, 156, true, "#69D39B"),
        new("Download stopped", "Resume when you are ready", "STOPPED", 38, 168, true, "#F2C66D"),
        new("Download failed", "Check the network and try again", "ERROR", 38, 176, true, "#FF8C91")
    ];

    private readonly MotionProfile[] profiles =
    [
        new(
            "A — Legacy",
            "50 / 167 / 200 / 250ms · Exponential · generic fade + offset",
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(167),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(250),
            new ExponentialEaseOut(),
            new ExponentialEaseIn(),
            new ExponentialEaseOut(),
            IsLegacy: true,
            IsReduced: false),
        new(
            "B — Fluent",
            "83 / 167 / 250 / 333ms · Windows entrance / exit / point-to-point",
            TimeSpan.FromMilliseconds(83),
            TimeSpan.FromMilliseconds(167),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(333),
            new SplineEasing { X1 = 0, Y1 = 0, X2 = 0, Y2 = 1 },
            new SplineEasing { X1 = 1, Y1 = 0, X2 = 1, Y2 = 1 },
            new SplineEasing { X1 = 0.55, Y1 = 0.55, X2 = 0, Y2 = 1 },
            IsLegacy: false,
            IsReduced: false),
        new(
            "C — Reduced",
            "No spatial, automatic, or large-area motion · transient fade ≤ 83ms",
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(83),
            TimeSpan.Zero,
            TimeSpan.Zero,
            new LinearEasing(),
            new LinearEasing(),
            new LinearEasing(),
            IsLegacy: false,
            IsReduced: true)
    ];

    private readonly Dictionary<string, Control> scenes = [];
    private CancellationTokenSource? activeMotion;
    private Button? selectedSceneButton;
    private int profileIndex;
    private string selectedScene = "shell";
    private int settingsIndex;
    private int wizardStep;
    private int carouselIndex;
    private int operationIndex;
    private int toastSerial;
    private bool modalOpen;
    private bool alternateWallpaper;

    public MainWindow()
    {
        InitializeComponent();

        scenes.Add("shell", ShellScene);
        scenes.Add("modal", ModalScene);
        scenes.Add("settings", SettingsScene);
        scenes.Add("wizard", WizardScene);
        scenes.Add("carousel", CarouselScene);
        scenes.Add("operation", OperationScene);
        scenes.Add("toast", ToastScene);
        scenes.Add("appearance", AppearanceScene);

        selectedSceneButton = this.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Tag as string, "shell", StringComparison.Ordinal));

        UpdateProfilePresentation();
        UpdateAllContent();
    }

    private MotionProfile Profile => profiles[profileIndex];

    private async void OnReplayClick(object? sender, RoutedEventArgs e) =>
        await RunDefaultSceneActionAsync();

    private async void OnRapidFireClick(object? sender, RoutedEventArgs e)
    {
        StatusTitle.Text = "Rapid fire";
        StatusDetail.Text = "Five inputs arrive before the previous transition can finish.";

        for (var index = 0; index < 5; index++)
        {
            _ = RunDefaultSceneActionAsync();
            await Task.Delay(58);
        }
    }

    private void OnResetClick(object? sender, RoutedEventArgs e) => ResetPrototype();

    private void OnPreviousVariantClick(object? sender, RoutedEventArgs e) => CycleVariant(-1);

    private void OnNextVariantClick(object? sender, RoutedEventArgs e) => CycleVariant(1);

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox)
        {
            return;
        }

        if (e.Key == Key.Left)
        {
            CycleVariant(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            CycleVariant(1);
            e.Handled = true;
        }
    }

    private void OnSceneClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string scene || !scenes.ContainsKey(scene))
        {
            return;
        }

        activeMotion?.Cancel();
        selectedSceneButton?.Classes.Remove("active");
        selectedSceneButton = button;
        selectedSceneButton.Classes.Add("active");
        selectedScene = scene;

        foreach ((string key, Control control) in scenes)
        {
            control.IsVisible = key == scene;
        }

        StatusTitle.Text = "Scene selected";
        StatusDetail.Text = $"{button.Content} is ready. Use Replay or its local controls.";
        UpdateStateDump();
    }

    private async void OnActionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action })
        {
            return;
        }

        await RunActionAsync(action);
    }

    private void CycleVariant(int delta)
    {
        activeMotion?.Cancel();
        profileIndex = (profileIndex + delta + profiles.Length) % profiles.Length;
        UpdateProfilePresentation();
        StatusTitle.Text = "Variant changed";
        StatusDetail.Text = $"Now evaluating {Profile.Label}. Existing scene state was preserved.";
    }

    private async Task RunDefaultSceneActionAsync()
    {
        string action = selectedScene switch
        {
            "shell" => "shell:replay",
            "modal" => "modal:toggle",
            "settings" => $"settings:{(settingsIndex + 1) % SettingsTitles.Length}",
            "wizard" => "wizard:next",
            "carousel" => "carousel:next",
            "operation" => "operation:next",
            "toast" => "toast:add",
            "appearance" => "appearance:toggle",
            _ => "shell:replay"
        };

        await RunActionAsync(action);
    }

    private async Task RunActionAsync(string action)
    {
        string[] parts = action.Split(':', 2);
        try
        {
            await (parts[0] switch
            {
                "shell" => ReplayShellAsync(),
                "modal" => ToggleModalAsync(),
                "settings" => ChangeSettingsAsync(int.Parse(parts[1])),
                "wizard" => ChangeWizardAsync(parts[1]),
                "carousel" => ChangeCarouselAsync(parts[1]),
                "operation" => ChangeOperationAsync(),
                "toast" => parts[1] == "add" ? AddToastAsync() : DismissToastAsync(),
                "appearance" => ToggleAppearanceAsync(),
                _ => Task.CompletedTask
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when a newer interaction supersedes an in-flight prototype transition.
        }
    }

    private async Task ReplayShellAsync()
    {
        CancellationToken token = BeginMotion("Shell entrance", Profile.IsReduced
            ? "Reduced presents the shell immediately."
            : Profile.IsLegacy
                ? "Legacy uses the existing generic content offset."
                : "Fluent fades the complete shell once without spatial movement.");

        TimeSpan duration = Profile.IsReduced
            ? TimeSpan.Zero
            : Profile.IsLegacy ? Profile.Content : Profile.Fast;
        double offset = Profile.IsLegacy ? 6 : 0;
        await AnimateEntranceAsync(ShellPreview, 0, offset, duration, Profile.Enter, token);
        CompleteMotion(token, "Shell is interactive and settled.");
    }

    private async Task ToggleModalAsync()
    {
        CancellationToken token = BeginMotion("Modal surface", modalOpen
            ? "The underlying task remains inert until exit completes."
            : "The top modal owns interaction as soon as it opens.");

        if (!modalOpen)
        {
            modalOpen = true;
            ModalDemoOverlay.IsVisible = true;
            TimeSpan overlayDuration = Profile.IsReduced ? Profile.Fast : Profile.Fast;
            TimeSpan surfaceDuration = Profile.IsReduced ? Profile.Fast : Profile.Normal;
            double surfaceOffset = Profile.IsReduced ? 0 : 8;

            List<Task> animations =
            [
                AnimateEntranceAsync(ModalDemoOverlay, 0, 0, overlayDuration, Profile.Enter, token),
                AnimateEntranceAsync(ModalDemoSurface, 0, surfaceOffset, surfaceDuration, Profile.Enter, token)
            ];

            if (Profile.IsLegacy)
            {
                animations.Add(AnimateEntranceAsync(
                    ModalDemoContent,
                    0,
                    6,
                    Profile.Content,
                    Profile.Enter,
                    token));
            }
            else
            {
                SetFinalVisual(ModalDemoContent);
            }

            await Task.WhenAll(animations);
            CompleteMotion(token, "Interaction owner: modal surface.");
        }
        else
        {
            TimeSpan duration = Profile.IsReduced ? Profile.Fast : Profile.Fast;
            double offset = Profile.IsReduced ? 0 : Profile.IsLegacy ? 8 : 4;
            await Task.WhenAll(
                AnimateExitAsync(ModalDemoSurface, 0, offset, duration, Profile.Exit, token),
                AnimateExitAsync(ModalDemoOverlay, 0, 0, duration, Profile.Exit, token));

            if (!token.IsCancellationRequested)
            {
                ModalDemoOverlay.IsVisible = false;
                modalOpen = false;
                CompleteMotion(token, "Interaction owner: underlying task; focus may return to the opener.");
            }
        }

        UpdateStateDump();
    }

    private async Task ChangeSettingsAsync(int nextIndex)
    {
        nextIndex = Math.Clamp(nextIndex, 0, SettingsTitles.Length - 1);
        CancellationToken token = BeginMotion("Settings content", $"Switching to {SettingsTitles[nextIndex]} without implying spatial order.");
        TimeSpan duration = Profile.IsReduced ? TimeSpan.Zero : Profile.IsLegacy ? Profile.Content : Profile.Fast;
        double offset = Profile.IsLegacy ? 6 : 0;

        await FadeSwapAsync(
            SettingsContent,
            () =>
            {
                settingsIndex = nextIndex;
                SettingsTitle.Text = SettingsTitles[settingsIndex];
                SettingsDescription.Text = SettingsDescriptions[settingsIndex];
            },
            0,
            offset,
            duration,
            Profile.Enter,
            token);

        CompleteMotion(token, $"Settings category: {SettingsTitles[settingsIndex]}.");
        UpdateStateDump();
    }

    private async Task ChangeWizardAsync(string direction)
    {
        int previous = wizardStep;
        int next = direction switch
        {
            "restart" => 0,
            "previous" => Math.Max(0, wizardStep - 1),
            _ => Math.Min(WizardSteps.Length - 1, wizardStep + 1)
        };

        CancellationToken token = BeginMotion("Wizard direction", next == WizardSteps.Length - 1
            ? "The current surface confirms completion without opening another page."
            : next >= previous ? "Forward motion follows the task direction." : "Backward motion reverses direction.");

        double offsetX = Profile.IsReduced
            ? 0
            : Profile.IsLegacy ? 0 : next >= previous ? 14 : -14;
        double offsetY = Profile.IsLegacy ? 6 : 0;
        TimeSpan duration = Profile.IsReduced ? TimeSpan.Zero : Profile.IsLegacy ? Profile.Content : Profile.Normal;

        await FadeSwapAsync(
            WizardContent,
            () =>
            {
                wizardStep = next;
                WizardStepLabel.Text = $"STEP {wizardStep + 1} OF {WizardSteps.Length}";
                WizardTitle.Text = WizardSteps[wizardStep].Title;
                WizardDescription.Text = WizardSteps[wizardStep].Description;
                WizardTitle.Foreground = wizardStep == WizardSteps.Length - 1
                    ? FindBrush("Lab.Success")
                    : FindBrush("Lab.Text");
            },
            offsetX,
            offsetY,
            duration,
            Profile.Enter,
            token);

        CompleteMotion(token, wizardStep == WizardSteps.Length - 1
            ? "Setup completion is visible in place."
            : $"Wizard step: {wizardStep + 1}.");
        UpdateStateDump();
    }

    private async Task ChangeCarouselAsync(string direction)
    {
        if (direction == "auto" && Profile.IsReduced)
        {
            BeginMotion("Automatic carousel", "Reduced motion keeps the current banner and stops automatic playback.");
            CompleteMotion(activeMotion!.Token, "Automatic playback: paused.");
            return;
        }

        int delta = direction == "previous" ? -1 : 1;
        int next = (carouselIndex + delta + CarouselItems.Length) % CarouselItems.Length;
        CancellationToken token = BeginMotion("Banner carousel", direction == "auto"
            ? Profile.IsLegacy ? "Legacy automatically slides the banner." : "Fluent automatically cross-fades the banner."
            : "Manual navigation preserves the requested direction.");

        bool directional = direction != "auto" || Profile.IsLegacy;
        double offsetX = Profile.IsReduced || !directional ? 0 : delta * 18;
        TimeSpan duration = Profile.IsReduced ? TimeSpan.Zero : Profile.Normal;

        await FadeSwapAsync(
            CarouselVisual,
            () =>
            {
                carouselIndex = next;
                ApplyCarouselItem();
            },
            offsetX,
            0,
            duration,
            Profile.Enter,
            token);

        CompleteMotion(token, $"Banner {carouselIndex + 1}; source: {direction}.");
        UpdateStateDump();
    }

    private async Task ChangeOperationAsync()
    {
        int next = (operationIndex + 1) % OperationStates.Length;
        OperationState state = OperationStates[next];
        CancellationToken token = BeginMotion("Game operation surface", state.Title);

        if (Profile.IsReduced)
        {
            operationIndex = next;
            ApplyOperationState();
        }
        else if (Profile.IsLegacy)
        {
            operationIndex = next;
            ApplyOperationState();
            await AnimateEntranceAsync(OperationPanel, 0, 12, Profile.Normal, Profile.Enter, token);
        }
        else
        {
            OperationPanel.Transitions = null;
            OperationPanel.Transitions =
            [
                new DoubleTransition
                {
                    Property = Layoutable.HeightProperty,
                    Duration = Profile.Normal,
                    Easing = Profile.PointToPoint
                },
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = Profile.Fast,
                    Easing = Profile.Enter
                }
            ];

            OperationPanel.Opacity = 0.58;
            operationIndex = next;
            ApplyOperationState();
            await NextFrameAsync(token);
            OperationPanel.Opacity = 1;
            await WaitAsync(Profile.Normal, token);
            if (!token.IsCancellationRequested)
            {
                OperationPanel.Transitions = null;
            }
        }

        CompleteMotion(token, state.Badge is "✓ DONE"
            ? "Important completion is confirmed in the same task surface."
            : state.Badge is "STOPPED" or "ERROR"
                ? "Recoverable context remains; no completion emphasis plays."
                : "The bottom edge stayed anchored while the task state changed.");
        UpdateStateDump();
    }

    private async Task AddToastAsync()
    {
        CancellationToken token = BeginMotion("Toast stack", "A new success message enters from the right; existing messages make room together.");

        try
        {
            SettleToastStack();
            while (ToastHost.Children.Count >= 3)
            {
                ToastHost.Children.RemoveAt(ToastHost.Children.Count - 1);
            }

            toastSerial++;
            Dictionary<Control, double> restingRows = new();
            foreach (Control existing in ToastHost.Children.OfType<Control>())
            {
                restingRows[existing] = existing.Bounds.Y;
            }

            Border toast = CreateToast(toastSerial);
            TranslateTransform toastTransform = EnsureTranslate(toast);
            toastTransform.Transitions = null;
            toastTransform.X = Profile.IsReduced ? 0 : Profile.IsLegacy ? 6 : 12;
            toast.Opacity = 0;
            ToastHost.Children.Insert(0, toast);
            ToastHost.UpdateLayout();

            foreach (KeyValuePair<Control, double> row in restingRows)
            {
                TranslateTransform transform = EnsureTranslate(row.Key);
                transform.Transitions = null;
                transform.Y = row.Key.Bounds.Y - row.Value;
            }

            await NextFrameAsync(token);

            TimeSpan shiftDuration = Profile.IsReduced ? TimeSpan.Zero : Profile.Fast;
            foreach (KeyValuePair<Control, double> row in restingRows)
            {
                TranslateTransform transform = EnsureTranslate(row.Key);
                transform.Transitions =
                [
                    new DoubleTransition
                    {
                        Property = TranslateTransform.YProperty,
                        Duration = shiftDuration,
                        Easing = Profile.PointToPoint
                    }
                ];
                transform.Y = 0;
            }

            TimeSpan enterDuration = Profile.IsReduced ? Profile.Fast : Profile.IsLegacy ? Profile.Content : Profile.Fast;
            toast.Transitions =
            [
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = enterDuration,
                    Easing = Profile.Enter
                }
            ];
            toastTransform.Transitions =
            [
                new DoubleTransition
                {
                    Property = TranslateTransform.XProperty,
                    Duration = enterDuration,
                    Easing = Profile.PointToPoint
                }
            ];
            toast.Opacity = 1;
            toastTransform.X = 0;

            await WaitAsync(enterDuration > shiftDuration ? enterDuration : shiftDuration, token);
        }
        catch (OperationCanceledException)
        {
            // A newer interaction supersedes this transition; snap to the settled stack below.
        }

        SettleToastStack();
        CompleteMotion(token, $"Toast count: {ToastHost.Children.Count}. No stagger queue.");
        UpdateStateDump();
    }

    private async Task DismissToastAsync()
    {
        if (ToastHost.Children.Count == 0)
        {
            StatusTitle.Text = "Toast stack";
            StatusDetail.Text = "There is no Toast to dismiss.";
            return;
        }

        CancellationToken token = BeginMotion("Toast exit", "The oldest message exits toward its right-edge origin.");
        Control oldest = (Control)ToastHost.Children[^1];
        TranslateTransform oldestTransform = EnsureTranslate(oldest);

        try
        {
            SettleToastStack();
            TimeSpan duration = Profile.Fast;
            double offset = Profile.IsReduced ? 0 : 12;

            oldest.Transitions =
            [
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = duration,
                    Easing = Profile.Exit
                }
            ];
            oldestTransform.Transitions =
            [
                new DoubleTransition
                {
                    Property = TranslateTransform.XProperty,
                    Duration = duration,
                    Easing = Profile.Exit
                }
            ];
            oldest.Opacity = 0;
            oldestTransform.X = offset;

            await WaitAsync(duration, token);
        }
        catch (OperationCanceledException)
        {
            // A newer interaction supersedes the exit; the toast stays fully visible.
        }

        if (!token.IsCancellationRequested)
        {
            ToastHost.Children.Remove(oldest);
            CompleteMotion(token, $"Toast count: {ToastHost.Children.Count}.");
        }
        else
        {
            SettleToastStack();
        }

        UpdateStateDump();
    }

    private void SettleToastStack()
    {
        foreach (Control child in ToastHost.Children.OfType<Control>())
        {
            SetFinalVisual(child);
        }
    }

    private async Task ToggleAppearanceAsync()
    {
        CancellationToken token = BeginMotion("Appearance preview", Profile.IsReduced
            ? "Large-area fades are disabled; wallpaper and tokens switch immediately."
            : Profile.IsLegacy
                ? "Legacy replaces the wallpaper immediately."
                : "Fluent cross-fades the wallpaper as one object; tokens switch atomically.");

        alternateWallpaper = !alternateWallpaper;
        AppearanceButton.Background = Brush.Parse(alternateWallpaper ? "#B98DE8" : "#78A9FF");
        AppearanceButton.Foreground = Brush.Parse("#071323");

        if (Profile.IsReduced || Profile.IsLegacy)
        {
            WallpaperB.Opacity = alternateWallpaper ? 1 : 0;
            WallpaperA.Opacity = alternateWallpaper ? 0 : 1;
        }
        else
        {
            await Task.WhenAll(
                AnimateOpacityAsync(WallpaperB, WallpaperB.Opacity, alternateWallpaper ? 1 : 0, Profile.Normal, Profile.Enter, token),
                AnimateOpacityAsync(WallpaperA, WallpaperA.Opacity, alternateWallpaper ? 0 : 1, Profile.Normal, Profile.Enter, token));
        }

        CompleteMotion(token, alternateWallpaper ? "Wallpaper B · purple accent." : "Wallpaper A · blue accent.");
        UpdateStateDump();
    }

    private CancellationToken BeginMotion(string title, string detail)
    {
        activeMotion?.Cancel();
        activeMotion?.Dispose();
        activeMotion = new CancellationTokenSource();
        StatusTitle.Text = title;
        StatusDetail.Text = detail;
        UpdateStateDump();
        return activeMotion.Token;
    }

    private void CompleteMotion(CancellationToken token, string detail)
    {
        if (!token.IsCancellationRequested)
        {
            StatusDetail.Text = detail;
            UpdateStateDump();
        }
    }

    private async Task FadeSwapAsync(
        Control target,
        Action swap,
        double entranceX,
        double entranceY,
        TimeSpan totalDuration,
        Easing easing,
        CancellationToken token)
    {
        if (totalDuration == TimeSpan.Zero)
        {
            swap();
            SetFinalVisual(target);
            return;
        }

        TimeSpan half = TimeSpan.FromTicks(totalDuration.Ticks / 2);
        await AnimateOpacityAsync(target, target.Opacity, 0, half, Profile.Exit, token);
        if (token.IsCancellationRequested)
        {
            return;
        }
        swap();
        await AnimateEntranceAsync(target, entranceX, entranceY, half, easing, token);
    }

    private async Task AnimateEntranceAsync(
        Control target,
        double fromX,
        double fromY,
        TimeSpan duration,
        Easing easing,
        CancellationToken token)
    {
        if (duration == TimeSpan.Zero)
        {
            SetFinalVisual(target);
            return;
        }

        TranslateTransform transform = EnsureTranslate(target);
        target.Transitions = null;
        transform.Transitions = null;
        target.Opacity = 0;
        transform.X = fromX;
        transform.Y = fromY;
        await NextFrameAsync(token);

        target.Transitions =
        [
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = duration,
                Easing = easing
            }
        ];
        transform.Transitions =
        [
            new DoubleTransition
            {
                Property = TranslateTransform.XProperty,
                Duration = duration,
                Easing = easing
            },
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = duration,
                Easing = easing
            }
        ];
        target.Opacity = 1;
        transform.X = 0;
        transform.Y = 0;
        await WaitAsync(duration, token);
        ClearTransitions(target, transform, token);
    }

    private async Task AnimateExitAsync(
        Control target,
        double toX,
        double toY,
        TimeSpan duration,
        Easing easing,
        CancellationToken token)
    {
        if (duration == TimeSpan.Zero)
        {
            target.Opacity = 0;
            return;
        }

        TranslateTransform transform = EnsureTranslate(target);
        target.Transitions =
        [
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = duration,
                Easing = easing
            }
        ];
        transform.Transitions =
        [
            new DoubleTransition
            {
                Property = TranslateTransform.XProperty,
                Duration = duration,
                Easing = easing
            },
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = duration,
                Easing = easing
            }
        ];
        target.Opacity = 0;
        transform.X = toX;
        transform.Y = toY;
        await WaitAsync(duration, token);
        ClearTransitions(target, transform, token);
    }

    private static async Task AnimateOpacityAsync(
        Control target,
        double from,
        double to,
        TimeSpan duration,
        Easing easing,
        CancellationToken token)
    {
        target.Transitions = null;
        target.Opacity = from;
        if (duration == TimeSpan.Zero)
        {
            target.Opacity = to;
            return;
        }

        await NextFrameAsync(token);
        target.Transitions =
        [
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = duration,
                Easing = easing
            }
        ];
        target.Opacity = to;
        await WaitAsync(duration, token);
        if (!token.IsCancellationRequested)
        {
            target.Transitions = null;
        }
    }

    private static TranslateTransform EnsureTranslate(Control target)
    {
        if (target.RenderTransform is TranslateTransform transform)
        {
            return transform;
        }

        transform = new TranslateTransform();
        target.RenderTransform = transform;
        return transform;
    }

    private static void SetFinalVisual(Control target)
    {
        target.Transitions = null;
        target.Opacity = 1;
        TranslateTransform transform = EnsureTranslate(target);
        transform.Transitions = null;
        transform.X = 0;
        transform.Y = 0;
    }

    private static void ClearTransitions(Control target, TranslateTransform transform, CancellationToken token)
    {
        if (!token.IsCancellationRequested)
        {
            target.Transitions = null;
            transform.Transitions = null;
        }
    }

    private static async Task NextFrameAsync(CancellationToken token)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(16, token);
    }

    private static async Task WaitAsync(TimeSpan duration, CancellationToken token)
    {
        try
        {
            await Task.Delay(duration + TimeSpan.FromMilliseconds(20), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private Border CreateToast(int serial)
    {
        Grid content = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12
        };
        content.Children.Add(new TextBlock
        {
            Text = "✓",
            Foreground = FindBrush("Lab.Success"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new StackPanel
        {
            [Grid.ColumnProperty] = 1,
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = $"Operation saved · {serial}", FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "The latest state is now active.", Classes = { "muted" }, FontSize = 12 }
            }
        });

        return new Border
        {
            Width = 310,
            Padding = new Thickness(16, 12),
            CornerRadius = new CornerRadius(10),
            Background = Brush.Parse("#1A2B42"),
            BorderBrush = Brush.Parse("#3B5878"),
            BorderThickness = new Thickness(1),
            Child = content,
            RenderTransform = new TranslateTransform()
        };
    }

    private void ApplyCarouselItem()
    {
        var item = CarouselItems[carouselIndex];
        CarouselKicker.Text = item.Kicker;
        CarouselTitle.Text = item.Title;
        CarouselDescription.Text = item.Description;
        CarouselVisual.Background = Brush.Parse(item.Brush);
    }

    private void ApplyOperationState()
    {
        OperationState state = OperationStates[operationIndex];
        OperationTitle.Text = state.Title;
        OperationDescription.Text = state.Description;
        OperationBadge.Text = state.Badge;
        OperationBadge.Foreground = Brush.Parse(state.Accent);
        OperationProgress.IsVisible = state.ShowProgress;
        OperationProgress.Value = state.Progress;
        OperationPanel.Height = state.Height;
    }

    private void UpdateProfilePresentation()
    {
        VariantLabel.Text = Profile.Label;
        ProfileDump.Text = Profile.Description;
        UpdateStateDump();
    }

    private void UpdateAllContent()
    {
        SettingsTitle.Text = SettingsTitles[settingsIndex];
        SettingsDescription.Text = SettingsDescriptions[settingsIndex];
        WizardStepLabel.Text = $"STEP {wizardStep + 1} OF {WizardSteps.Length}";
        WizardTitle.Text = WizardSteps[wizardStep].Title;
        WizardDescription.Text = WizardSteps[wizardStep].Description;
        ApplyCarouselItem();
        ApplyOperationState();
        UpdateStateDump();
    }

    private void UpdateStateDump()
    {
        StateDump.Text =
            $"scene: {selectedScene}\n" +
            $"variant: {Profile.Label}\n" +
            $"settings: {SettingsTitles[settingsIndex]}\n" +
            $"wizard: {wizardStep + 1}/{WizardSteps.Length}\n" +
            $"carousel: {carouselIndex + 1}/{CarouselItems.Length}\n" +
            $"operation: {OperationStates[operationIndex].Badge}\n" +
            $"modal open: {modalOpen}\n" +
            $"toasts: {ToastHost.Children.Count}\n" +
            $"wallpaper: {(alternateWallpaper ? "B" : "A")}";
    }

    private IBrush FindBrush(string key)
    {
        if (Application.Current?.TryGetResource(key, ActualThemeVariant, out object? value) == true
            && value is IBrush brush)
        {
            return brush;
        }

        return Brushes.White;
    }

    private void ResetPrototype()
    {
        activeMotion?.Cancel();
        settingsIndex = 0;
        wizardStep = 0;
        carouselIndex = 0;
        operationIndex = 0;
        toastSerial = 0;
        modalOpen = false;
        alternateWallpaper = false;
        ModalDemoOverlay.IsVisible = false;
        ToastHost.Children.Clear();
        WallpaperA.Opacity = 1;
        WallpaperB.Opacity = 0;
        AppearanceButton.Background = FindBrush("Lab.Accent");
        WizardTitle.Foreground = FindBrush("Lab.Text");
        SetFinalVisual(ShellPreview);
        SetFinalVisual(SettingsContent);
        SetFinalVisual(WizardContent);
        SetFinalVisual(CarouselVisual);
        SetFinalVisual(OperationPanel);
        UpdateAllContent();
        StatusTitle.Text = "Reset complete";
        StatusDetail.Text = "All scene state returned to its initial value.";
    }

    private sealed record MotionProfile(
        string Label,
        string Description,
        TimeSpan Faster,
        TimeSpan Fast,
        TimeSpan Content,
        TimeSpan Normal,
        Easing Enter,
        Easing Exit,
        Easing PointToPoint,
        bool IsLegacy,
        bool IsReduced);

    private sealed record OperationState(
        string Title,
        string Description,
        string Badge,
        double Progress,
        double Height,
        bool ShowProgress,
        string Accent);
}
