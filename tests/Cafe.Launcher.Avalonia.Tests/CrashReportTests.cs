using System.Text;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class CrashReportTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "Cafe.Launcher.Avalonia.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_WhenExceptionContainsUserProfile_PersistsReadableSanitizedSnapshot()
    {
        var store = new CrashReportStore(tempDirectory);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var secretPath = Path.Combine(userProfile, "private", "file.txt");

        var report = store.Create("TestBoundary", new InvalidOperationException(secretPath));
        var restored = CrashReportStore.TryRead(report.SnapshotPath);
        var persistedText = File.ReadAllText(report.SnapshotPath, Encoding.UTF8);

        Assert.NotNull(restored);
        Assert.Equal(report.Id, restored!.Id);
        Assert.Contains("%USERPROFILE%", restored.TechnicalDetails, StringComparison.Ordinal);
        Assert.DoesNotContain(userProfile, persistedText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(report.SnapshotPath, restored.SnapshotPath);
    }

    [Fact]
    public void Create_WhenPrimaryDirectoryIsBlocked_UsesFallbackDirectory()
    {
        Directory.CreateDirectory(tempDirectory);
        var blockedPath = Path.Combine(tempDirectory, "blocked");
        File.WriteAllText(blockedPath, "not a directory");
        var fallbackPath = Path.Combine(tempDirectory, "fallback");
        var store = new CrashReportStore(blockedPath, fallbackPath);

        var report = store.Create("Fallback", new IOException("primary unavailable"));

        Assert.StartsWith(fallbackPath, report.SnapshotPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(report.SnapshotPath));
    }

    [Fact]
    public void CleanupOldReports_WhenCountExceedsLimit_RetainsNewestTen()
    {
        Directory.CreateDirectory(tempDirectory);
        var now = DateTimeOffset.Now;
        for (var index = 0; index < 12; index++)
        {
            var path = Path.Combine(tempDirectory, $"report-{index:D2}.json");
            File.WriteAllText(path, "{}");
            File.SetLastWriteTimeUtc(path, now.AddDays(-index).UtcDateTime);
        }

        var store = new CrashReportStore(tempDirectory);
        store.CleanupOldReports(now);

        Assert.Equal(
            CrashReportStore.RetainedReportCount,
            Directory.EnumerateFiles(tempDirectory, "*.json").Count());
        Assert.False(File.Exists(Path.Combine(tempDirectory, "report-11.json")));
    }

    [Fact]
    public void HandleUnhandledCrash_WhenFailuresRepeat_LaunchesOnceAndAppendsLaterFailure()
    {
        Directory.CreateDirectory(tempDirectory);
        using var logger = new UnifiedLogger(tempDirectory);
        var reportDirectory = Path.Combine(tempDirectory, "reports");
        var store = new CrashReportStore(reportDirectory);
        var launcher = new RecordingCrashReporterLauncher();
        var service = new FatalCrashService(logger, store, launcher);

        service.HandleUnhandledCrash("First", new InvalidOperationException("first"));
        service.HandleUnhandledCrash("Second", new IOException("second"));

        Assert.Single(launcher.Paths);
        Assert.Single(Directory.EnumerateFiles(reportDirectory, "*.json"));
        var additional = Assert.Single(Directory.EnumerateFiles(reportDirectory, "*.additional.log"));
        Assert.Contains("second", File.ReadAllText(additional), StringComparison.Ordinal);
    }

    [Fact]
    public void HandleFatalCrash_WhenUiSubscriberExists_RaisesOnceWithoutExternalReporter()
    {
        Directory.CreateDirectory(tempDirectory);
        using var logger = new UnifiedLogger(tempDirectory);
        var store = new CrashReportStore(Path.Combine(tempDirectory, "reports"));
        var launcher = new RecordingCrashReporterLauncher();
        var service = new FatalCrashService(logger, store, launcher);
        CrashReport? requestedReport = null;
        service.FatalCrashRequested += report => requestedReport = report;

        service.HandleFatalCrash("ExplicitFatal", new InvalidOperationException("fatal"));
        service.HandleFatalCrash("DuplicateFatal", new InvalidOperationException("duplicate"));

        Assert.NotNull(requestedReport);
        Assert.Empty(launcher.Paths);
    }

    [Theory]
    [InlineData("--crash-report", "report.json", true)]
    [InlineData("--CRASH-REPORT", "report.json", false)]
    [InlineData("--crash-report", "", false)]
    public void TryGetCrashReportPath_WithArguments_UsesExactInternalCommand(
        string argument,
        string path,
        bool expected)
    {
        var result = Program.TryGetCrashReportPath([argument, path], out var resolvedPath);

        Assert.Equal(expected, result);
        Assert.Equal(expected, resolvedPath is not null);
    }

    [Fact]
    public void RunCrashReporter_WhenReporterClosesCleanly_ReturnsFailureExitCode()
    {
        var exitCode = Program.RunCrashReporter(() => 0);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void RunCrashReporter_WhenReporterThrows_ReturnsFailureExitCode()
    {
        var exitCode = Program.RunCrashReporter(
            () => throw new InvalidOperationException("reporter unavailable"));

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void ResolveSessionExitCode_WhenFatalCrashWasPresented_ReturnsFailureCode()
    {
        Program.FatalCrashExitRequested = true;
        try
        {
            Assert.Equal(1, Program.ResolveSessionExitCode());
        }
        finally
        {
            Program.FatalCrashExitRequested = false;
        }
    }

    [Fact]
    public void ResolveSessionExitCode_WhenNoFatalCrash_ReturnsSuccessCode()
    {
        Program.FatalCrashExitRequested = false;

        Assert.Equal(0, Program.ResolveSessionExitCode());
    }

    [Fact]
    public void CrashReportApp_RegistersIconStylesUsedByCrashReportWindow()
    {
        var projectRoot = TestLocalizationHelper.FindProjectRoot();
        var windowXaml = File.ReadAllText(Path.Combine(projectRoot, "Views", "CrashReportWindow.axaml"));
        var appXaml = File.ReadAllText(Path.Combine(projectRoot, "CrashReportApp.axaml"));

        // The isolated reporter builds its own minimal Application, so every control
        // theme the crash window relies on must be registered there and not only in
        // the main App — otherwise the window silently loses that control.
        Assert.Contains("materialIcons:MaterialIcon", windowXaml, StringComparison.Ordinal);
        Assert.Contains("materialIcons:MaterialIconStyles", appXaml, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private sealed class RecordingCrashReporterLauncher : ICrashReporterLauncher
    {
        public List<string> Paths { get; } = [];

        public bool TryLaunch(string snapshotPath)
        {
            Paths.Add(snapshotPath);
            return true;
        }
    }
}
