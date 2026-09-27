using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 已保存设置的唯一写入方。settings.json 在生产代码中的每一次改写都经由此处。
///
/// 「已保存」的真值同时存在于磁盘与表现层的设置快照（草稿），两者只在一起推进时才是同一个值，
/// 因此本模块把落盘的归一化值交还给草稿所有者，使它成为新的草稿与快照。调用方只描述「改什么」，
/// 不再自己读盘、拼改、落盘、补草稿——那四步里的任何一步漏掉一次，磁盘与草稿就会分叉，而分叉
/// 的后果是用户的下一次保存把别人的值写回旧值。
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
/// 设置草稿的所有者：表现层的设置编辑器。协调器（本文件）需要它三件事——当前草稿（保存什么）、
/// 已保存快照（增量改动的基底）、以及把落盘值收回去（草稿与快照一起推进）。
/// </summary>
/// <remarks>
/// 这是 Core 到表现层的唯一反向接缝，刻意保持窄：线程编排（Avalonia 的 UI 线程要求）留在实现方，
/// 因为「绑定只能在 UI 线程更新」是表现层的知识，不是 Core 的。收口必须可等待——写入方返回时
/// 草稿必须已经拿掉落盘值（ADR-024），所以这里不用事件而用 <see cref="Task"/>。
/// </remarks>
public interface ISettingsDraftOwner
{
    /// <summary>用户正在编辑的草稿副本（设置页保存时落盘的就是它）。</summary>
    LauncherSettings GetDraftSnapshot();

    /// <summary>最后一次落盘的设置副本，增量改动的基底。</summary>
    LauncherSettings GetSavedSnapshot();

    /// <summary>落盘完成后收口草稿与快照；实现方负责把它调度到自己的线程上。</summary>
    Task ApplyPersistedAsync(LauncherSettings persisted, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="ISavedSettingsWriter"/> 的实现：<see cref="LauncherSettingsService"/> 负责盘上的
/// 归一化写入，<see cref="ISettingsDraftOwner"/>（表现层的设置编辑器）负责草稿与快照；本类只维护
/// 二者的一致性，因此不需要知道任何表现层类型。
/// </summary>
internal sealed class SavedSettingsWriter : ISavedSettingsWriter
{
    private readonly LauncherSettingsService settingsService;
    private readonly ISettingsDraftOwner draftOwner;

    public SavedSettingsWriter(LauncherSettingsService settingsService, ISettingsDraftOwner draftOwner)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(draftOwner);

        this.settingsService = settingsService;
        this.draftOwner = draftOwner;
    }

    public Task<LauncherSettings> SaveDraftAsync(CancellationToken cancellationToken = default) =>
        PersistAsync(draftOwner.GetDraftSnapshot(), cancellationToken);

    public Task<LauncherSettings> UpdateAsync(
        Action<LauncherSettings> change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        var next = draftOwner.GetSavedSnapshot();
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

    /// <summary>落盘并让草稿所有者收口：草稿与快照都是归一化后的那个值，两者因此不可能与磁盘分叉。</summary>
    private async Task<LauncherSettings> PersistAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        var persisted = await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        await draftOwner.ApplyPersistedAsync(persisted, cancellationToken).ConfigureAwait(false);
        return persisted;
    }
}
