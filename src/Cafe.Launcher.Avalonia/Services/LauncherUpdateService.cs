using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
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
public sealed partial class LauncherUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Strict;
    private readonly IRemoteHttpTransport transport;
    private readonly string currentVersion;
    private readonly LocalDiagnostics? diagnostics;

    /// <summary>Production constructor — accepts dependencies from DI.</summary>
    public LauncherUpdateService(
        IRemoteHttpTransport transport,
        LocalDiagnostics diagnostics)
    {
        this.transport = transport;
        currentVersion = BuildInfo.LauncherVersion;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Injectable constructor — accepts a transport and a version override for
    /// testability (the stub transport is the second adapter at that seam).
    /// </summary>
    internal LauncherUpdateService(
        IRemoteHttpTransport transport,
        string? currentVersionOverride = null,
        LocalDiagnostics? diagnosticsOverride = null)
    {
        this.transport = transport;
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
        CancellationToken cancellationToken = default)
    {
        try
        {
            var releases = await FetchReleasesAsync(cancellationToken).ConfigureAwait(false);

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
                        "LauncherUpdate",
                        $"Launcher update check failed — invalid release file data{Environment.NewLine}" +
                        $"version: {targetRelease.Version}{Environment.NewLine}{validationError}",
                        CancellationToken.None).ConfigureAwait(false);
                }

                return LauncherUpdateCheckResult.Failed(message: validationError);
            }

            var isUpdateAvailable = IsNewerVersion(targetRelease.Version, currentVersion);
            var releaseNotes = targetRelease.ReleaseNotes;
            if (isUpdateAvailable && string.IsNullOrWhiteSpace(releaseNotes))
            {
                releaseNotes = await TryFetchGitHubReleaseNotesAsync(
                    targetRelease.Version,
                    cancellationToken).ConfigureAwait(false);
            }

            return LauncherUpdateCheckResult.Succeeded(
                targetRelease.Version,
                Array.AsReadOnly(targetRelease.Files.ToArray()),
                isUpdateAvailable,
                releaseNotes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            if (diagnostics is not null)
                await diagnostics.ErrorAsync(
                    "LauncherUpdate",
                    "Launcher update check failed — HTTP request error",
                    ex,
                    CancellationToken.None).ConfigureAwait(false);
            return LauncherUpdateCheckResult.Failed(exception: ex);
        }
        catch (JsonException ex)
        {
            if (diagnostics is not null)
                await diagnostics.ErrorAsync(
                    "LauncherUpdate",
                    "Launcher update check failed — JSON deserialization error",
                    ex,
                    CancellationToken.None).ConfigureAwait(false);
            return LauncherUpdateCheckResult.Failed(exception: ex);
        }
        catch (TaskCanceledException ex)
        {
            if (diagnostics is not null)
                await diagnostics.ErrorAsync(
                    "LauncherUpdate",
                    "Launcher update check failed — request timeout",
                    ex,
                    CancellationToken.None).ConfigureAwait(false);
            return LauncherUpdateCheckResult.Failed(exception: ex);
        }
        catch (InvalidOperationException ex)
        {
            // 传输层契约把 URL 校验拒绝（scheme、端口、私网地址等）映射为
            // InvalidOperationException；本方法 try 范围内没有其它该类型的来源，
            // 此捕获是精确的。被拒绝的 URL 是一次失败的检查而非崩溃：让调用方
            // 以失败 toast 呈现（CR-20260921-070313-7BDC）。
            if (diagnostics is not null)
                await diagnostics.ErrorAsync(
                    "LauncherUpdate",
                    "Launcher update check failed — request URL was rejected",
                    ex,
                    CancellationToken.None).ConfigureAwait(false);
            return LauncherUpdateCheckResult.Failed(exception: ex);
        }
    }

    private async Task<List<LauncherReleaseResponse>?> FetchReleasesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await FetchProxyReleasesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException
            || exception is InvalidOperationException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A slow proxy endpoint surfaces as TaskCanceledException (HttpClient timeout)
            // rather than HttpRequestException; that is precisely when the GitHub fallback
            // matters most, so both degrade to it. The URL validator's rejection (an
            // InvalidOperationException per the transport contract — e.g. poisoned DNS
            // answering a private address) is the same "no usable answer" condition, and
            // the GitHub endpoint is resolved and validated independently of the proxy
            // endpoint. Caller cancellation still propagates.
            return await FetchGitHubReleasesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<LauncherReleaseResponse>?> FetchProxyReleasesAsync(
        CancellationToken cancellationToken)
    {
        // LauncherApiBaseUrl ends with '/' and the path starts with '/', so plain
        // string concatenation would produce a double slash; Uri-relative resolution
        // replaces the base path instead. No transport-level retries: the endpoint's
        // failures degrade to the GitHub fallback instead of being replayed.
        var requestUri = new Uri(new Uri(ApiConfig.LauncherApiBaseUrl), ApiConfig.LauncherReleasesPath);
        return await transport.GetJsonAsync<List<LauncherReleaseResponse>>(
            requestUri,
            new RemoteRequestOptions
            {
                Timeout = TimeSpan.FromSeconds(15),
                Json = JsonOptions
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<LauncherReleaseResponse>> FetchGitHubReleasesAsync(
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(ApiConfig.GitHubReleasesApiUrl);
        var releases = await transport.GetJsonAsync<List<GitHubRelease>>(
            requestUri,
            new RemoteRequestOptions
            {
                Timeout = TimeSpan.FromSeconds(15),
                Json = JsonOptions,
                ConfigureRequest = request => request.Headers.UserAgent.ParseAdd(
                    $"CafeLauncher/{BuildInfo.LauncherVersion}")
            },
            cancellationToken).ConfigureAwait(false) ?? [];
        return releases
            .Where(release => !release.Draft)
            .Select(release => new LauncherReleaseResponse
            {
                Version = NormalizeGitHubTag(release.TagName),
                ReleaseDate = release.PublishedAt,
                ReleaseNotes = release.Body,
                Files = release.Assets
                    .Where(asset => string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase))
                    .Select(asset => new ReleaseFile
                    {
                        Name = asset.Name,
                        Url = asset.BrowserDownloadUrl,
                        Size = asset.Size
                    })
                    .ToList()
            })
            .ToList();
    }

    private async Task<string> TryFetchGitHubReleaseNotesAsync(
        string version,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = new Uri(ApiConfig.GitHubReleaseByTagApiUrl + "v" + Uri.EscapeDataString(version));
            var release = await transport.GetJsonAsync<GitHubRelease>(
                requestUri,
                new RemoteRequestOptions
                {
                    Timeout = TimeSpan.FromSeconds(15),
                    Json = JsonOptions,
                    ConfigureRequest = request => request.Headers.UserAgent.ParseAdd(
                        $"CafeLauncher/{BuildInfo.LauncherVersion}")
                },
                cancellationToken).ConfigureAwait(false);
            return release?.Body ?? "";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or JsonException
            or InvalidOperationException
            or TaskCanceledException)
        {
            if (diagnostics is not null)
            {
                await diagnostics.MessageAsync(
                    "LauncherUpdate",
                    $"Release notes could not be loaded: {exception.Message}",
                    CancellationToken.None).ConfigureAwait(false);
            }

            return "";
        }
    }

    private static string NormalizeGitHubTag(string tagName) =>
        tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? tagName[1..]
            : tagName;

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
        return VersionComparer.ComparePrerelease(latest.PrereleaseLabel, current.PrereleaseLabel) > 0;
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

    private readonly record struct SemanticVersion(string CoreVersion, bool IsPrerelease, string PrereleaseLabel);

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; set; }

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; } = "";
    }

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
        string releaseNotes,
        Exception? failureException = null,
        string? failureMessage = null)
    {
        IsSuccessful = isSuccessful;
        IsUpdateAvailable = isUpdateAvailable;
        LatestVersion = latestVersion;
        Files = files;
        ReleaseNotes = releaseNotes;
        FailureException = failureException;
        FailureMessage = failureMessage;
    }

    public bool IsSuccessful { get; }
    public bool IsUpdateAvailable { get; }
    public string LatestVersion { get; }

    public IReadOnlyList<ReleaseFile> Files { get; }
    public string ReleaseNotes { get; }
    public Exception? FailureException { get; }
    public string? FailureMessage { get; }

    internal static LauncherUpdateCheckResult Succeeded(
        string latestVersion,
        IReadOnlyList<ReleaseFile> files,
        bool isUpdateAvailable,
        string releaseNotes = "")
    {
        return new LauncherUpdateCheckResult(
            isSuccessful: true,
            isUpdateAvailable,
            latestVersion,
            files,
            releaseNotes);
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
            releaseNotes: "",
            failureException: exception,
            failureMessage: message);
    }
}
