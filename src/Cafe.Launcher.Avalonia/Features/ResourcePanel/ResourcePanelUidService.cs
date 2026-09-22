using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Features.ResourcePanel;

public sealed partial class ResourcePanelUidService
{
    private const string ResourcePanelCookieName = "uid";
    private const string ResourcePanelCookieDomain = "bluearchive.cafe";
    private const string ResourcePanelCookiePath = "/";
    private readonly LocalDiagnostics? diagnostics;

    /// <summary>
    /// UID format: exactly 8 uppercase ASCII letters (e.g. <c>ABCDEFGH</c>).
    /// Mirrors the dashboard's <c>/^[A-Z]{8}$/</c> validation so both clients
    /// reject the same invalid UIDs and never send malformed values to the server.
    /// </summary>
    [GeneratedRegex("^[A-Z]{8}$")]
    private static partial Regex UidFormat { get; }

    /// <summary>Returns <see langword="true"/> when <paramref name="uid"/> matches the 8-uppercase-letter format.</summary>
    public static bool IsValidUid(string? uid)
    {
        return !string.IsNullOrEmpty(uid) && UidFormat.IsMatch(uid);
    }

    private readonly BestHttpCookieLibraryService cookieLibraryService;
    private readonly LauncherSettingsService settingsService;
    private readonly ISavedSettingsWriter savedSettingsWriter;
    private readonly string? cookieLibraryPathOverride;
    private string cookieLibraryPath;

    public ResourcePanelUidService(
        BestHttpCookieLibraryService cookieLibraryService,
        LauncherSettingsService settingsService,
        ISavedSettingsWriter savedSettingsWriter,
        LocalDiagnostics? diagnostics = null)
        : this(cookieLibraryService, settingsService, savedSettingsWriter, null, diagnostics)
    {
    }

    internal ResourcePanelUidService(
        BestHttpCookieLibraryService cookieLibraryService,
        LauncherSettingsService settingsService,
        ISavedSettingsWriter savedSettingsWriter,
        string? cookieLibraryPath,
        LocalDiagnostics? diagnostics = null)
    {
        this.cookieLibraryService = cookieLibraryService;
        this.settingsService = settingsService;
        this.savedSettingsWriter = savedSettingsWriter;
        cookieLibraryPathOverride = cookieLibraryPath;
        this.cookieLibraryPath = cookieLibraryPath ?? GetDefaultCookieLibraryPath();
        this.diagnostics = diagnostics;
    }

    public string CookieLibraryPath => cookieLibraryPath;

