using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// Records fatal-crash requests and replays them to subscribers without touching
/// disk, logging, or process state.
/// </summary>
public sealed class StubFatalCrashService : IFatalCrashService
{
    public event Action<CrashReport>? FatalCrashRequested;

    public List<(string Context, Exception Exception)> Requests { get; } = [];

    public void HandleFatalCrash(string context, Exception exception)
    {
        Requests.Add((context, exception));
        FatalCrashRequested?.Invoke(new CrashReport
        {
            Id = "CR-STUB",
            OccurredAt = DateTimeOffset.Now,
            Source = context,
            AppVersion = "0.0.0",
            OperatingSystem = "stub",
            UiCulture = "en",
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            TechnicalDetails = exception.ToString()
        });
    }
}
