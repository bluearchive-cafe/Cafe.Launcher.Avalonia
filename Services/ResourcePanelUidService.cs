using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class ResourcePanelUidService
{
    private const string ResourcePanelCookieName = "uid";
    private const string ResourcePanelCookieDomain = "bluearchive.cafe";
    private const string ResourcePanelCookiePath = "/";

    /// <summary>
    /// UID format: exactly 8 uppercase ASCII letters (e.g. <c>ABCDEFGH</c>).
    /// Mirrors the dashboard's <c>/^[A-Z]{8}$/</c> validation so both clients
    /// reject the same invalid UIDs and never send malformed values to the server.
    /// </summary>
    private static readonly Regex UidFormat = new("^[A-Z]{8}$", RegexOptions.Compiled);

    /// <summary>Returns <see langword="true"/> when <paramref name="uid"/> matches the 8-uppercase-letter format.</summary>
    public static bool IsValidUid(string? uid)
    {
        return !string.IsNullOrEmpty(uid) && UidFormat.IsMatch(uid);
    }

    private readonly BestHttpCookieLibraryService cookieLibraryService;
    private readonly LauncherSettingsService settingsService;
    private readonly string cookieLibraryPath;

    public ResourcePanelUidService(
        BestHttpCookieLibraryService cookieLibraryService,
        LauncherSettingsService settingsService)
        : this(cookieLibraryService, settingsService, GetDefaultCookieLibraryPath())
    {
    }

    internal ResourcePanelUidService(
        BestHttpCookieLibraryService cookieLibraryService,
        LauncherSettingsService settingsService,
        string cookieLibraryPath)
    {
        this.cookieLibraryService = cookieLibraryService;
        this.settingsService = settingsService;
        this.cookieLibraryPath = cookieLibraryPath;
    }

    public string CookieLibraryPath => cookieLibraryPath;

    public async Task<string> ResolveUidAsync(CancellationToken cancellationToken = default)
    {
        var cookieUid = TryReadCookieUid();
        if (IsValidUid(cookieUid))
        {
            return cookieUid;
        }

        var settings = await settingsService.ReadAsync(cancellationToken).ConfigureAwait(false);
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

        var settings = await settingsService.ReadAsync(cancellationToken).ConfigureAwait(false);
        settings.ResourcePanelUid = trimmed;
        await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    private string TryReadCookieUid()
    {
        try
        {
            if (!File.Exists(cookieLibraryPath))
            {
                return "";
            }

            var library = cookieLibraryService.Read(cookieLibraryPath);
            return library.Cookies.FirstOrDefault(IsResourcePanelUidCookie)?.Value ?? "";
        }
        catch
        {
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
