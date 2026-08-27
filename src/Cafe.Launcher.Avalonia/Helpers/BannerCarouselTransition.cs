using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// ADR-016 banner transition: automatic ticks cross-fade as one visual object while manual
/// navigation slides the incoming banner a short distance toward its arrival edge.
/// </summary>
public sealed class BannerCarouselTransition : IPageTransition
{
    private const double ManualSlideOffset = 18;

    public BannerCarouselTransition(TimeSpan duration) => Duration = duration;

    /// <summary>Gets the transition duration; reduced motion supplies <see cref="TimeSpan.Zero"/>.</summary>
    public TimeSpan Duration { get; }

    // Set by the view model immediately before a manual navigation; consumed once by the next Start.
    internal bool NextSlideIsDirectional { get; set; }
    internal bool NextSlideIsBackward { get; set; }

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var isDirectional = NextSlideIsDirectional;
        var isBackward = NextSlideIsBackward;
        NextSlideIsDirectional = false;
        NextSlideIsBackward = false;

        if ((from is null && to is null)
            || Duration == TimeSpan.Zero
            || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var offsetX = isDirectional
            ? (isBackward ? -ManualSlideOffset : ManualSlideOffset)
            : 0.0;

        var animations = new List<Task>(2);
        if (to is not null)
        {
            animations.Add(CreateEnterAnimation(Duration, offsetX).RunAsync(to, cancellationToken));
        }

        if (from is not null)
        {
            animations.Add(CreateExitAnimation(Duration).RunAsync(from, cancellationToken));
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

    private static Animation CreateEnterAnimation(TimeSpan duration, double offsetX)
    {
        var animation = new Animation
        {
            Duration = duration,
            Easing = new SplineEasing { X1 = 0, Y1 = 0, X2 = 0, Y2 = 1 },
            FillMode = FillMode.Forward,
        };
        var startFrame = new KeyFrame { Cue = new Cue(0) };
        startFrame.Setters.Add(new Setter { Property = Visual.OpacityProperty, Value = 0d });
        if (offsetX != 0.0)
        {
            startFrame.Setters.Add(new Setter
            {
                Property = Visual.RenderTransformProperty,
                Value = global::Avalonia.Media.Transformation.TransformOperations.Parse(
                    FormattableString.Invariant($"translateX({offsetX}px)")),
            });
        }

        var endFrame = new KeyFrame { Cue = new Cue(1) };
        endFrame.Setters.Add(new Setter { Property = Visual.OpacityProperty, Value = 1d });

        animation.Children.Add(startFrame);
        animation.Children.Add(endFrame);
        return animation;
    }

    private static Animation CreateExitAnimation(TimeSpan duration)
    {
        return new Animation
        {
            Duration = duration,
            Easing = new SplineEasing { X1 = 1, Y1 = 0, X2 = 1, Y2 = 1 },
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
