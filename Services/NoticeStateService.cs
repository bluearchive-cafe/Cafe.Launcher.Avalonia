using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class NoticeStateService
{
    private readonly string? statePath;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public NoticeStateService(string? statePath = null)
    {
        this.statePath = statePath;
    }

    private string StatePath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(statePath))
            {
                return statePath;
            }

            return Path.Combine(
                LauncherUserDataDirectory.Root,
                "shown_notices.json");
        }
    }

    public async Task<HashSet<string>> ReadShownNoticesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var path = StatePath;
            if (!File.Exists(path))
            {
                return [];
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public async Task SaveShownNoticeAsync(string noticeHash, CancellationToken cancellationToken = default)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var shown = await ReadShownNoticesAsync(cancellationToken).ConfigureAwait(false);
            shown.Add(noticeHash);

            var path = StatePath;
            var parentDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            var tempPath = $"{path}.tmp";
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(shown), cancellationToken).ConfigureAwait(false);
            await Task.Run(() => File.Move(tempPath, path, overwrite: true), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Notice state is best-effort and must not block launcher startup.
        }
        finally
        {
            writeLock.Release();
        }
    }
}
