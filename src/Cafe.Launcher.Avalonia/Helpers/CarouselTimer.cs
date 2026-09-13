using System;
using Avalonia.Threading;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// Periodic tick source for the banner carousel. The production adapter runs on
/// the Avalonia UI-thread dispatcher; tests substitute a manually fired timer to
/// exercise the carousel timing paths without real delays.
/// </summary>
internal interface ICarouselTimer
{
    /// <summary>Gets whether periodic ticks are currently enabled.</summary>
    bool IsRunning { get; }

    /// <summary>Starts periodic ticks at <paramref name="interval" />, replacing any existing schedule.</summary>
    void Start(TimeSpan interval, Action onTick);

    /// <summary>Stops periodic ticks; subsequent stale ticks must not invoke the callback.</summary>
    void Stop();
}

/// <summary>Production <see cref="ICarouselTimer"/> backed by the UI dispatcher timer.</summary>
internal sealed class DispatcherCarouselTimer : ICarouselTimer
{
    private DispatcherTimer? timer;

    /// <inheritdoc />
    public bool IsRunning => timer?.IsEnabled == true;

    /// <inheritdoc />
    public void Start(TimeSpan interval, Action onTick)
    {
        Stop();
        timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => onTick();
        timer.Start();
    }

    /// <inheritdoc />
    public void Stop()
    {
        timer?.Stop();
        timer = null;
    }
}
