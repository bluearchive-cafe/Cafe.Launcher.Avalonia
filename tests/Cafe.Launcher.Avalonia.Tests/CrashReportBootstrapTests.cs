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
        using var culture = new CultureScope();
        var expected = CultureInfo.CurrentUICulture;

        CrashReportBootstrap.ApplyCulture(cultureName);

        Assert.Equal(expected, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void ApplyCulture_WhenCultureNameIsInvalid_KeepsTheActiveCulture()
    {
        using var culture = new CultureScope();
        var expected = CultureInfo.CurrentUICulture;

        CrashReportBootstrap.ApplyCulture("xx-INVALID");

        Assert.Equal(expected, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void ApplyCulture_WhenCultureNameIsValid_AppliesItToTheCurrentAndDefaultThread()
    {
        using var culture = new CultureScope();

        CrashReportBootstrap.ApplyCulture("ja-JP");

        var expected = CultureInfo.GetCultureInfo("ja-JP");
        Assert.Equal(expected, CultureInfo.CurrentUICulture);
        Assert.Equal(expected, CultureInfo.DefaultThreadCurrentUICulture);
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
    public void Resolve_WhenRequiredMemberIsMissing_ReturnsTheUnreadableReport()
    {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "missing-member.json");
        File.WriteAllText(path, """
            {
              "OccurredAt": "2026-09-08T21:47:02+08:00",
              "Source": "Main",
              "AppVersion": "1.2.3",
              "BuildSha": "0123456789abcdef0123456789abcdef01234567",
              "OperatingSystem": "Test OS",
              "UiCulture": "en",
              "ExceptionType": "System.InvalidOperationException",
              "TechnicalDetails": "no Id member"
            }
            """);

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
        using (var culture = new CultureScope())
        {
            CrashReportBootstrap.ApplyCulture(report.UiCulture);
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

    /// <summary>
    /// Restores the four process-wide culture slots a test may overwrite. Unlike the
    /// production <c>SystemCultureSnapshot</c> (current-thread slots only), this also
    /// restores the default-thread slots that <c>ApplyCulture</c> writes.
    /// </summary>
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        private readonly CultureInfo? defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        public void Dispose()
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
        }
    }
}
