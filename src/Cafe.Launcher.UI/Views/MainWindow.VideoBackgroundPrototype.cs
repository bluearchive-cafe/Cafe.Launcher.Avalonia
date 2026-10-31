#if DEBUG
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Cafe.Launcher.UI.Controls.VideoBackgroundPrototype;

namespace Cafe.Launcher.UI.Views;

internal partial class MainWindow
{
    private VideoBackgroundPrototypeControl? prototypeVideo;
    private DispatcherTimer? prototypeRestoreTimer;
    private bool prototypeBaseline;
    private int prototypeDpiIndex;

    /// <summary>Debug 显式启用入口；正常启动与无头测试不创建视频或原生资源。</summary>
    [ExcludeFromCodeCoverage(Justification = "Debug-only GPU experiment; absent from Release and exercised by real-window validation.")]
    private void InitializeVideoBackgroundPrototype()
    {
        if (Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE") != "1") return;
        Opened += AttachPrototypeVideo;
    }

    [ExcludeFromCodeCoverage(Justification = "Debug-only GPU experiment; absent from Release and exercised by real-window validation.")]
    private void AttachPrototypeVideo(object? sender, EventArgs e)
    {
        Opened -= AttachPrototypeVideo;
        var library = Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_LIBRARY");
        var video = Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_FILE");
        var output = Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_OUTPUT");
        if (!File.Exists(library) || !File.Exists(video) || string.IsNullOrWhiteSpace(output)) return;

        prototypeVideo = new VideoBackgroundPrototypeControl(library, video, output);
        prototypeVideo.DiagnosticCommand += HandlePrototypeDiagnosticCommand;
        // Above the two wallpaper images, below every real shell surface/overlay.
        ShellRoot.Children.Insert(3, prototypeVideo);
        if (Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_CACHE_SHELL") == "1")
        {
            // Cache the unchanged shell independently of the video surface. Invalidations
            // from real interactions still rebuild this cache through Avalonia.
            foreach (var child in ShellRoot.Children)
            {
                if (child is Grid && !ReferenceEquals(child, prototypeVideo)) child.CacheMode = new BitmapCache();
            }
        }
        if (Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_MINIMAL") == "1")
        {
            foreach (var child in ShellRoot.Children)
            {
                if (!ReferenceEquals(child, prototypeVideo)) child.IsVisible = false;
            }
        }
        Title += " [mpv prototype]";
        PropertyChanged += PrototypeWindowChanged;
        KeyDown += PrototypeKeyDown;
        Closed += PrototypeClosed;
        UpdatePrototypePause();
    }

    [ExcludeFromCodeCoverage(Justification = "Debug-only GPU performance probe; driven by the opt-in process harness.")]
    private void HandlePrototypeDiagnosticCommand(string command)
    {
        if (prototypeVideo is null) return;
        switch (command)
        {
            case "pause":
                prototypeVideo.Phase = "paused";
                prototypeVideo.SetPaused(true);
                break;
            case "play":
                prototypeVideo.Phase = "playing";
                prototypeVideo.SetPaused(false);
                break;
            case "gc":
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                break;
            case "release":
                prototypeVideo.Phase = "released";
                prototypeVideo.SetPaused(true);
                ShellRoot.Children.Remove(prototypeVideo);
                break;
            case "recreate":
                prototypeVideo.Phase = "recreated";
                if (!ShellRoot.Children.Contains(prototypeVideo)) ShellRoot.Children.Insert(3, prototypeVideo);
                prototypeVideo.SetPaused(false);
                break;
            case "close":
                Close();
                break;
        }
    }

    [ExcludeFromCodeCoverage(Justification = "Debug-only GPU experiment; absent from Release and exercised by real-window validation.")]
    private void PrototypeWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty)
        {
            UpdatePrototypePause();
        }
    }

    [ExcludeFromCodeCoverage(Justification = "Debug-only GPU experiment; absent from Release and exercised by real-window validation.")]
    private void UpdatePrototypePause()
    {
        prototypeVideo?.SetPaused(prototypeBaseline || !IsVisible || WindowState == WindowState.Minimized);
    }

    [ExcludeFromCodeCoverage(Justification = "Debug-only GPU experiment; absent from Release and exercised by real-window validation.")]
    private void PrototypeKeyDown(object? sender, KeyEventArgs e)
    {
        if (prototypeVideo is null || e.KeyModifiers != (KeyModifiers.Control | KeyModifiers.Shift)) return;
        switch (e.Key)
        {
            case Key.B:
                prototypeBaseline = !prototypeBaseline;
                prototypeVideo.Phase = prototypeBaseline ? "static-baseline" : "video";
                prototypeVideo.IsVisible = !prototypeBaseline;
                UpdatePrototypePause();
                break;
            case Key.S:
                prototypeVideo.Phase = "settings";
                configuredViewModel?.WindowChrome.ShowSettingsCommand.Execute(null);
                break;
            case Key.C:
                prototypeVideo.Phase = "dialog";
                configuredViewModel?.Dialogs.SettingsResetConfirm.Show();
                break;
            case Key.D:
                // Synthetic platform notification: exercises layout and physical FBO sizing,
                // without changing the user's Windows display settings. Not a monitor test.
                double[] scales = [1, 1.25, 1.5, 2];
                prototypeDpiIndex = (prototypeDpiIndex + 1) % scales.Length;
                var scalingChanged = typeof(TopLevel).GetMethod("HandleScalingChanged", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Prototype scaling hook no longer matches Avalonia.");
                scalingChanged.Invoke(this, [scales[prototypeDpiIndex]]);
                prototypeVideo.Phase = $"synthetic-dpi-{scales[prototypeDpiIndex]:F2}";
                prototypeVideo.RequestNextFrameRendering();
                break;
            case Key.T:
                prototypeVideo.Phase = "tray-hidden";
                if (systemTray is not null) systemTray.HideWindow();
                else WindowState = WindowState.Minimized;
                prototypeRestoreTimer?.Stop();
                if (prototypeRestoreTimer is not null) prototypeRestoreTimer.Tick -= RestorePrototypeWindow;
                prototypeRestoreTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                prototypeRestoreTimer.Tick += RestorePrototypeWindow;
                prototypeRestoreTimer.Start();
                break;
            case Key.R:
                prototypeVideo.Phase = "resize";
                if (Width > 1100) { Width = 960; Height = 640; }
                else { Width = 1300; Height = 800; }
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    [ExcludeFromCodeCoverage(Justification = "Debug-only GPU experiment; absent from Release and exercised by real-window validation.")]
    private void RestorePrototypeWindow(object? sender, EventArgs e)
    {
        prototypeRestoreTimer?.Stop();
        if (systemTray is not null) systemTray.ShowWindow();
        else { Show(); WindowState = WindowState.Normal; Activate(); }
        if (prototypeVideo is not null) prototypeVideo.Phase = "tray-restored";
    }

    [ExcludeFromCodeCoverage(Justification = "Debug-only GPU experiment; absent from Release and exercised by real-window validation.")]
    private void PrototypeClosed(object? sender, EventArgs e)
    {
        PropertyChanged -= PrototypeWindowChanged;
        KeyDown -= PrototypeKeyDown;
        Closed -= PrototypeClosed;
        prototypeRestoreTimer?.Stop();
        if (prototypeRestoreTimer is not null) prototypeRestoreTimer.Tick -= RestorePrototypeWindow;
        if (prototypeVideo is not null)
        {
            prototypeVideo.DiagnosticCommand -= HandlePrototypeDiagnosticCommand;
            // Detach while the window still owns the control so OpenGlControlBase can
            // make its context current and release mpv before sampling stops.
            ShellRoot.Children.Remove(prototypeVideo);
        }
        prototypeVideo?.StopSampling();
    }
}
#endif
