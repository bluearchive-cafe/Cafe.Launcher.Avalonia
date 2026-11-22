using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

internal sealed class NoticeStateService : INoticeStateStore
{
    private readonly string statePath;
    private readonly string? legacyStatePath;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public NoticeStateService(LauncherDataRoot dataRoot)
        : this((dataRoot ?? throw new ArgumentNullException(nameof(dataRoot))).NoticeStatePath)
    {
    }

    /// <summary>读写指定游戏的公告状态；仅 BA 私有文件缺失时只读兼容旧根，首次写入保留旧文件。</summary>
    public NoticeStateService(GameDataPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).NoticeStatePath)
    {
        if (string.Equals(paths.GameId, LauncherProfiles.BlueArchiveJapan.RuntimeId, StringComparison.Ordinal))
        {
            legacyStatePath = paths.DataRoot.NoticeStatePath;
        }
    }

    private NoticeStateService(string statePath)
    {
        this.statePath = statePath;
    }

    public async Task<HashSet<string>> ReadShownNoticesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // A committed private file is authoritative. Do not merge an old root again after
            // migration, and never interpret that BA-only legacy file as another game's state.
            var path = File.Exists(statePath) ? statePath : legacyStatePath;
            if (path is null || !File.Exists(path))
            {
                return [];
            }

            return await AtomicJsonFileStore.ReadAsync<HashSet<string>>(
                path,
                CoreJsonContext.Default.HashSetString,
                cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (Exception exception) when (StorageFailure.IsRecoverableOrInvalidJson(exception))
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

            var path = statePath;
            await AtomicJsonFileStore.WriteAsync(
                path,
                shown,
                CoreJsonContext.Default.HashSetString,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (StorageFailure.IsRecoverableOrInvalidJson(exception))
        {
            // Notice state is best-effort and must not block launcher startup.
        }
        finally
        {
            writeLock.Release();
        }
    }
}
