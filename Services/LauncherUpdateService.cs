using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Checks for launcher self-updates via the server proxy endpoint.
/// </summary>
public sealed partial class LauncherUpdateService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Strict;
    private readonly IHttpClientLeaseSource leaseSource;
    private readonly string currentVersion;
    private readonly LocalDiagnostics? diagnostics;

    public LauncherUpdateService(HttpClientFactory httpClientFactory, LocalDiagnostics diagnostics)
    {
        leaseSource = new ProxyAwareHttpClientLeaseSource(
            httpClientFactory,
            new Uri(ApiConfig.LauncherApiBaseUrl),
            TimeSpan.FromSeconds(15));
        currentVersion = BuildInfo.LauncherVersion;
        this.diagnostics = diagnostics;
    }

    internal LauncherUpdateService(
        HttpMessageHandler handler,
        string? currentVersionOverride = null,
        LocalDiagnostics? diagnosticsOverride = null)
    {
        leaseSource = new FixedHttpClientLeaseSource(
            handler,
            new Uri(ApiConfig.LauncherApiBaseUrl),
            TimeSpan.FromSeconds(15));
        currentVersion = currentVersionOverride ?? BuildInfo.LauncherVersion;
        diagnostics = diagnosticsOverride;
    }

    /// <summary>
    /// Checks for launcher self-updates via the server proxy endpoint.
    /// The <paramref name="updateChannel"/> controls whether pre-releases are considered:
    /// <see cref="UpdateChannels.Beta"/> includes pre-releases, <see cref="UpdateChannels.Stable"/> skips them.
    /// </summary>
    public async Task<LauncherUpdateCheckResult> CheckForUpdateAsync(
        string updateChannel,
        string proxyMode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var releases = await FetchReleasesAsync(proxyMode, cancellationToken).ConfigureAwait(false);

            if (releases is null || releases.Count == 0)
            {
                return LauncherUpdateCheckResult.Failed(message: "No release metadata was returned.");
            }

            if (!TryParseSemanticVersion(currentVersion, out _))
            {
                return LauncherUpdateCheckResult.Failed(message: "The current launcher version is invalid.");
            }

            var validReleases = releases
                .Where(release => TryParseSemanticVersion(release.Version, out _))
                .ToList();
            if (validReleases.Count == 0)
            {
                return LauncherUpdateCheckResult.Failed(message: "No valid release versions were returned.");
            }

            // Sort by semantic version descending so the latest by version is first.
            validReleases.Sort((a, b) =>
            {
                if (IsNewerVersion(a.Version, b.Version)) return -1;
                if (IsNewerVersion(b.Version, a.Version)) return 1;
                return 0;
            });

            // Filter: beta channel sees all releases; stable channel skips pre-releases.
            var targetRelease = updateChannel == UpdateChannels.Beta
                ? validReleases[0]
                : validReleases.FirstOrDefault(r => !IsPrereleaseVersion(r.Version));

            if (targetRelease is null)
            {
                return LauncherUpdateCheckResult.Succeeded(
                    currentVersion,
                    [],
                    isUpdateAvailable: false);
            }

            if (!TryValidateReleaseFiles(targetRelease.Files, out var validationError))
            {
                if (diagnostics is not null)
                {
                    await diagnostics.MessageAsync(
                        "Launcher update check failed — invalid release file data",
                        $"version: {targetRelease.Version}{Environment.NewLine}{validationError}",
                        CancellationToken.None).ConfigureAwait(false);
                }

                return LauncherUpdateCheckResult.Failed(message: validationError);
            }

            return LauncherUpdateCheckResult.Succeeded(
                targetRelease.Version,
                Array.AsReadOnly(targetRelease.Files.ToArray()),
                IsNewerVersion(targetRelease.Version, currentVersion));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            if (diagnostics is not null)
                await diagnostics.ErrorAsync(
                    "Launcher update check failed — HTTP request error",
                    ex,
                    CancellationToken.None).ConfigureAwait(false);
            return LauncherUpdateCheckResult.Failed(exception: ex);
        }
        catch (JsonException ex)
        {
            if (diagnostics is not null)
                await diagnostics.ErrorAsync(
                    "Launcher update check failed — JSON deserialization error",
                    ex,
                    CancellationToken.None).ConfigureAwait(false);
            return LauncherUpdateCheckResult.Failed(exception: ex);
        }
        catch (TaskCanceledException ex)
        {
            if (diagnostics is not null)
                await diagnostics.ErrorAsync(
                    "Launcher update check failed — request timeout",
                    ex,
                    CancellationToken.None).ConfigureAwait(false);
            return LauncherUpdateCheckResult.Failed(exception: ex);
        }
    }

    private async Task<List<LauncherReleaseResponse>?> FetchReleasesAsync(
        string proxyMode,
        CancellationToken cancellationToken)
    {
        using var lease = await leaseSource.CreateLeaseAsync(proxyMode, cancellationToken).ConfigureAwait(false);
        using var response = await lease.Client.GetAsync(
            ApiConfig.LauncherReleasesPath,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await RemoteHttpRequestService.DeserializeJsonAsync<List<LauncherReleaseResponse>>(
            response,
            new Uri(ApiConfig.LauncherApiBaseUrl + ApiConfig.LauncherReleasesPath),
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Detects whether a version string represents a pre-release by checking for a hyphen suffix
    /// (e.g. "1.0.0-beta.1"). Consistent with <see cref="LauncherSettings"/> channel auto-detection.
    /// </summary>
    private static bool IsPrereleaseVersion(string version) =>
        version.Contains('-');

    private static bool TryValidateReleaseFiles(
        IReadOnlyList<ReleaseFile>? files,
        out string validationError)
    {
        if (files is null || files.Count == 0)
        {
            validationError = "files must contain at least one entry";
            return false;
        }

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            if (string.IsNullOrWhiteSpace(file.Name))
            {
                validationError = $"files[{index}].name must not be empty";
                return false;
            }

            if (!IsReleaseDownloadUri(file.Url))
            {
                validationError = $"files[{index}].url must be a GitHub release download URL";
                return false;
            }

            if (file.Size <= 0)
            {
                validationError = $"files[{index}].size must be greater than zero";
                return false;
            }
        }

        validationError = "";
        return true;
    }

    private static bool IsReleaseDownloadUri(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var downloadUri)
            && downloadUri.Scheme == Uri.UriSchemeHttps
            && string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && downloadUri.AbsolutePath.StartsWith(
                ApiConfig.GitHubReleaseDownloadPathPrefix,
                StringComparison.Ordinal);
    }

    internal static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        if (!TryParseSemanticVersion(latestVersion, out var latest)
            || !TryParseSemanticVersion(currentVersion, out var current))
        {
            return false;
        }

        var coreComparison = VersionComparer.Compare(latest.CoreVersion, current.CoreVersion);
        if (coreComparison != 0)
        {
            return coreComparison > 0;
        }

        // Same core version: check prerelease status.
        if (latest.IsPrerelease != current.IsPrerelease)
        {
            // Stable is newer than prerelease; prerelease is not newer than stable.
            return !latest.IsPrerelease && current.IsPrerelease;
        }

        // Both stable — equal.
        if (!latest.IsPrerelease)
        {
            return false;
        }

        // Both prerelease — compare prerelease labels per SemVer 2.0.0 §11.
        return ComparePrereleaseLabels(latest.PrereleaseLabel, current.PrereleaseLabel) > 0;
    }

    /// <summary>
    /// Compares two dot-separated prerelease labels following SemVer 2.0.0 precedence rules:
    /// 1. Numeric identifiers compare numerically.
    /// 2. Alphanumeric identifiers compare by ASCII sort order.
    /// 3. Numeric identifiers have lower precedence than alphanumeric.
    /// 4. More identifiers (fields) have higher precedence when all preceding fields are equal.
    /// </summary>
    private static int ComparePrereleaseLabels(string latestLabel, string currentLabel)
    {
        var latestParts = latestLabel.Split('.');
        var currentParts = currentLabel.Split('.');
        var maxParts = Math.Max(latestParts.Length, currentParts.Length);

        for (var i = 0; i < maxParts; i++)
        {
            if (i >= latestParts.Length) return -1;
            if (i >= currentParts.Length) return 1;

            var latestIsNumeric = int.TryParse(latestParts[i], out var latestNum);
            var currentIsNumeric = int.TryParse(currentParts[i], out var currentNum);

            if (latestIsNumeric && currentIsNumeric)
            {
                if (latestNum > currentNum) return 1;
                if (latestNum < currentNum) return -1;
            }
            else if (latestIsNumeric != currentIsNumeric)
            {
                return latestIsNumeric ? -1 : 1;
            }
            else
            {
                var comparison = string.Compare(latestParts[i], currentParts[i], StringComparison.Ordinal);
                if (comparison != 0) return comparison;
            }
        }

        return 0;
    }

    private static bool TryParseSemanticVersion(string value, out SemanticVersion version)
    {
        var match = SemanticVersionRegex().Match(value);
        if (!match.Success)
        {
            version = default;
            return false;
        }

        version = new SemanticVersion(
            $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}",
            match.Groups[4].Success,
            match.Groups[4].Success ? match.Groups[4].Value : "");
        return true;
    }

    public void Dispose()
    {
        leaseSource.Dispose();
    }

    private readonly record struct SemanticVersion(string CoreVersion, bool IsPrerelease, string PrereleaseLabel);

    [GeneratedRegex(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionRegex();
}

public sealed class LauncherUpdateCheckResult
{
    private LauncherUpdateCheckResult(
        bool isSuccessful,
        bool isUpdateAvailable,
        string latestVersion,
        IReadOnlyList<ReleaseFile> files,
        Exception? failureException = null,
        string? failureMessage = null)
    {
        IsSuccessful = isSuccessful;
        IsUpdateAvailable = isUpdateAvailable;
        LatestVersion = latestVersion;
        Files = files;
        FailureException = failureException;
        FailureMessage = failureMessage;
    }

    public bool IsSuccessful { get; }
    public bool IsUpdateAvailable { get; }
    public string LatestVersion { get; }

    public IReadOnlyList<ReleaseFile> Files { get; }
    public Exception? FailureException { get; }
    public string? FailureMessage { get; }

    internal static LauncherUpdateCheckResult Succeeded(
        string latestVersion,
        IReadOnlyList<ReleaseFile> files,
        bool isUpdateAvailable)
    {
        return new LauncherUpdateCheckResult(
            isSuccessful: true,
            isUpdateAvailable,
            latestVersion,
            files);
    }

    internal static LauncherUpdateCheckResult Failed(
        Exception? exception = null,
        string? message = null)
    {
        return new LauncherUpdateCheckResult(
            isSuccessful: false,
            isUpdateAvailable: false,
            latestVersion: "",
            files: [],
            failureException: exception,
            failureMessage: message);
    }
}
