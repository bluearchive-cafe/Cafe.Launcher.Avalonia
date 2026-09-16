using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 已保存设置写入方的行为守卫：落盘值与编辑器必须一直是同一个值。这里钉住的三件事都是
/// 曾经真实存在的分叉——落盘的是归一化副本而编辑器留着未归一化的草稿，以及别的写入方
/// 改了盘上的字段而行写入方不知道。
/// </summary>
public sealed class SavedSettingsWriterTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public async Task UpdateAsync_ThenDraftSave_KeepsTheFieldWrittenByTheOtherWriter()
    {
        using var rig = CreateRig();
        rig.Editor.ApplySnapshot(new LauncherSettings { GamePath = @"C:\YostarGames\BlueArchive_JP" });

        // 资源面板写入手动 UID（不经设置页），随后用户在设置页改一项并保存。
        await rig.Writer.UpdateAsync(settings => settings.ResourcePanelUid = "ABCDEFGH");
        rig.Editor.Commit(settings => settings.EnableHttp2 = true);
        await rig.Writer.SaveDraftAsync();

        var persisted = await rig.SettingsService.ReadAsync();
        Assert.Equal("ABCDEFGH", persisted.ResourcePanelUid);
        Assert.True(persisted.EnableHttp2);
        Assert.Equal(@"C:\YostarGames\BlueArchive_JP", persisted.GamePath);
        Assert.Equal("ABCDEFGH", rig.Editor.GetSavedSnapshot().ResourcePanelUid);
    }

    [Fact]
    public async Task SaveDraftAsync_WhenDraftNeedsNormalizing_MakesThePersistedValueTheSnapshot()
    {
        using var rig = CreateRig();
        rig.Editor.ApplySnapshot(LauncherSettings.CreateDefaults());
        rig.Editor.Commit(settings => settings.ResourcePanelUid = "  abcdefgh  ");

        var persisted = await rig.Writer.SaveDraftAsync();

        // 归一化在落盘处发生（trim、色板去重、非法回退），编辑器拿到的是落盘的那个值：
        // 否则「已保存设置」在编辑器里是 abcdefgh、在磁盘上是 abcdefgh——两个不同字符串。
        Assert.Equal("abcdefgh", persisted.ResourcePanelUid);
        Assert.Equal("abcdefgh", rig.Editor.GetSavedSnapshot().ResourcePanelUid);
        Assert.Equal("abcdefgh", (await rig.SettingsService.ReadAsync()).ResourcePanelUid);
        Assert.False(rig.Editor.IsDirty);
    }

    [Fact]
    public async Task UpdateAsync_WhenFieldChanges_LandsOnDiskAndInTheEditorTogether()
    {
        using var rig = CreateRig();
        rig.Editor.ApplySnapshot(LauncherSettings.CreateDefaults());

        await rig.Writer.UpdateAsync(settings => settings.PatchUrlGroup = PatchUrlGroups.Cafe);

        Assert.Equal(PatchUrlGroups.Cafe, (await rig.SettingsService.ReadAsync()).PatchUrlGroup);
        Assert.Equal(PatchUrlGroups.Cafe, rig.Editor.GetSavedSnapshot().PatchUrlGroup);
        Assert.Equal(PatchUrlGroups.Cafe, rig.Editor.Current.PatchUrlGroup);
        Assert.False(rig.Editor.IsDirty);
    }

    [Fact]
    public async Task ReplaceAsync_WhenWholeSettingsReplace_KeepsDraftAndSnapshotOnTheReplacement()
    {
        using var rig = CreateRig();
        rig.Editor.ApplySnapshot(new LauncherSettings { GamePath = @"C:\old" });

        var replacement = LauncherSettings.CreateDefaults();
        replacement.GamePath = @"C:\new";
        await rig.Writer.ReplaceAsync(replacement);

        Assert.Equal(@"C:\new", rig.Editor.Current.GamePath);
        Assert.Equal(@"C:\new", rig.Editor.GetSavedSnapshot().GamePath);
        Assert.False(rig.Editor.IsDirty);
    }

    [Fact]
    public async Task UpdateAsync_WhenChangeIsNull_ThrowsArgumentNullException()
    {
        using var rig = CreateRig();

        await Assert.ThrowsAsync<ArgumentNullException>(() => rig.Writer.UpdateAsync(null!));
    }

    [Fact]
    public async Task ReplaceAsync_WhenSettingsAreNull_ThrowsArgumentNullException()
    {
        using var rig = CreateRig();

        await Assert.ThrowsAsync<ArgumentNullException>(() => rig.Writer.ReplaceAsync(null!));
    }

    private SavedSettingsTestRig CreateRig() =>
        new(tempDir.Sub(GamePaths.LauncherSettingsFileName));

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
