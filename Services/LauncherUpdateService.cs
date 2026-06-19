using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Checks the public GitHub Releases API for launcher self-updates.
/// </summary>
public sealed partial class LauncherUpdateService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };
    private readonly IHttpClientLeaseSource leaseSource;
    private readonly string currentVersion;
    private readonly string gitHubToken;

    public LauncherUpdateService(HttpClientFactory httpClientFactory)
    {
        leaseSource = new ProxyAwareHttpClientLeaseSource(
            httpClientFactory,
            new Uri(ApiConfig.GitHubApiBaseUrl),
            TimeSpan.FromSeconds(15));
        currentVersion = BuildInfo.LauncherVersion;
        gitHubToken = Environment.GetEnvironmentVariable("CAFE_LAUNCHER_GITHUB_TOKEN") ?? "";
    }

    internal LauncherUpdateService(
        HttpMessageHandler handler,
        string? currentVersionOverride = null,
        string? gitHubTokenOverride = null)
    {
        leaseSource = new FixedHttpClientLeaseSource(
            handler,
            new Uri(ApiConfig.GitHubApiBaseUrl),
            TimeSpan.FromSeconds(15));
        currentVersion = currentVersionOverride ?? BuildInfo.LauncherVersion;
        gitHubToken = gitHubTokenOverride ?? Environment.GetEnvironmentVariable("CAFE_LAUNCHER_GITHUB_TOKEN") ?? "";
    }

    /// <summary>
    /// Checks the GitHub Releases API for launcher self-updates.
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
            var releases = await FetchReleasesAsync(
                useAuth: !string.IsNullOrEmpty(gitHubToken),
                proxyMode,
                cancellationToken);

            if (releases is null || releases.Count == 0)
            {
                return LauncherUpdateCheckResult.Failed();
            }

            if (!TryParseSemanticVersion(currentVersion, out _))
            {
                return LauncherUpdateCheckResult.Failed();
            }

            // Sort by semantic version descending so the latest by version is first,
            // not merely the most recently created release.
            releases.Sort((a, b) =>
            {
                var versionA = NormalizeReleaseTag(a.TagName);
                var versionB = NormalizeReleaseTag(b.TagName);
                if (!TryParseSemanticVersion(versionA, out _)
                    || !TryParseSemanticVersion(versionB, out _))
                {
                    return 0;
                }

                if (IsNewerVersion(versionA, versionB)) return -1;
                if (IsNewerVersion(versionB, versionA)) return 1;
                return 0;
            });

            // Filter: beta channel sees all releases; stable channel skips pre-releases.
            var targetRelease = updateChannel == UpdateChannels.Beta
                ? releases[0]
                : releases.FirstOrDefault(r => !r.Prerelease);

            if (targetRelease is null)
            {
                return LauncherUpdateCheckResult.Succeeded(
                    currentVersion,
                    "",
                    isUpdateAvailable: false);
            }

            if (string.IsNullOrWhiteSpace(targetRelease.TagName)
                || string.IsNullOrWhiteSpace(targetRelease.HtmlUrl))
            {
                return LauncherUpdateCheckResult.Failed();
            }

            var latestVersion = NormalizeReleaseTag(targetRelease.TagName);
            if (!TryParseSemanticVersion(latestVersion, out _))
            {
                return LauncherUpdateCheckResult.Failed();
            }

            return LauncherUpdateCheckResult.Succeeded(
                latestVersion,
                targetRelease.HtmlUrl,
                IsNewerVersion(latestVersion, currentVersion));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return LauncherUpdateCheckResult.Failed();
        }
        catch (JsonException)
        {
            return LauncherUpdateCheckResult.Failed();
        }
        catch (TaskCanceledException)
        {
            return LauncherUpdateCheckResult.Failed();
        }
    }

    private async Task<List<GitHubReleaseResponse>?> FetchReleasesAsync(
        bool useAuth,
        string proxyMode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FetchReleasesWithAuthAsync(useAuth, proxyMode, cancellationToken);
        }
        catch (HttpRequestException ex) when (useAuth && IsAuthenticationFailure(ex))
        {
            // Token is invalid or expired — fall back to unauthenticated.
            return await FetchReleasesWithAuthAsync(
                useAuth: false,
                proxyMode,
                cancellationToken);
        }
    }

    private static bool IsAuthenticationFailure(HttpRequestException ex) =>
        ex.StatusCode is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden;

    private async Task<List<GitHubReleaseResponse>?> FetchReleasesWithAuthAsync(
        bool useAuth,
        string proxyMode,
        CancellationToken cancellationToken)
    {
        using var lease = await leaseSource.CreateLeaseAsync(proxyMode, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ApiConfig.GitHubReleasesPath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd($"{LauncherConstants.ProductName.Replace(" ", "-", StringComparison.Ordinal)}/{currentVersion}");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        if (useAuth)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gitHubToken);
        }

        using var response = await lease.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<GitHubReleaseResponse>>(
            stream,
            JsonOptions,
            cancellationToken);
    }

    private static string NormalizeReleaseTag(string tagName)
    {
        var trimmed = tagName.Trim();
        return trimmed.StartsWith('v') ? trimmed[1..] : trimmed;
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

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }
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
        string releaseUrl)
    {
        IsSuccessful = isSuccessful;
        IsUpdateAvailable = isUpdateAvailable;
        LatestVersion = latestVersion;
        ReleaseUrl = releaseUrl;
    }

    public bool IsSuccessful { get; }
    public bool IsUpdateAvailable { get; }
    public string LatestVersion { get; }
    public string ReleaseUrl { get; }

    internal static LauncherUpdateCheckResult Succeeded(
        string latestVersion,
        string releaseUrl,
        bool isUpdateAvailable)
    {
        return new LauncherUpdateCheckResult(
            isSuccessful: true,
            isUpdateAvailable,
            latestVersion,
            releaseUrl);
    }

    internal static LauncherUpdateCheckResult Failed()
    {
        return new LauncherUpdateCheckResult(
            isSuccessful: false,
            isUpdateAvailable: false,
            latestVersion: "",
            releaseUrl: "");
    }
}

