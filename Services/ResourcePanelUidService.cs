using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class ResourcePanelUidService
{
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
        if (!string.IsNullOrWhiteSpace(cookieUid))
        {
            return cookieUid;
        }

        var settings = await settingsService.ReadAsync(cancellationToken);
        return settings.ResourcePanelUid.Trim();
    }

    public async Task SaveManualUidAsync(string uid, CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.ReadAsync(cancellationToken);
        settings.ResourcePanelUid = uid.Trim();
        await settingsService.SaveAsync(settings, cancellationToken);
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
            return library.Cookies.FirstOrDefault(cookie => cookie.Name == "uid"
                && !string.IsNullOrWhiteSpace(cookie.Value))?.Value ?? "";
        }
        catch
        {
            return "";
        }
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
