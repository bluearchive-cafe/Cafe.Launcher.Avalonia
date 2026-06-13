using System;
using System.Diagnostics;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class ExternalLinkService
{
    private readonly LocalDiagnostics? diagnostics;

    /// <summary>
    /// Creates an ExternalLinkService without diagnostics (backward-compatible default).
    /// </summary>
    public ExternalLinkService()
    {
    }

    public ExternalLinkService(LocalDiagnostics diagnostics)
    {
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Opens a URL in the system browser. Only <c>http</c> and <c>https</c> schemes are
    /// allowed — file://, cmd://, and other schemes that could trigger arbitrary process
    /// execution are rejected.
    /// </summary>
    public void Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            diagnostics?.MessageAsync(
                "External link blocked by scheme validation",
                $"url: {url}").ContinueWith(_ => { });
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
            diagnostics?.MessageAsync(
                "External link failed to open",
                $"url: {uri.AbsoluteUri}\nexception: {ex.Message}").ContinueWith(_ => { });
        }
    }
}
