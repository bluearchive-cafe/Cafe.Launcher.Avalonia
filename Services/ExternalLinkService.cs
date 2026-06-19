using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Opens URLs in the system browser. Only browser links and mail links are allowed;
/// file://, cmd://, and other schemes that could trigger arbitrary process execution
/// are rejected.
/// </summary>
public static class ExternalLinkService
{
    /// <summary>
    /// Opens a URL in the system browser. Only http, https, and mailto schemes are allowed.
    /// </summary>
    public static void Open(string? url, LocalDiagnostics? diagnostics = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (!TryCreateAllowedUri(url, out var uri))
        {
            _ = LogDiagnosticsAsync(diagnostics, "External link blocked by scheme validation", $"url: {url}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _ = LogDiagnosticsAsync(diagnostics, "External link failed to open", $"url: {uri.AbsoluteUri}\nexception: {ex.Message}");
        }
    }

    public static bool TryCreateAllowedUri(string url, out Uri uri)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri!))
        {
            return false;
        }

        return uri.Scheme is "http" or "https" or "mailto";
    }

    private static async Task LogDiagnosticsAsync(LocalDiagnostics? diagnostics, string message, string details)
    {
        try
        {
            if (diagnostics is not null)
                await diagnostics.MessageAsync(message, details);
        }
        catch
        {
            // Best-effort diagnostics — must not throw.
        }
    }
}
