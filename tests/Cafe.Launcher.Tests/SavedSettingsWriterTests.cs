using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Tests;

/// <summary>
/// 已保存设置写入方的行为守卫：落盘值与编辑器必须一直是同一个值。这里钉住的三件事都是
/// 曾经真实存在的分叉——落盘的是归一化副本而编辑器留着未归一化的草稿，以及别的写入方
/// 改了盘上的字段而行写入方不知道。
/// </summary>
public sealed class SavedSettingsWriterTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Theory]
    [InlineData("Update")]
    [InlineData("SaveDraft")]
    [InlineData("Replace")]
    [InlineData("UpdateGame")]
    public async Task WriteAsync_WhenAnotherUpdateWaitsForEditorSync_PreservesBothChanges(string operation)
    {
        using var service = new LauncherSettingsService(tempDir.DataRoot);
        var owner = new BlockingDraftOwner();
        using var writer = new SavedSettingsWriter(service, owner);
        owner.Editor.Current.ResourcePanelUid = "ABCDEFGH";
        var first = operation switch
        {
            "SaveDraft" => writer.SaveDraftAsync(),
            "Replace" => writer.ReplaceAsync(owner.Editor.GetSnapshot()),
            "UpdateGame" => writer.UpdateGameAsync(LauncherProfiles.BlueArchiveJapan.RuntimeId,
                settings => settings.ResourcePanelUid = "ABCDEFGH"),
            _ => writer.UpdateAsync(settings => settings.ResourcePanelUid = "ABCDEFGH")
        };
        await owner.ApplyEntered.Task;

        // 第二个调用必须在第一次同步编辑器之后才读取增量基底。
        var second = writer.UpdateAsync(settings => settings.ShowRemoteContentCard = false);
        owner.ReleaseApply.SetResult();
        await Task.WhenAll(first, second);

        var persisted = await service.ReadAsync();
        Assert.Equal("ABCDEFGH", persisted.ResourcePanelUid);
        Assert.False(persisted.ShowRemoteContentCard);
        Assert.True(persisted.HasSameSettingsState(owner.Editor.GetSavedSnapshot()));
    }

    [Fact]
    public async Task SaveDraftAsync_WhenWaitingForAnotherWrite_ReadsDraftAfterEditorSync()
    {
        using var service = new LauncherSettingsService(tempDir.DataRoot);
        var owner = new BlockingDraftOwner();
        using var writer = new SavedSettingsWriter(service, owner);
        var first = writer.UpdateAsync(settings => settings.ResourcePanelUid = "ABCDEFGH");
        await owner.ApplyEntered.Task;

        var second = writer.SaveDraftAsync();
        owner.ReleaseApply.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal("ABCDEFGH", (await service.ReadAsync()).ResourcePanelUid);
        Assert.Equal("ABCDEFGH", owner.Editor.GetSavedSnapshot().ResourcePanelUid);
    }

    [Fact]
    public async Task UpdateAsync_WhenWaitingCallIsCanceled_DoesNotRunItsChangeAndAllowsNextWrite()
    {
        using var service = new LauncherSettingsService(tempDir.DataRoot);
        var owner = new BlockingDraftOwner();
        using var writer = new SavedSettingsWriter(service, owner);
        var first = writer.UpdateAsync(settings => settings.ResourcePanelUid = "ABCDEFGH");
        await owner.ApplyEntered.Task;
        using var cancellation = new CancellationTokenSource();
        var changed = false;
        var waiting = writer.UpdateAsync(_ => changed = true, cancellation.Token);
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Assert.False(changed);
        }
        finally
        {
            owner.ReleaseApply.SetResult();
            await first;
        }

        await writer.UpdateAsync(settings => settings.ShowRemoteContentCard = false);
        var persisted = await service.ReadAsync();
        Assert.Equal("ABCDEFGH", persisted.ResourcePanelUid);
        Assert.False(persisted.ShowRemoteContentCard);
    }

    [Fact]
    public async Task UpdateAsync_WhenCanceledAfterDiskCommit_StillSynchronizesTheEditor()
    {
        using var service = new LauncherSettingsService(tempDir.DataRoot);
        var owner = new BlockingDraftOwner();
        using var writer = new SavedSettingsWriter(service, owner);
        using var cancellation = new CancellationTokenSource();
        var pending = writer.UpdateAsync(settings => settings.ResourcePanelUid = "ABCDEFGH", cancellation.Token);
        await owner.ApplyEntered.Task;

        cancellation.Cancel();
        owner.ReleaseApply.SetResult();
        await pending;

        var persisted = await service.ReadAsync();
        Assert.True(persisted.HasSameSettingsState(owner.Editor.GetSavedSnapshot()));
    }

    [Fact]
    public async Task UpdateAsync_WhenPersistenceFails_AllowsTheNextWrite()
    {
        using var rig = CreateRig();
        Directory.CreateDirectory(rig.SettingsService.SettingsPath);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            rig.Writer.UpdateAsync(settings => settings.ResourcePanelUid = "ABCDEFGH"));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Empty(rig.Editor.GetSavedSnapshot().ResourcePanelUid);
        Directory.Delete(rig.SettingsService.SettingsPath);

        await rig.Writer.UpdateAsync(settings => settings.ResourcePanelUid = "IJKLMNOP");

        Assert.Equal("IJKLMNOP", (await rig.SettingsService.ReadAsync()).ResourcePanelUid);
    }

    private sealed class BlockingDraftOwner : ISettingsDraftOwner
    {
        private int applications;
        public SettingsEditor Editor { get; } = new();
        public TaskCompletionSource ApplyEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseApply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LauncherSettings GetDraftSnapshot() => Editor.GetSnapshot();
        public LauncherSettings GetSavedSnapshot() => Editor.GetSavedSnapshot();

        public async Task ApplyPersistedAsync(LauncherSettings persisted, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref applications) == 1)
            {
                ApplyEntered.SetResult();
                await ReleaseApply.Task;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Editor.ApplySnapshot(persisted);
        }
    }

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

        await Assert.ThrowsAsync<ArgumentNullException>(() => rig.Writer.ReplaceAsync((LauncherSettings)null!));
    }

    [Fact]
    public async Task ReplaceAsync_WhenDerivingReplacement_ReadsTheSavedSnapshotAsBase()
    {
        using var rig = CreateRig();
        await rig.SeedAsync(new LauncherSettings { ResourcePanelUid = "ABCDEFGH" });

        var persisted = await rig.Writer.ReplaceAsync(settings =>
        {
            var next = LauncherSettings.CreateDefaults();
            next.ResourcePanelUid = settings.ResourcePanelUid;
            return next;
        });

        Assert.Equal("ABCDEFGH", persisted.ResourcePanelUid);
        Assert.Equal("ABCDEFGH", (await rig.SettingsService.ReadAsync()).ResourcePanelUid);
    }

    [Fact]
    public async Task UpdateAsync_WhenJsonIsTruncated_BacksUpOriginalAndPersistsSettings()
    {
        using var service = new LauncherSettingsService(tempDir.DataRoot);
        var owner = new SettingsEditor();
        using var writer = new SavedSettingsWriter(service, owner);
        var original = "{\"formatVersion\":3,\"games\":"u8.ToArray();
        await File.WriteAllBytesAsync(service.SettingsPath, original);
        var backup = tempDir.Sub("settings.corrupt.json");
        await File.WriteAllTextAsync(backup, "older backup");
        owner.ApplySnapshot(await service.ReadAsync());

        await writer.UpdateAsync(settings => settings.Language = LauncherLanguages.Japanese);

        Assert.Equal(original, await File.ReadAllBytesAsync(backup + ".1"));
        Assert.Equal("older backup", await File.ReadAllTextAsync(backup));
        Assert.Equal(LauncherLanguages.Japanese, (await service.ReadAsync()).Language);
        Assert.Equal(LauncherLanguages.Japanese, owner.GetSavedSnapshot().Language);
    }

    [Fact]
    public async Task UpdateAsync_WhenCorruptSettingsCannotBeBackedUp_LeavesOriginalInPlace()
    {
        using var service = new LauncherSettingsService(tempDir.DataRoot);
        var owner = new SettingsEditor();
        using var writer = new SavedSettingsWriter(service, owner);
        var original = "{"u8.ToArray();
        await File.WriteAllBytesAsync(service.SettingsPath, original);
        Directory.CreateDirectory(tempDir.Sub("settings.corrupt.json"));

        var error = await Record.ExceptionAsync(() => writer.UpdateAsync(settings => settings.Language = LauncherLanguages.Japanese));

        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        Assert.Equal(original, await File.ReadAllBytesAsync(service.SettingsPath));
        Assert.Empty(Directory.EnumerateFiles(tempDir, "*.tmp"));
    }

    [Fact]
    public async Task UpdateAsync_LegacyFileBeforeEditorInitialization_PreservesExistingSettings()
    {
        using var rig = CreateRig();
        const string legacy = """{"gamePath":"D:\\Existing","resourcePanelUid":"ABCDEFGH","language":"ja"}""";
        await File.WriteAllTextAsync(rig.SettingsService.SettingsPath, legacy);

        await rig.Writer.UpdateAsync(settings => settings.WindowWidth = 1280);

        var persisted = await rig.SettingsService.ReadAsync();
        Assert.Equal(@"D:\Existing", persisted.GamePath);
        Assert.Equal("ABCDEFGH", persisted.ResourcePanelUid);
        Assert.Equal("ja", persisted.Language);
        Assert.Equal(1280, persisted.WindowWidth);
    }

    [Fact]
    public async Task SaveDraftAsync_LegacyFileBeforeEditorInitialization_RejectsDefaultSnapshotOverwrite()
    {
        using var rig = CreateRig();
        const string legacy = """{"gamePath":"D:\\Existing","language":"ja"}""";
        await File.WriteAllTextAsync(rig.SettingsService.SettingsPath, legacy);
        rig.Editor.Current.ThemeMode = ThemeModes.Dark;

        await Assert.ThrowsAsync<SettingsFormatException>(() => rig.Writer.SaveDraftAsync());

        Assert.Equal(legacy, await File.ReadAllTextAsync(rig.SettingsService.SettingsPath));
        Assert.True(rig.Editor.IsDirty);
    }

    [Fact]
    public async Task UpdateGameAsync_WhenAnotherGameUpdateWaitsForEditorSync_PreservesBothGames()
    {
        using var service = new LauncherSettingsService(tempDir.DataRoot);
        var owner = new BlockingDraftOwner();
        using var writer = new SavedSettingsWriter(service, owner);
        var first = writer.UpdateGameAsync(LauncherProfiles.BlueArchiveJapan.RuntimeId,
            game => game.GamePath = @"C:\Games\BlueArchive_JP");
        await owner.ApplyEntered.Task;

        var second = writer.UpdateGameAsync(LauncherProfiles.StellaSoraChina.RuntimeId,
            game => game.GamePath = @"D:\Games\StellaSora_CN");
        owner.ReleaseApply.SetResult();
        await Task.WhenAll(first, second);

        var persisted = await service.ReadAsync();
        Assert.Equal(@"C:\Games\BlueArchive_JP", persisted.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).GamePath);
        Assert.Equal(@"D:\Games\StellaSora_CN", persisted.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GamePath);
        Assert.True(persisted.HasSameSettingsState(owner.Editor.Current));
        Assert.True(persisted.HasSameSettingsState(owner.Editor.GetSavedSnapshot()));
    }

    [Fact]
    public async Task UpdateGameAsync_WhenSelectionChangesWhileWaiting_KeepsTheExplicitTarget()
    {
        using var service = new LauncherSettingsService(tempDir.DataRoot);
        var owner = new BlockingDraftOwner();
        using var writer = new SavedSettingsWriter(service, owner);
        var first = writer.UpdateAsync(settings => settings.LastViewedGameId = LauncherProfiles.StellaSoraChina.RuntimeId);
        await owner.ApplyEntered.Task;

        var targeted = writer.UpdateGameAsync(LauncherProfiles.BlueArchiveJapan.RuntimeId,
            game => game.GamePath = @"C:\Games\BlueArchive_JP");
        owner.ReleaseApply.SetResult();
        await Task.WhenAll(first, targeted);

        var persisted = await service.ReadAsync();
        Assert.Equal(LauncherProfiles.StellaSoraChina.RuntimeId, persisted.LastViewedGameId);
        Assert.Equal(@"C:\Games\BlueArchive_JP", persisted.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).GamePath);
        Assert.Empty(persisted.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GamePath);
        Assert.True(persisted.HasSameSettingsState(owner.Editor.GetSavedSnapshot()));
    }

    [Fact]
    public async Task UpdateGameAsync_WhenUpdatesAreSequential_PreservesOtherGamesAndGlobalPreferences()
    {
        using var rig = CreateRig();
        await rig.Writer.ReplaceAsync(new LauncherSettings
        {
            Language = LauncherLanguages.Japanese,
            LastViewedGameId = LauncherProfiles.StellaSoraChina.RuntimeId
        });

        await rig.Writer.UpdateGameAsync(LauncherProfiles.BlueArchiveJapan.RuntimeId,
            game => game.GamePath = @"C:\Games\BlueArchive_JP");
        await rig.Writer.UpdateGameAsync(LauncherProfiles.StellaSoraChina.RuntimeId,
            game => game.GamePath = @"D:\Games\StellaSora_CN");

        var persisted = await rig.SettingsService.ReadAsync();
        Assert.Equal(LauncherLanguages.Japanese, persisted.Language);
        Assert.Equal(LauncherProfiles.StellaSoraChina.RuntimeId, persisted.LastViewedGameId);
        Assert.Equal(@"C:\Games\BlueArchive_JP", persisted.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).GamePath);
        Assert.Equal(@"D:\Games\StellaSora_CN", persisted.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GamePath);
        Assert.True(persisted.HasSameSettingsState(rig.Editor.Current));
        Assert.False(rig.Editor.IsDirty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("BlueArchive_JP")]
    [InlineData("BLUE-ARCHIVE-JP")]
    public async Task UpdateGameAsync_WhenIdentityIsInvalid_DoesNotRunChangeOrTouchSavedState(string gameId)
    {
        using var rig = CreateRig();
        await rig.Writer.ReplaceAsync(new LauncherSettings { GamePath = @"C:\Games\BlueArchive_JP" });
        var before = await File.ReadAllBytesAsync(rig.SettingsService.SettingsPath);
        var snapshot = rig.Editor.GetSavedSnapshot();
        var called = false;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => rig.Writer.UpdateGameAsync(gameId, _ => called = true));

        Assert.False(called);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.SettingsService.SettingsPath));
        Assert.True(snapshot.HasSameSettingsState(rig.Editor.Current));
        Assert.True(snapshot.HasSameSettingsState(rig.Editor.GetSavedSnapshot()));
        Assert.False(rig.Editor.IsDirty);
    }

    [Fact]
    public async Task UpdateGameAsync_WhenIdentityIsInvalidAndSettingsDoNotExist_DoesNotCreateFile()
    {
        using var rig = CreateRig();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => rig.Writer.UpdateGameAsync("unknown", game => game.GamePath = "changed"));

        Assert.False(File.Exists(rig.SettingsService.SettingsPath));
    }

    [Fact]
    public async Task UpdateGameAsync_WhenChangeIsNull_RejectsWithoutWriting()
    {
        using var rig = CreateRig();

        await Assert.ThrowsAsync<ArgumentNullException>(() => rig.Writer.UpdateGameAsync(LauncherProfiles.BlueArchiveJapan.RuntimeId, null!));

        Assert.False(File.Exists(rig.SettingsService.SettingsPath));
    }

    [Fact]
    public async Task UpdateGameAsync_WhenTargetNeedsNormalizing_SynchronizesTheNormalizedGame()
    {
        using var rig = CreateRig();

        var persisted = await rig.Writer.UpdateGameAsync(LauncherProfiles.BlueArchiveJapan.RuntimeId,
            game => game.ResourcePanelUid = "  abcdefgh  ");

        Assert.Equal("abcdefgh", persisted.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).ResourcePanelUid);
        Assert.Equal("abcdefgh", rig.Editor.Current.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).ResourcePanelUid);
        Assert.Equal("abcdefgh", (await rig.SettingsService.ReadAsync()).GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).ResourcePanelUid);
        Assert.False(rig.Editor.IsDirty);
    }

    [Fact]
    public async Task SaveDraftAsync_AfterAnotherGameWasUpdated_PreservesThatGamesSavedValue()
    {
        using var rig = CreateRig();
        await rig.Writer.UpdateGameAsync(LauncherProfiles.StellaSoraChina.RuntimeId,
            game => game.GamePath = @"D:\Games\StellaSora_CN");
        rig.Editor.Current.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).GamePath = @"C:\Games\BlueArchive_JP";

        await rig.Writer.SaveDraftAsync();

        var persisted = await rig.SettingsService.ReadAsync();
        Assert.Equal(@"C:\Games\BlueArchive_JP", persisted.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).GamePath);
        Assert.Equal(@"D:\Games\StellaSora_CN", persisted.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GamePath);
        Assert.True(persisted.HasSameSettingsState(rig.Editor.Current));
        Assert.False(rig.Editor.IsDirty);
    }

    [Theory]
    [InlineData("blue-archive-jp", "SaveDraft")]
    [InlineData("blue-archive-jp", "Update")]
    [InlineData("blue-archive-jp", "Replace")]
    [InlineData("blue-archive-jp", "UpdateGame")]
    [InlineData("stella-sora-cn", "SaveDraft")]
    [InlineData("stella-sora-cn", "Update")]
    [InlineData("stella-sora-cn", "Replace")]
    [InlineData("stella-sora-cn", "UpdateGame")]
    public async Task WriteAsync_WhenTargetHasAnActiveFileTask_RejectsItsSettingsButAllowsGlobalAndOtherGame(
        string gameId, string operation)
    {
        using var service = new LauncherSettingsService(tempDir.DataRoot);
        var owner = new SettingsEditor();
        var coordinator = new GameFileOperationCoordinator();
        using var writer = new SavedSettingsWriter(service, owner, coordinator);
        await writer.ReplaceAsync(new LauncherSettings());
        var before = await File.ReadAllBytesAsync(service.SettingsPath);
        using var taskLease = coordinator.TryAcquire(gameId, GameFileOperationKind.Download).Lease!;
        owner.Current.GetGameSettings(gameId).GamePath = tempDir.Sub("changed");

        await Assert.ThrowsAsync<InvalidOperationException>(() => operation switch
        {
            "SaveDraft" => writer.SaveDraftAsync(),
            "Update" => writer.UpdateAsync(settings => settings.GetGameSettings(gameId).GamePath = tempDir.Sub("changed")),
            "Replace" => writer.ReplaceAsync(owner.GetSnapshot()),
            _ => writer.UpdateGameAsync(gameId, game => game.GamePath = tempDir.Sub("changed"))
        });

        Assert.Equal(before, await File.ReadAllBytesAsync(service.SettingsPath));
        Assert.True(owner.IsDirty);
        var otherId = LauncherProfiles.SupportedGames.Single(profile => profile.RuntimeId != gameId).RuntimeId;
        await writer.UpdateAsync(settings =>
        {
            settings.ThemeMode = ThemeModes.Dark;
            settings.GetGameSettings(otherId).LaunchCheckMode = LaunchCheckModes.None;
        });
        var saved = await service.ReadAsync();
        Assert.Equal(ThemeModes.Dark, saved.ThemeMode);
        Assert.Equal(LaunchCheckModes.None, saved.GetGameSettings(otherId).LaunchCheckMode);
        Assert.Empty(saved.GetGameSettings(gameId).GamePath);
    }

    [Fact]
    public async Task UpdateGameAsync_UntilEditorSynchronizationFinishes_BlocksOnlyItsGameFromStarting()
    {
        using var service = new LauncherSettingsService(tempDir.DataRoot);
        var owner = new BlockingDraftOwner();
        var coordinator = new GameFileOperationCoordinator();
        using var writer = new SavedSettingsWriter(service, owner, coordinator);
        var gameId = LauncherProfiles.BlueArchiveJapan.RuntimeId;
        var write = writer.UpdateGameAsync(gameId, game => game.GamePath = tempDir.Sub("new-game"));
        await owner.ApplyEntered.Task;
        try
        {
            Assert.Equal(GameFileOperationAdmissionCode.SettingsChanging,
                coordinator.TryAcquire(gameId, GameFileOperationKind.Download).Code);
            using var other = coordinator.TryAcquire(LauncherProfiles.StellaSoraChina.RuntimeId, GameFileOperationKind.Repair).Lease;
            Assert.NotNull(other);
        }
        finally
        {
            owner.ReleaseApply.SetResult();
        }

        await write;
        using var admitted = coordinator.TryAcquire(gameId, GameFileOperationKind.Download).Lease;
        Assert.NotNull(admitted);
    }

    [Fact]
    public async Task UpdateGameAsync_WhenQueuedBehindGlobalWrite_RechecksTaskAdmissionInsideItsTransaction()
    {
        using var service = new LauncherSettingsService(tempDir.DataRoot);
        var owner = new BlockingDraftOwner();
        var coordinator = new GameFileOperationCoordinator();
        using var writer = new SavedSettingsWriter(service, owner, coordinator);
        var first = writer.UpdateAsync(settings => settings.ThemeMode = ThemeModes.Dark);
        await owner.ApplyEntered.Task;
        var second = writer.UpdateGameAsync(LauncherProfiles.BlueArchiveJapan.RuntimeId,
            game => game.GamePath = tempDir.Sub("late-change"));
        using var active = coordinator.TryAcquire(LauncherProfiles.BlueArchiveJapan.RuntimeId, GameFileOperationKind.Repair).Lease;
        Assert.NotNull(active);
        owner.ReleaseApply.SetResult();
        await first;

        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        var saved = await service.ReadAsync();
        Assert.Equal(ThemeModes.Dark, saved.ThemeMode);
        Assert.Empty(saved.GamePath);
    }

    [Fact]
    public async Task UpdateGameAsync_WhenAProgrammaticPathChangeOverlapsAnotherInstallation_RejectsWithoutWriting()
    {
        using var service = new LauncherSettingsService(tempDir.DataRoot);
        var owner = new SettingsEditor();
        using var writer = new SavedSettingsWriter(service, owner, new GameFileOperationCoordinator());
        var other = new GameInstallationPath(LauncherProfiles.StellaSoraChina).NormalizeGamePath(tempDir.Sub("games"));
        await writer.UpdateGameAsync(LauncherProfiles.StellaSoraChina.RuntimeId, game => game.GamePath = other);
        var before = await File.ReadAllBytesAsync(service.SettingsPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.UpdateGameAsync(
            LauncherProfiles.BlueArchiveJapan.RuntimeId, game => game.GamePath = other));

        Assert.Equal(before, await File.ReadAllBytesAsync(service.SettingsPath));
        Assert.Empty(owner.GetSavedSnapshot().GamePath);
    }

    private SavedSettingsTestRig CreateRig() =>
        new(tempDir.Sub(LauncherPaths.LauncherSettingsFileName));

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
