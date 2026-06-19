using System;
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
    private string proxyMode = ProxyModes.Direct;

    public LauncherUpdateService(HttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
        httpClient = httpClientFactory.CreateClient(
            LauncherConstants.GitHubApiBaseUrl,
            TimeSpan.FromSeconds(15));
    }

    internal LauncherUpdateService(HttpMessageHandler handler)
    {
        httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(LauncherConstants.GitHubApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public void SetProxyMode(string value)
    {
        proxyMode = value == ProxyModes.System ? ProxyModes.System : ProxyModes.Direct;
    }

    /// <summary>
    /// Checks the latest non-draft, non-prerelease GitHub release.
    /// </summary>
    public async Task<LauncherUpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var lease = await CreateRequestClientAsync(cancellationToken);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                LauncherConstants.GitHubLatestReleasePath);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd($"{LauncherConstants.ProductName.Replace(" ", "-", StringComparison.Ordinal)}/{LauncherConstants.LauncherVersion}");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await lease.Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(
                stream,
                JsonOptions,
                cancellationToken);

            if (release is null
                || string.IsNullOrWhiteSpace(release.TagName)
                || string.IsNullOrWhiteSpace(release.HtmlUrl))
            {
                return LauncherUpdateCheckResult.Failed();
            }

            var latestVersion = NormalizeReleaseTag(release.TagName);
            if (!TryParseSemanticVersion(latestVersion, out _)
                || !TryParseSemanticVersion(LauncherConstants.LauncherVersion, out _))
            {
                return LauncherUpdateCheckResult.Failed();
            }

            return LauncherUpdateCheckResult.Succeeded(
                latestVersion,
                release.HtmlUrl,
                IsNewerVersion(latestVersion, LauncherConstants.LauncherVersion));
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

        return !latest.IsPrerelease && current.IsPrerelease;
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
            match.Groups[4].Success);
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
    }

    private readonly record struct SemanticVersion(string CoreVersion, bool IsPrerelease);

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

