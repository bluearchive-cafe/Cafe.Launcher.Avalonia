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
    bool IsRunning { get; }

    void Start(TimeSpan interval, Action onTick);

    void Stop();
}

/// <summary>Production <see cref="ICarouselTimer"/> backed by the UI dispatcher timer.</summary>
internal sealed class DispatcherCarouselTimer : ICarouselTimer
{
    private DispatcherTimer? timer;

    public bool IsRunning => timer?.IsEnabled == true;

    public void Start(TimeSpan interval, Action onTick)
    {
        Stop();
        timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => onTick();
        timer.Start();
    }

    public void Stop()
    {
        timer?.Stop();
        timer = null;
    }
}
