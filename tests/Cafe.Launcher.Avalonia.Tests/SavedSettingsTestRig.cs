using System;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 已保存设置的测试装配：设置服务 ＋ 它们的编辑器 ＋ 唯一写入方，三者的关系与生产一致。
///
/// 生产里 <see cref="ISettingsEditor"/> 与 <see cref="ISavedSettingsWriter"/> 都是全局单例，
/// 测试里必须成对创建并贯穿同一张对象图：写入方以编辑器的设置快照为基底，随手 new 一个
/// 空编辑器会把「在已保存设置上改一个字段」变成「把已保存设置改回默认值再改一个字段」，
/// 于是测试里的落盘内容与生产不同。
/// </summary>
internal sealed class SavedSettingsTestRig : IDisposable
{
    public SavedSettingsTestRig(string settingsPath)
        : this(new LauncherSettingsService( TestDataRoot.ForFile(settingsPath) ))
    {
    }

    public SavedSettingsTestRig(LauncherDataRoot dataRoot)
        : this(new LauncherSettingsService(dataRoot))
    {
    }

    public SavedSettingsTestRig(LauncherSettingsService settingsService)
    {
        SettingsService = settingsService;
        Editor = new SettingsEditor();
        Writer = new SavedSettingsWriter(settingsService, Editor);
    }

    public LauncherSettingsService SettingsService { get; }

    public SettingsEditor Editor { get; }

    public ISavedSettingsWriter Writer { get; }

    /// <summary>把设置作为已保存设置写下去：与生产同一条路，编辑器随后与磁盘一致。</summary>
    public Task<LauncherSettings> SeedAsync(LauncherSettings settings) => Writer.ReplaceAsync(settings);

    public void Dispose() => SettingsService.Dispose();
}
