using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

internal sealed class TitleVideoAudioReader : IGameTitleVideoAudioReader
{
    public TitleVideoAudioReadResult Read(string titleVideoAudioPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titleVideoAudioPath);

        try
        {
            using var stream = new FileStream(
                titleVideoAudioPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var document = JsonDocument.Parse(stream);
            return Parse(document.RootElement);
        }
        catch (FileNotFoundException)
        {
            return Failure(TitleVideoAudioReadStatus.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(TitleVideoAudioReadStatus.Missing);
        }
        catch (JsonException)
        {
            return Failure(TitleVideoAudioReadStatus.InvalidJson);
        }
        catch (IOException)
        {
            return Failure(TitleVideoAudioReadStatus.Unreadable);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(TitleVideoAudioReadStatus.Unreadable);
        }
    }

    private static TitleVideoAudioReadResult Parse(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object
            || !root.TryGetProperty("saved", out var saved)
            || saved.ValueKind is not JsonValueKind.Array)
        {
            return Failure(TitleVideoAudioReadStatus.InvalidJson);
        }

        var entries = new List<TitleVideoAudioEntry>();
        foreach (var item in saved.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.Object
                || !item.TryGetProperty("Id", out var idNode)
                || !idNode.TryGetInt64(out var id)
                || !item.TryGetProperty("VideoPath", out var videoPathNode)
                || videoPathNode.ValueKind is not JsonValueKind.String
                || string.IsNullOrWhiteSpace(videoPathNode.GetString())
                || !item.TryGetProperty("BgmId", out var bgmIdNode)
                || !bgmIdNode.TryGetInt64(out var bgmId)
                || !item.TryGetProperty("BgmPath", out var bgmPathNode)
                || bgmPathNode.ValueKind is not JsonValueKind.String
                || string.IsNullOrWhiteSpace(bgmPathNode.GetString()))
            {
                return Failure(TitleVideoAudioReadStatus.InvalidJson);
            }

            entries.Add(new TitleVideoAudioEntry(
                id,
                videoPathNode.GetString()!,
                bgmId,
                bgmPathNode.GetString()!));
        }

        var lastOpenedId = 0L;
        if (root.TryGetProperty("LastOpenedId", out var lastOpenedNode)
            && lastOpenedNode.TryGetInt64(out var parsedLastOpenedId))
        {
            lastOpenedId = parsedLastOpenedId;
        }

        var version = 0;
        if (root.TryGetProperty("Version", out var versionNode)
            && versionNode.TryGetInt32(out var parsedVersion))
        {
            version = parsedVersion;
        }

        return new TitleVideoAudioReadResult(
            TitleVideoAudioReadStatus.Supported,
            new TitleVideoAudioSelection(entries, lastOpenedId, version));
    }

    private static TitleVideoAudioReadResult Failure(TitleVideoAudioReadStatus status) =>
        new(status, null);
}