    public async Task<string> GetUidSourceAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.ReadAsync(cancellationToken).ConfigureAwait(false);
        return settings.ResourcePanelUidSource;
    }

    /// <summary>
    /// 写 UID 来源。走唯一写入方而非直接落盘：这个字段与设置草稿同属一份已保存设置，
    /// 落盘而不回写编辑器，用户在设置页的下一次保存就会把它写回旧值。
    /// </summary>
    internal async Task SaveUidSourceAsync(string uidSource, CancellationToken cancellationToken = default)
    {
        await savedSettingsWriter.UpdateAsync(
            settings => settings.ResourcePanelUidSource = uidSource,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ResolveUidAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.ReadAsync(cancellationToken).ConfigureAwait(false);
        return ResolveUidCore(settings, settings.ResourcePanelUidSource);
    }

    public async Task<string> ResolveUidWithSourceAsync(
        string uidSource,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.ReadAsync(cancellationToken).ConfigureAwait(false);
        return ResolveUidCore(settings, uidSource);
    }

    private string ResolveUidCore(LauncherSettings settings, string uidSource)
    {
        if (uidSource == ResourcePanelUidSources.Custom)
        {
            var customUid = settings.ResourcePanelUid.Trim();
            if (IsValidUid(customUid))
            {
                return customUid;
            }

            // Fallback: custom UID invalid, revert to auto-detection
            return ResolveAutoUidCore(settings);
        }

        return ResolveAutoUidCore(settings);
    }

    private string ResolveAutoUidCore(LauncherSettings settings)
    {
        var cookieUid = TryReadCookieUid(settings);
        if (IsValidUid(cookieUid))
        {
            return cookieUid;
        }

        var settingsUid = settings.ResourcePanelUid.Trim();
        return IsValidUid(settingsUid) ? settingsUid : "";
    }

    public async Task SaveManualUidAsync(string uid, CancellationToken cancellationToken = default)
    {
        var trimmed = uid.Trim();
        if (!IsValidUid(trimmed))
        {
            throw new ArgumentException("UID must be exactly 8 uppercase letters (A-Z).", nameof(uid));
        }

        await savedSettingsWriter.UpdateAsync(
            settings => settings.ResourcePanelUid = trimmed,
            cancellationToken).ConfigureAwait(false);
    }

    private string TryReadCookieUid(LauncherSettings settings)
    {
        try
        {
            cookieLibraryPath = cookieLibraryPathOverride ?? (OperatingSystem.IsLinux()
                ? ResolveLinuxCookieLibraryPath(settings.GameRuntime, Environment.UserName,
                    runner => GameCompatibilityPaths.GetDefaultPrefixPath(GameRuntimeIds.BlueArchiveJapan, runner))
                : GetDefaultCookieLibraryPath());
            if (!File.Exists(cookieLibraryPath))
            {
                return "";
            }

            var library = cookieLibraryService.Read(cookieLibraryPath);
            return library.Cookies.FirstOrDefault(IsResourcePanelUidCookie)?.Value ?? "";
        }
        catch (Exception exception)
        {
            _ = diagnostics?.WarningAsync(
                "ResourcePanelUid",
                $"Reading the cookie library failed: {exception.Message}");
            return "";
        }
    }

    private static bool IsResourcePanelUidCookie(BestHttpCookie cookie)
    {
        return cookie.Name == ResourcePanelCookieName
            && cookie.Domain == ResourcePanelCookieDomain
            && cookie.Path == ResourcePanelCookiePath
            && !string.IsNullOrWhiteSpace(cookie.Value);
    }

    /// <summary>
    /// Finds the cookie library only within the configured compatibility environment.
    /// Auto mode checks managed UMU then Wine prefixes; Wine user names need not match the host.
    /// </summary>
    internal static string ResolveLinuxCookieLibraryPath(
        GameRuntimeSettings settings, string userName, Func<string, string> defaultPrefix)
    {
        string[] runners = settings.Runner == GameRuntimeRunners.Wine
            ? [GameRuntimeRunners.Wine]
            : settings.Runner == GameRuntimeRunners.Umu
                ? [GameRuntimeRunners.Umu]
                : [GameRuntimeRunners.Umu, GameRuntimeRunners.Wine];
        string[] prefixes = !string.IsNullOrWhiteSpace(settings.PrefixPath)
            ? [settings.PrefixPath]
            : runners.Select(defaultPrefix).ToArray();
        var fallback = CookiePath(Path.Combine(prefixes[0], "drive_c", "users", userName));
        foreach (var prefix in prefixes)
        {
            var users = Path.Combine(prefix, "drive_c", "users");
            var currentUserPath = CookiePath(Path.Combine(users, userName));
            if (File.Exists(currentUserPath))
            {
                return currentUserPath;
            }

            if (!Directory.Exists(users))
            {
                continue;
            }

            foreach (var profile in Directory.EnumerateDirectories(users).Order(StringComparer.Ordinal))
            {
                var path = CookiePath(profile);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return fallback;
    }

    private static string CookiePath(string profile) =>
        Path.Combine(profile, "AppData", "LocalLow", "YostarJP", "BlueArchive", "Cookies", "Library");

    private static string GetDefaultCookieLibraryPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData",
            "LocalLow",
            "YostarJP",
            "BlueArchive",
            "Cookies",
            "Library");
    }
}
