using System.Globalization;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class CrashReportBootstrapTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "Cafe.Launcher.Avalonia.Tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyCulture_WhenCultureNameIsAbsent_KeepsTheActiveCulture(string? cultureName)
    {
        var snapshot = CultureSnapshot.Capture();
        try
        {
            var expected = CultureInfo.CurrentUICulture;

            CrashReportBootstrap.ApplyCulture(cultureName);

            Assert.Equal(expected, CultureInfo.CurrentUICulture);
        }
        finally
        {
            snapshot.Restore();
        }
    }

    [Fact]
    public void ApplyCulture_WhenCultureNameIsInvalid_KeepsTheActiveCulture()
    {
        var snapshot = CultureSnapshot.Capture();
        try
        {
            var expected = CultureInfo.CurrentUICulture;

            CrashReportBootstrap.ApplyCulture("xx-INVALID");

            Assert.Equal(expected, CultureInfo.CurrentUICulture);
        }
        finally
        {
            snapshot.Restore();
        }
    }

    [Fact]
    public void ApplyCulture_WhenCultureNameIsValid_AppliesItToTheCurrentAndDefaultThread()
    {
        var snapshot = CultureSnapshot.Capture();
        try
        {
            CrashReportBootstrap.ApplyCulture("ja-JP");

            var expected = CultureInfo.GetCultureInfo("ja-JP");
            Assert.Equal(expected, CultureInfo.CurrentUICulture);
            Assert.Equal(expected, CultureInfo.DefaultThreadCurrentUICulture);
        }
        finally
        {
            snapshot.Restore();
        }
    }

    [Fact]
    public void Resolve_WhenSnapshotIsMissing_ReturnsTheUnreadableReport()
    {
        var report = CrashReportBootstrap.Resolve(Path.Combine(tempDirectory, "missing.json"));

        Assert.Equal("CR-UNAVAILABLE", report.Id);
    }

    [Fact]
    public void Resolve_WhenSnapshotIsMalformed_ReturnsTheUnreadableReport()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "garbage.json");
        File.WriteAllText(path, "{not json");

        Assert.Equal("CR-UNAVAILABLE", CrashReportBootstrap.Resolve(path).Id);
    }

    [Fact]
    public void Resolve_WhenSnapshotWasPersisted_ReturnsTheStoredReport()
    {
        Directory.CreateDirectory(tempDirectory);
        var store = new CrashReportStore(tempDirectory);
        var stored = store.Create(CrashOrigin.Main, new InvalidOperationException("persisted"));

        var resolved = CrashReportBootstrap.Resolve(stored.SnapshotPath);

        Assert.Equal(stored.Id, resolved.Id);
        Assert.Equal(stored.SnapshotPath, resolved.SnapshotPath);
        Assert.Equal(stored.TechnicalDetails, resolved.TechnicalDetails);
    }

    [Fact]
    public void Resolve_WhenSnapshotDeclaresNullUiCulture_StillYieldsAReportTheReporterCanShow()
    {
        // System.Text.Json does not enforce non-nullable annotations, so a snapshot
        // carrying "UiCulture": null deserializes successfully and hands the culture
        // application a null name. The reporter must survive it: this is the field
        // that once made the isolated reporter exit without ever showing a window.
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "null-culture.json");
        File.WriteAllText(path, """
            {
              "Id": "CR-NULL-CULTURE",
              "OccurredAt": "2026-09-08T21:47:02+08:00",
              "Source": "Main",
              "AppVersion": "1.2.3",
              "BuildSha": "0123456789abcdef0123456789abcdef01234567",
              "OperatingSystem": "Test OS",
              "UiCulture": null,
              "ExceptionType": "System.InvalidOperationException",
              "TechnicalDetails": "null culture",
              "SnapshotPath": ""
            }
            """);

        var report = CrashReportBootstrap.Resolve(path);
        var snapshot = CultureSnapshot.Capture();
        try
        {
            CrashReportBootstrap.ApplyCulture(report.UiCulture);
        }
        finally
        {
            snapshot.Restore();
        }

        Assert.Equal("CR-NULL-CULTURE", report.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    /// <summary>Saves the process-wide cultures so a culture-applying test can restore them.</summary>
    private readonly record struct CultureSnapshot(
        CultureInfo Culture,
        CultureInfo UiCulture,
        CultureInfo? DefaultCulture,
        CultureInfo? DefaultUiCulture)
    {
        public static CultureSnapshot Capture() => new(
            CultureInfo.CurrentCulture,
            CultureInfo.CurrentUICulture,
            CultureInfo.DefaultThreadCurrentCulture,
            CultureInfo.DefaultThreadCurrentUICulture);

        public void Restore()
        {
            CultureInfo.CurrentCulture = Culture;
            CultureInfo.CurrentUICulture = UiCulture;
            CultureInfo.DefaultThreadCurrentCulture = DefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = DefaultUiCulture;
        }
    }
}
