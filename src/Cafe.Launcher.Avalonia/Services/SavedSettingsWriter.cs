using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// 已保存设置的唯一写入方。settings.json 在生产代码中的每一次改写都经由此处。
///
/// 「已保存」的真值同时存在于磁盘与编辑器的设置快照，两者只在一起推进时才是同一个值，
/// 因此本模块把落盘的归一化值就地写回编辑器，使它成为新的设置草稿与设置快照。调用方
/// 只描述「改什么」，不再自己读盘、拼改、落盘、补草稿——那四步里的任何一步漏掉一次，
/// 磁盘与草稿就会分叉，而分叉的后果是用户的下一次保存把别人的值写回旧值。
/// </summary>
public interface ISavedSettingsWriter
{
    /// <summary>保存设置草稿（设置页的保存）。</summary>
    Task<LauncherSettings> SaveDraftAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 在已保存设置上施加一次改动（资源面板来源与 UID、游戏路径、窗口几何）。
    /// 基底是设置快照而非重新读盘：所有写入方都走这里，两者不会分叉。
    /// </summary>
    Task<LauncherSettings> UpdateAsync(
        Action<LauncherSettings> change,
        CancellationToken cancellationToken = default);

    /// <summary>整份替换已保存设置（首次设置向导完成、恢复默认设置）。</summary>
    Task<LauncherSettings> ReplaceAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="ISavedSettingsWriter"/> 的实现：<see cref="LauncherSettingsService"/> 负责
/// 盘上的归一化写入，<see cref="ISettingsEditor"/> 负责草稿与快照；本类只维护二者的一致性。
/// </summary>
public sealed class SavedSettingsWriter : ISavedSettingsWriter
{
    private readonly LauncherSettingsService settingsService;
    private readonly ISettingsEditor editor;

    public SavedSettingsWriter(LauncherSettingsService settingsService, ISettingsEditor editor)
    {
        this.settingsService = settingsService;
        this.editor = editor;
    }

    public Task<LauncherSettings> SaveDraftAsync(CancellationToken cancellationToken = default) =>
        PersistAsync(editor.GetSnapshot(), cancellationToken);

    public Task<LauncherSettings> UpdateAsync(
        Action<LauncherSettings> change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        var next = editor.GetSavedSnapshot();
        change(next);
        return PersistAsync(next, cancellationToken);
    }

    public Task<LauncherSettings> ReplaceAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return PersistAsync(settings, cancellationToken);
    }

    /// <summary>落盘并以落盘值收口编辑器：草稿与快照都是归一化后的那个值，编辑器因此不可能与磁盘分叉。</summary>
    private async Task<LauncherSettings> PersistAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        var persisted = await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        editor.ApplySnapshot(persisted);
        return persisted;
    }
}
