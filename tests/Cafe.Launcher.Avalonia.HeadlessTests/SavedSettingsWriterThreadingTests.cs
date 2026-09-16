using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// 唯一写入方的线程契约：编辑器是 UI 可观察状态（PropertyChanged 直接驱动绑定与命令
/// 可用性），因此收口必须落在 UI 线程。落盘续体在线程池线程上，若就地收口，通知会撞到
/// 绑定层的 VerifyAccess——用户看到的是保存设置弹出「保存启动器设置失败」。
/// </summary>
public sealed class SavedSettingsWriterThreadingTests
{
    [AvaloniaFact]
    public async Task SaveDraftAsync_FromUiThread_AppliesEditorSnapshotOnUiThread()
    {
        using var rig = new WriterRig();
        rig.Editor.Current.Language = LauncherLanguages.Japanese;

        await rig.Writer.SaveDraftAsync();

        rig.AssertSnapshotAppliedOnUiThread();
    }

    [AvaloniaFact]
    public async Task UpdateAsync_FromUiThread_AppliesEditorSnapshotOnUiThread()
    {
        using var rig = new WriterRig();

        await rig.Writer.UpdateAsync(settings => settings.Language = LauncherLanguages.Japanese);

        rig.AssertSnapshotAppliedOnUiThread();
    }

    [AvaloniaFact]
    public async Task ReplaceAsync_FromUiThread_AppliesEditorSnapshotOnUiThread()
    {
        using var rig = new WriterRig();

        await rig.Writer.ReplaceAsync(LauncherSettings.CreateDefaults());

        rig.AssertSnapshotAppliedOnUiThread();
    }

    /// <summary>
    /// 后台调用方同样要拿到 UI 线程上的收口：契约按编辑器（UI 可观察状态）定义，
    /// 而不是按调用方所在线程定义。
    /// </summary>
    [AvaloniaFact]
    public async Task SaveDraftAsync_FromWorkerThread_AppliesEditorSnapshotOnUiThread()
    {
        using var rig = new WriterRig();
        rig.Editor.Current.Language = LauncherLanguages.Japanese;

        // 带上限而非直接 await：若收口没有回到 UI 线程，这里会以超时失败而不是挂住整个测试进程。
        await Task.Run(() => rig.Writer.SaveDraftAsync()).WaitAsync(TimeSpan.FromSeconds(10));

        rig.AssertSnapshotAppliedOnUiThread();
    }

    private sealed class WriterRig : IDisposable
    {
        private readonly TestDirectory directory;
        private readonly ServiceProvider provider;
        private readonly List<(string Property, bool HasUiAccess)> notifications = [];

        public WriterRig()
        {
            directory = TestDirectory.Create(TestDirectoryCleanup.BestEffort);
            provider = HeadlessTestHost.CreateServiceProvider(directory);
            Writer = provider.GetRequiredService<ISavedSettingsWriter>();
            Editor = provider.GetRequiredService<ISettingsEditor>();
            Editor.PropertyChanged += (_, eventArgs) => notifications.Add(
                (eventArgs.PropertyName ?? "", Dispatcher.UIThread.CheckAccess()));
        }

        public ISavedSettingsWriter Writer { get; }

        public ISettingsEditor Editor { get; }

        public void AssertSnapshotAppliedOnUiThread()
        {
            var report = string.Join(
                "; ",
                notifications.Select(entry => $"{entry.Property}={entry.HasUiAccess}"));

            // 没有 Current 通知说明压根没走到收口，断言不能因为「没有通知」而空过。
            Assert.Contains(
                notifications,
                entry => entry.Property == nameof(ISettingsEditor.Current));
            Assert.All(
                notifications,
                entry => Assert.True(
                    entry.HasUiAccess,
                    $"编辑器通知落在非 UI 线程：{report}"));
        }

        public void Dispose()
        {
            provider.Dispose();
            directory.Dispose();
        }
    }
}
