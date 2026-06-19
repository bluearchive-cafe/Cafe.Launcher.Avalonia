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
    private readonly HttpClientFactory? httpClientFactory;
    private readonly HttpClient httpClient;
    private readonly string currentVersion;
    private readonly string gitHubToken;
    private string proxyMode = ProxyModes.Direct;

    public LauncherUpdateService(HttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
        httpClient = httpClientFactory.CreateClient(
            LauncherConstants.GitHubApiBaseUrl,
            TimeSpan.FromSeconds(15));
        currentVersion = LauncherConstants.LauncherVersion;
        gitHubToken = LauncherConstants.GitHubToken;
    }

    internal LauncherUpdateService(
        HttpMessageHandler handler,
        string? currentVersionOverride = null,
        string? gitHubTokenOverride = null)
    {
        httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(LauncherConstants.GitHubApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };
        currentVersion = currentVersionOverride ?? LauncherConstants.LauncherVersion;
        gitHubToken = gitHubTokenOverride ?? LauncherConstants.GitHubToken;
    }

    public void SetProxyMode(string value)
    {
        proxyMode = value == ProxyModes.System ? ProxyModes.System : ProxyModes.Direct;
    }

    /// <summary>
    /// Checks the GitHub Releases API for launcher self-updates.
    /// The <paramref name="updateChannel"/> controls whether pre-releases are considered:
    /// <see cref="UpdateChannels.Beta"/> includes pre-releases, <see cref="UpdateChannels.Stable"/> skips them.
    /// </summary>
    public async Task<LauncherUpdateCheckResult> CheckForUpdateAsync(
        string updateChannel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var lease = await CreateRequestClientAsync(cancellationToken);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                LauncherConstants.GitHubReleasesPath);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd($"{LauncherConstants.ProductName.Replace(" ", "-", StringComparison.Ordinal)}/{currentVersion}");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            if (!string.IsNullOrEmpty(gitHubToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gitHubToken);
            }

            using var response = await lease.Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var releases = await JsonSerializer.DeserializeAsync<List<GitHubReleaseResponse>>(
                stream,
                JsonOptions,
                cancellationToken);

            if (releases is null || releases.Count == 0)
            {
                return LauncherUpdateCheckResult.Failed();
            }

            if (!TryParseSemanticVersion(currentVersion, out _))
            {
                return LauncherUpdateCheckResult.Failed();
            }

            // Filter: beta channel sees all releases; stable channel skips pre-releases.
            var targetRelease = updateChannel == UpdateChannels.Beta
                ? releases[0]
                : releases.FirstOrDefault(r => !r.Prerelease);

            if (targetRelease is null)
            {
                // No compatible release found (e.g., stable channel with only pre-releases).
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

    private async Task<HttpClientLease> CreateRequestClientAsync(CancellationToken cancellationToken)
    {
        if (httpClientFactory is not null)
        {
            return await httpClientFactory.CreateLeaseAsync(
                proxyMode,
                httpClient.BaseAddress,
                httpClient.Timeout,
                cancellationToken);
        }

        return new HttpClientLease(httpClient);
    }

    public void Dispose()
    {
        httpClient.Dispose();
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

