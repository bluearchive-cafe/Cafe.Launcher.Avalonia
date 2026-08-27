using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Styling;
using Avalonia.Media.Transformation;
using Easings = Avalonia.Animation.Easings;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// ADR-016 banner transition: automatic ticks cross-fade as one visual object while manual
/// navigation slides the incoming banner a short distance toward its arrival edge.
/// </summary>
public sealed class BannerCarouselTransition : IPageTransition
{
    /// <summary>Slide mode consumed by the next <see cref="Start"/> call.</summary>
    public enum CarouselSlideMode
    {
        Fade,
        Forward,
        Backward
    }

    private const string ManualSlideOffsetKey = "Launcher.Motion.Offset.CarouselManual";
    private const double ManualSlideOffsetFallback = 18;
    private const string EnterEasingKey = "Launcher.Motion.Easing.Enter";
    private const string ExitEasingKey = "Launcher.Motion.Easing.Exit";

    public BannerCarouselTransition(TimeSpan duration) => Duration = duration;

    /// <summary>Gets the transition duration; reduced motion supplies <see cref="TimeSpan.Zero"/>.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Gets or sets how the next page change animates; manual navigation sets this before switching.</summary>
    internal CarouselSlideMode PendingSlide { get; set; } = CarouselSlideMode.Fade;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var slideMode = PendingSlide;
        PendingSlide = CarouselSlideMode.Fade;

        if ((from is null && to is null)
            || Duration == TimeSpan.Zero
            || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var offsetX = slideMode switch
        {
            CarouselSlideMode.Forward => MotionResourceLookup.GetDouble(
                ManualSlideOffsetKey, ManualSlideOffsetFallback),
            CarouselSlideMode.Backward => -MotionResourceLookup.GetDouble(
                ManualSlideOffsetKey, ManualSlideOffsetFallback),
            _ => 0.0,
        };

        var animations = new List<Task>(2);
        if (to is not null)
        {
            animations.Add(CreateEnterAnimation(offsetX).RunAsync(to, cancellationToken));
        }

        if (from is not null)
        {
            animations.Add(CreateExitAnimation().RunAsync(from, cancellationToken));
        }

        try
        {
            await Task.WhenAll(animations);
        }
        catch (OperationCanceledException)
        {
            // A newer navigation supersedes an in-flight transition; the host shows the newest page.
        }
    }

    private Animation CreateEnterAnimation(double offsetX)
    {
        var animation = new Animation
        {
            Duration = Duration,
            Easing = MotionResourceLookup.GetEasing(EnterEasingKey, () => new Easings.SplineEasing { X1 = 0, Y1 = 0, X2 = 0, Y2 = 1 }),
            FillMode = FillMode.Forward,
        };
        var startFrame = new KeyFrame { Cue = new Cue(0) };
        startFrame.Setters.Add(new Setter { Property = Visual.OpacityProperty, Value = 0d });
        if (offsetX != 0.0)
        {
            startFrame.Setters.Add(new Setter
            {
                Property = Visual.RenderTransformProperty,
                Value = TransformOperations.Parse(
                    FormattableString.Invariant($"translateX({offsetX}px)")),
            });
        }

        var endFrame = new KeyFrame { Cue = new Cue(1) };
        endFrame.Setters.Add(new Setter { Property = Visual.OpacityProperty, Value = 1d });

        animation.Children.Add(startFrame);
        animation.Children.Add(endFrame);
        return animation;
    }

    private Animation CreateExitAnimation()
    {
        return new Animation
        {
            Duration = Duration,
            Easing = MotionResourceLookup.GetEasing(ExitEasingKey, () => new Easings.SplineEasing { X1 = 1, Y1 = 0, X2 = 1, Y2 = 1 }),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter { Property = Visual.OpacityProperty, Value = 1d } },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter { Property = Visual.OpacityProperty, Value = 0d } },
                },
            },
        };
    }
}
