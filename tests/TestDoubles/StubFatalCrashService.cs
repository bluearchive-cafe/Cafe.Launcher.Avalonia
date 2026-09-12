using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// Records fatal-crash requests and replays them to subscribers without touching
/// disk, logging, or process state.
/// </summary>
public sealed class StubFatalCrashService : IFatalCrashService
{
    public event Action<CrashReport>? FatalCrashRequested;

    public List<(CrashOrigin Origin, Exception Exception)> Requests { get; } = [];

    public void HandleFatalCrash(CrashOrigin origin, Exception exception)
    {
        Requests.Add((origin, exception));
        FatalCrashRequested?.Invoke(new CrashReport
        {
            Id = "CR-STUB",
            OccurredAt = DateTimeOffset.Now,
            Source = origin.ToString(),
            AppVersion = "0.0.0",
            BuildSha = "stub",
            OperatingSystem = "stub",
            UiCulture = "en",
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            TechnicalDetails = exception.ToString()
        });
    }
}
