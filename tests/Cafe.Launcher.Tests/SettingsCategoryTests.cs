using Cafe.Launcher.UI.Constants;
using System.ComponentModel;
using Cafe.Launcher.Composition;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class SettingsCategoryTests
{
    private static readonly string ResourceDirectory = Path.Combine(
        TestLocalizationHelper.FindProjectRoot(), "Resources");

    private static readonly string[] CategoryCodes =
    [
        SettingsCategoryCodes.General,
        SettingsCategoryCodes.Appearance,
        SettingsCategoryCodes.DefaultBackground,
        SettingsCategoryCodes.DownloadNetwork,
        SettingsCategoryCodes.Advanced,
        SettingsCategoryCodes.About
    ];

    private static readonly string[] GameCategoryCodes =
    [
        SettingsCategoryCodes.Game,
        SettingsCategoryCodes.GameLaunch,
        SettingsCategoryCodes.GameOptions,
        SettingsCategoryCodes.GameDownload,
        SettingsCategoryCodes.GameBackground
    ];

    private static readonly (string Key, Func<LocalizedTextCatalog, string> GetValue)[] CategoryLocalizedValues =
    [
        ("settingsCategoryGeneral", strings => strings["settingsCategoryGeneral"]),
        ("settingsCategoryAppearance", strings => strings["settingsCategoryAppearance"]),
        ("settingsDefaultBackground", strings => strings["settingsDefaultBackground"]),
        ("settingsCategoryDownloadNetwork", strings => strings["settingsCategoryDownloadNetwork"]),
        ("settingsCategoryAdvanced", strings => strings["settingsCategoryAdvanced"]),
        ("settingsCategoryAbout", strings => strings["settingsCategoryAbout"]),
        ("settingsGameInstallation", strings => strings["settingsGameInstallation"]),
        ("settingsGameLaunch", strings => strings["settingsGameLaunch"]),
        ("settingsGameOptions", strings => strings["settingsGameOptions"]),
        ("settingsGameDownload", strings => strings["settingsGameDownload"]),
        ("settingsGameBackground", strings => strings["settingsGameBackground"])
    ];

    private static readonly string[] CategoryDescriptionKeys =
    [
        "settingsCategoryGeneralDescription",
        "settingsCategoryAppearanceDescription",
        "settingsCategoryDownloadNetworkDescription",
        "settingsCategoryAdvancedDescription",
        "settingsCategoryAboutDescription",
        "settingsGameOptionsDescription"
    ];

    private static readonly string[] CategoryPropertyNames =
    [
        nameof(SettingsViewModel.SelectedCategoryOption),
        nameof(SettingsViewModel.IsGeneralCategorySelected),
        nameof(SettingsViewModel.IsGameCategorySelected),
        nameof(SettingsViewModel.IsGameLaunchCategorySelected),
        nameof(SettingsViewModel.IsGameOptionsCategorySelected),
        nameof(SettingsViewModel.IsGameDownloadCategorySelected),
        nameof(SettingsViewModel.IsGameBackgroundCategorySelected),
        nameof(SettingsViewModel.IsDownloadNetworkCategorySelected),
        nameof(SettingsViewModel.IsDefaultBackgroundCategorySelected),
        nameof(SettingsViewModel.IsAppearanceCategorySelected),
        nameof(SettingsViewModel.IsAdvancedCategorySelected),
        nameof(SettingsViewModel.IsAboutCategorySelected)
    ];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GENERAL")]
    [InlineData("unknown")]
    public void Normalize_InvalidCode_ReturnsGeneral(string? code)
    {
        Assert.Equal(SettingsCategoryCodes.General, SettingsCategoryCodes.Normalize(code));
    }

    [Theory]
    [InlineData("general")]
    [InlineData("game")]
    [InlineData("game-launch")]
    [InlineData("game-options")]
    [InlineData("game-download")]
    [InlineData("game-background")]
    [InlineData("download-network")]
    [InlineData("default-background")]
    [InlineData("appearance")]
    [InlineData("advanced")]
    [InlineData("about")]
    public void Normalize_ExactCode_ReturnsCode(string code)
    {
        Assert.Equal(code, SettingsCategoryCodes.Normalize(code));
    }

    [Fact]
    public void Selection_DefaultsToGeneralAndNormalizesUnknownCode()
    {
        using var provider = new ServiceCollection().AddLauncherServices().BuildServiceProvider();
        var viewModel = provider.GetRequiredService<SettingsViewModel>();

        Assert.Equal(SettingsCategoryCodes.General, viewModel.SelectedCategory);
        Assert.True(viewModel.IsGeneralCategorySelected);

        viewModel.SelectedCategory = SettingsCategoryCodes.Appearance;
        Assert.True(viewModel.IsAppearanceCategorySelected);
        Assert.False(viewModel.IsGeneralCategorySelected);

        viewModel.SelectedCategory = "ABOUT";
        Assert.Equal(SettingsCategoryCodes.General, viewModel.SelectedCategory);
    }

    [Fact]
    public void Selection_DoesNotChangeDraftAndSurvivesSnapshotLoading()
    {
        using var provider = new ServiceCollection().AddLauncherServices().BuildServiceProvider();
        var viewModel = provider.GetRequiredService<SettingsViewModel>();
        viewModel.LoadFromSnapshot(new LauncherSettings { Language = LauncherLanguages.English });
        viewModel.Editor.Current.Language = LauncherLanguages.Japanese;

        viewModel.SelectedCategory = SettingsCategoryCodes.DownloadNetwork;
        viewModel.LoadFromSnapshot(viewModel.Editor.GetSnapshot());

        Assert.Equal(SettingsCategoryCodes.DownloadNetwork, viewModel.SelectedCategory);
        Assert.Equal(LauncherLanguages.Japanese, viewModel.Editor.Current.Language);
    }

    [Fact]
    public void Options_AreRebuiltInFixedOrderWithLocalizedNames()
    {
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.English);
        var options = new SettingsOptionsViewModel(localizer, new DiskSpaceService());

        options.RefreshDisplayNames();

        Assert.Equal(
            [SettingsCategoryCodes.General, SettingsCategoryCodes.Appearance,
             SettingsCategoryCodes.DefaultBackground, SettingsCategoryCodes.DownloadNetwork,
             SettingsCategoryCodes.Advanced,
             SettingsCategoryCodes.About],
            options.SettingsCategories.Select(option => option.Code));
        Assert.All(options.SettingsCategories, option => Assert.False(string.IsNullOrWhiteSpace(option.DisplayName)));
        Assert.Equal(GameCategoryCodes, options.GameSettingsCategories.Select(option => option.Code));
        Assert.All(options.GameSettingsCategories, option => Assert.False(string.IsNullOrWhiteSpace(option.DisplayName)));
    }

    [Fact]
    public void CategoryIcons_UseOutlinedAndFilledPairs()
    {
        var options = new SettingsOptionsViewModel(new LocalizationService(), new DiskSpaceService());
        options.RefreshDisplayNames();

        Assert.Equal(
            [
                FluentGlyphs.Setting,
                FluentGlyphs.ColorPalette,
                FluentGlyphs.Image,
                FluentGlyphs.Download,
                FluentGlyphs.Equalizer,
                FluentGlyphs.Info
            ],
            options.SettingsCategories.Select(option => option.CategoryGlyph));
        Assert.Equal(
            [
                FluentGlyphs.Folder,
                FluentGlyphs.Play,
                FluentGlyphs.Setting,
                FluentGlyphs.Download,
                FluentGlyphs.Image
            ],
            options.GameSettingsCategories.Select(option => option.CategoryGlyph));
    }

    [Theory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public void CategoryLocalization_HasNamesAndDescriptions(string language)
    {
        var localizer = new LocalizationService();
        localizer.SetLanguage(language);

        foreach (var (key, _) in CategoryLocalizedValues)
        {
            Assert.NotEqual(key, localizer.Lookup(key));
        }

        foreach (var key in CategoryDescriptionKeys)
        {
            Assert.NotEqual(key, localizer.Lookup(key));
        }

        using var strings = new LocalizedTextCatalog(localizer);
        Assert.Equal(localizer.T(LocalizationKeys.SettingsCategoryGeneral), strings["settingsCategoryGeneral"]);
    }

    [Theory]
    [InlineData("LauncherStrings.resx")]
    [InlineData("LauncherStrings.zh-Hans.resx")]
    [InlineData("LauncherStrings.zh-Hant.resx")]
    [InlineData("LauncherStrings.ja.resx")]
    public void CategoryLocaleFile_DefinesAllCategoryKeysDirectly(string fileName)
    {
        var resources = TestLocalizationHelper.ReadResx(Path.Combine(ResourceDirectory, fileName));
        Assert.NotNull(resources);

        foreach (var (key, _) in CategoryLocalizedValues)
        {
            Assert.True(resources.TryGetValue(key, out var value));
            Assert.False(string.IsNullOrWhiteSpace(value));
        }

        foreach (var key in CategoryDescriptionKeys)
        {
            Assert.True(resources.TryGetValue(key, out var value));
            Assert.False(string.IsNullOrWhiteSpace(value));
        }
    }

    [Theory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public void LocalizedTextCatalog_MapsAllCategoryKeys(string language)
    {
        var localizer = new LocalizationService();
        localizer.SetLanguage(language);
        using var strings = new LocalizedTextCatalog(localizer);

        foreach (var (key, getValue) in CategoryLocalizedValues)
        {
            Assert.Equal(localizer.Lookup(key), getValue(strings));
        }
    }

    [Fact]
    public void Options_RefreshPreservesCollectionAndRebuildsLocalizedItems()
    {
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.English);
        var options = new SettingsOptionsViewModel(localizer, new DiskSpaceService());
        var collection = options.SettingsCategories;
        options.RefreshDisplayNames();
        var englishItems = collection.ToArray();
        var englishNames = englishItems.Select(item => item.DisplayName).ToArray();

        localizer.SetLanguage(LauncherLanguages.Japanese);
        options.RefreshDisplayNames();

        Assert.Same(collection, options.SettingsCategories);
        Assert.Equal(CategoryCodes, collection.Select(item => item.Code));
        Assert.Equal(englishItems.Length, collection.Count);
        Assert.All(collection, item => Assert.Contains(englishItems, old => ReferenceEquals(old, item)));
        Assert.NotEqual(englishNames, collection.Select(item => item.DisplayName).ToArray());
    }

    [Fact]
    public async Task Selection_ForEveryCategoryHasExactlyOneVisibleCategoryAndRaisesAllNotifications()
    {
        using var scope = CreateSettingsViewModel();
        var viewModel = scope.ViewModel;

        await AssertSelectionNotificationsAsync(viewModel, CategoryCodes);

        viewModel.BeginEdit(gameSpecific: true);
        await AssertSelectionNotificationsAsync(viewModel, GameCategoryCodes);
    }

    [Fact]
    public void BeginEdit_SwitchesActiveCategoriesAndRestoresEachScopeSelection()
    {
        using var scope = CreateSettingsViewModel();
        var viewModel = scope.ViewModel;

        Assert.Same(viewModel.Options.SettingsCategories, viewModel.ActiveCategories);
        viewModel.SelectedCategory = SettingsCategoryCodes.Appearance;

        viewModel.BeginEdit(gameSpecific: true);
        Assert.Same(viewModel.Options.GameSettingsCategories, viewModel.ActiveCategories);
        Assert.Equal(SettingsCategoryCodes.Game, viewModel.SelectedCategory);
        Assert.True(viewModel.Game?.GamePath is not null);

        viewModel.SelectedCategory = SettingsCategoryCodes.GameLaunch;
        viewModel.BeginEdit(gameSpecific: false);
        Assert.Equal(SettingsCategoryCodes.Appearance, viewModel.SelectedCategory);

        viewModel.BeginEdit(gameSpecific: true);
        Assert.Equal(SettingsCategoryCodes.GameLaunch, viewModel.SelectedCategory);
    }

    [Fact]
    public void IsSettingsDirty_TracksOnlyTheActiveScope()
    {
        using var scope = CreateSettingsViewModel();
        var viewModel = scope.ViewModel;

        viewModel.BeginEdit(gameSpecific: false);
        viewModel.Editor.Current.GetGameSettings(
            Cafe.Launcher.Core.Constants.LauncherProfiles.BlueArchiveJapan.RuntimeId).GamePath = @"C:\Games\BlueArchive_JP";
        Assert.False(viewModel.IsSettingsDirty);

        viewModel.BeginEdit(gameSpecific: true);
        Assert.True(viewModel.IsSettingsDirty);

        viewModel.BeginEdit(gameSpecific: false);
        viewModel.Editor.Current.Language = LauncherLanguages.Japanese;
        Assert.True(viewModel.IsSettingsDirty);
    }

    private static async Task AssertSelectionNotificationsAsync(
        SettingsViewModel viewModel,
        IReadOnlyList<string> codes)
    {
        foreach (var code in codes)
        {
            viewModel.SelectedCategory = codes.First(candidate => candidate != code);
            var changed = new List<string>();
            var gameRuntimeChanged = new List<string>();
            PropertyChangedEventHandler handler = (_, args) =>
            {
                if (args.PropertyName is not null)
                {
                    changed.Add(args.PropertyName);
                }
            };
            PropertyChangedEventHandler gameRuntimeHandler = (_, args) =>
            {
                if (args.PropertyName is not null)
                {
                    gameRuntimeChanged.Add(args.PropertyName);
                }
            };
            viewModel.PropertyChanged += handler;
            viewModel.GameRuntime.PropertyChanged += gameRuntimeHandler;

            viewModel.SelectedCategory = code;
            if (code == SettingsCategoryCodes.Game)
            {
                // 选中 Game 会异步刷新状态行；等它完成再收口，避免通知落在下一次迭代里。
                await viewModel.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));
            }

            viewModel.PropertyChanged -= handler;
            viewModel.GameRuntime.PropertyChanged -= gameRuntimeHandler;
            var changedProperties = changed.ToHashSet(StringComparer.Ordinal);
            // Selecting the Game category refreshes the runtime status row inside the
            // child VM; the parent raises the selection notification and all scope flags,
            // including the two derived background-editor flags. The default-background
            // category additionally raises the preview flag while starting its preview.
            var expectedCount = code == SettingsCategoryCodes.DefaultBackground ? 16 : 15;
            Assert.Equal(expectedCount, changedProperties.Count);
            Assert.Contains(nameof(SettingsViewModel.SelectedCategory), changedProperties);
            Assert.Contains(nameof(SettingsViewModel.IsBackgroundEditorCategorySelected), changedProperties);
            Assert.Contains(nameof(SettingsViewModel.CanPreviewAppearance), changedProperties);
            if (code == SettingsCategoryCodes.Game)
            {
                var runtimeChanged = gameRuntimeChanged.ToHashSet(StringComparer.Ordinal);
                Assert.Contains(nameof(SettingsGameRuntimeViewModel.StatusSummary), runtimeChanged);
                Assert.Contains(nameof(SettingsGameRuntimeViewModel.IsStatusRefreshing), runtimeChanged);
            }
            foreach (var propertyName in CategoryPropertyNames)
            {
                Assert.Contains(propertyName, changedProperties);
            }
            Assert.Single(
                new[]
                {
                    viewModel.IsGeneralCategorySelected,
                    viewModel.IsGameCategorySelected,
                    viewModel.IsGameLaunchCategorySelected,
                    viewModel.IsGameOptionsCategorySelected,
                    viewModel.IsGameDownloadCategorySelected,
                    viewModel.IsGameBackgroundCategorySelected,
                    viewModel.IsDownloadNetworkCategorySelected,
                    viewModel.IsDefaultBackgroundCategorySelected,
                    viewModel.IsAppearanceCategorySelected,
                    viewModel.IsAdvancedCategorySelected,
                    viewModel.IsAboutCategorySelected
                },
                isSelected => isSelected);
        }
    }

    [Fact]
    public async Task Discard_RestoresDraftWithoutResettingSelection()
    {
        using var provider = new ServiceCollection().AddLauncherServices().BuildServiceProvider();
        var viewModel = provider.GetRequiredService<SettingsViewModel>();
        viewModel.LoadFromSnapshot(new LauncherSettings { Language = LauncherLanguages.English });
        viewModel.Editor.Current.Language = LauncherLanguages.Japanese;
        viewModel.SelectedCategory = SettingsCategoryCodes.DownloadNetwork;

        await viewModel.DiscardChangesAsync();

        Assert.Equal(LauncherLanguages.English, viewModel.Editor.Current.Language);
        Assert.Equal(SettingsCategoryCodes.DownloadNetwork, viewModel.SelectedCategory);
    }

    [Fact]
    public void Selection_PreservesCurrentDraftAndDirtyStateExactly()
    {
        using var scope = CreateSettingsViewModel();
        var viewModel = scope.ViewModel;
        viewModel.LoadFromSnapshot(new LauncherSettings
        {
            Language = LauncherLanguages.English,
            ProxyMode = ProxyModes.Direct
        });
        var current = viewModel.Editor.Current;
        current.Language = LauncherLanguages.Japanese;
        current.ProxyMode = ProxyModes.System;
        var wasDirty = viewModel.Editor.IsDirty;

        viewModel.SelectedCategory = SettingsCategoryCodes.DownloadNetwork;

        Assert.Same(current, viewModel.Editor.Current);
        Assert.Equal(LauncherLanguages.Japanese, viewModel.Editor.Current.Language);
        Assert.Equal(ProxyModes.System, viewModel.Editor.Current.ProxyMode);
        Assert.Equal(wasDirty, viewModel.Editor.IsDirty);
    }

    [Fact]
    public async Task Selection_DoesNotSaveOrChangeSettingsFile()
    {
        using var scope = CreateSettingsViewModel();
        const string original = "{\"language\":\"en\"}";
        Directory.CreateDirectory(Path.GetDirectoryName(scope.SettingsPath)!);
        await File.WriteAllTextAsync(scope.SettingsPath, original);
        var followThrough = new RecordingFollowThrough();
        scope.ViewModel.SavedSettingsFollowThrough = followThrough;

        scope.ViewModel.SelectedCategory = SettingsCategoryCodes.About;

        Assert.Equal(0, followThrough.ApplyCount);
        Assert.Equal(0, followThrough.CompleteCount);
        Assert.Equal(original, await File.ReadAllTextAsync(scope.SettingsPath));
    }

    [Fact]
    public async Task SaveSettingsCommand_PreservesSelection()
    {
        using var scope = CreateSettingsViewModel();
        scope.ViewModel.LoadFromSnapshot(new LauncherSettings { Language = LauncherLanguages.English });
        scope.ViewModel.SelectedCategory = SettingsCategoryCodes.Appearance;
        scope.ViewModel.Editor.Current.Language = LauncherLanguages.Japanese;

        await scope.ViewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal(SettingsCategoryCodes.Appearance, scope.ViewModel.SelectedCategory);
        Assert.True(File.Exists(scope.SettingsPath));
    }

    [Fact]
    public async Task SaveSettingsCommand_EndsSavingStateAtPersistWhileFollowThroughRuns()
    {
        using var scope = CreateSettingsViewModel();
        scope.ViewModel.LoadFromSnapshot(new LauncherSettings { Language = LauncherLanguages.English });
        var completionStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completionRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        scope.ViewModel.SavedSettingsFollowThrough = new RecordingFollowThrough
        {
            OnComplete = async () =>
            {
                completionStarted.SetResult();
                await completionRelease.Task;
            }
        };
        scope.ViewModel.Editor.Current.Language = LauncherLanguages.Japanese;
        Assert.True(scope.ViewModel.IsSettingsDirty);

        var saveTask = scope.ViewModel.SaveSettingsCommand.ExecuteAsync(null);
        await completionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 落盘已完成、跟随刷新仍在进行：保存态必须以落盘为界结束，
        // 否则页脚、导航、关闭与编辑会被整段壳刷新继续锁住（AUD-SET-003）。
        Assert.False(scope.ViewModel.IsSaving);
        Assert.False(scope.ViewModel.IsSettingsDirty);
        Assert.False(saveTask.IsCompleted);
        Assert.True(File.Exists(scope.SettingsPath));

        completionRelease.SetResult();
        await saveTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(scope.ViewModel.IsSaving);
    }

    [Fact]
    public async Task SaveSettingsCommand_WhenFollowThroughFails_DoesNotReportSaveFailure()
    {
        using var scope = CreateSettingsViewModel();
        scope.ViewModel.LoadFromSnapshot(new LauncherSettings { Language = LauncherLanguages.English });
        var toasts = new List<ToastNotification>();
        scope.Provider.GetRequiredService<ToastService>().ToastRaised += toasts.Add;
        scope.ViewModel.SavedSettingsFollowThrough = new RecordingFollowThrough
        {
            OnComplete = () => Task.FromException(new InvalidOperationException("follow-through failed"))
        };
        scope.ViewModel.Editor.Current.Language = LauncherLanguages.Japanese;

        await scope.ViewModel.SaveSettingsCommand.ExecuteAsync(null);

        // 值已落盘：跟随动作失败不能回退成「保存失败」，也不能重新弄脏草稿。
        Assert.False(scope.ViewModel.IsSaving);
        Assert.False(scope.ViewModel.IsSettingsDirty);
        Assert.True(File.Exists(scope.SettingsPath));
        var notification = Assert.Single(toasts);
        Assert.NotEqual(ToastSeverity.Error, notification.Severity);
    }

    [Fact]
    public async Task SaveSettingsCommand_AwaitsFollowThroughCompletion()
    {
        using var scope = CreateSettingsViewModel();
        var completionStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completionRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        scope.ViewModel.SavedSettingsFollowThrough = new RecordingFollowThrough
        {
            OnComplete = async () =>
            {
                completionStarted.SetResult();
                await completionRelease.Task;
            }
        };

        var saveTask = scope.ViewModel.SaveSettingsCommand.ExecuteAsync(null);
        await completionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 保存命令要等后续效果收尾（含刷新）完成才结束，否则调用方会在壳状态
        // 尚未落定时就继续执行。
        Assert.False(saveTask.IsCompleted);
        completionRelease.SetResult();
        await saveTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SaveSettingsCommand_GameScope_WritesOnlyTheGamePartition()
    {
        using var scope = CreateSettingsViewModel();
        var viewModel = scope.ViewModel;
        var service = scope.Provider.GetRequiredService<ILauncherSettingsService>();
        var gameId = Cafe.Launcher.Core.Constants.LauncherProfiles.BlueArchiveJapan.RuntimeId;
        viewModel.LoadFromSnapshot(new LauncherSettings { Language = LauncherLanguages.English });
        viewModel.BeginEdit(gameSpecific: true);

        viewModel.Game.GamePath = @"C:\Games\BlueArchive_JP";
        viewModel.Game.GameRuntime.EnableMangoHud = true;
        viewModel.Editor.Current.Language = LauncherLanguages.Japanese;

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        var saved = await service.ReadAsync();
        Assert.Equal(LauncherLanguages.English, saved.Language);
        Assert.Equal(@"C:\Games\BlueArchive_JP", saved.GetGameSettings(gameId).GamePath);
        Assert.True(saved.GetGameSettings(gameId).GameRuntime.EnableMangoHud);
        Assert.False(viewModel.IsSettingsDirty);
    }

    [Fact]
    public async Task SaveSettingsCommand_GlobalScope_DoesNotWriteGamePartitionChanges()
    {
        using var scope = CreateSettingsViewModel();
        var viewModel = scope.ViewModel;
        var service = scope.Provider.GetRequiredService<ILauncherSettingsService>();
        var gameId = Cafe.Launcher.Core.Constants.LauncherProfiles.BlueArchiveJapan.RuntimeId;
        viewModel.LoadFromSnapshot(new LauncherSettings { Language = LauncherLanguages.English });
        viewModel.Game.GamePath = @"C:\Games\BlueArchive_JP";

        viewModel.BeginEdit(gameSpecific: false);
        viewModel.Editor.Current.Language = LauncherLanguages.Japanese;
        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        var saved = await service.ReadAsync();
        Assert.Equal(LauncherLanguages.Japanese, saved.Language);
        Assert.Empty(saved.GetGameSettings(gameId).GamePath);
    }

    private static SettingsViewModelScope CreateSettingsViewModel()
    {
        var directory = TestDirectory.Create();
        var settingsPath = Path.Combine(directory, "settings.json");
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.Replace(ServiceDescriptor.Singleton(new LauncherSettingsService( TestDataRoot.ForFile(settingsPath) )));
        var provider = services.BuildServiceProvider();
        return new SettingsViewModelScope(
            provider,
            provider.GetRequiredService<SettingsViewModel>(),
            settingsPath,
            directory);
    }

    private sealed class RecordingFollowThrough : ISavedSettingsFollowThrough
    {
        public Func<Task>? OnComplete { get; init; }

        public int ApplyCount { get; private set; }

        public int CompleteCount { get; private set; }

        public SavedSettingsHandoff Apply(LauncherSettings savedSettings)
        {
            ApplyCount++;
            return new SavedSettingsHandoff(savedSettings, null);
        }

        public Task CompleteAsync(SavedSettingsHandoff handoff)
        {
            CompleteCount++;
            return OnComplete?.Invoke() ?? Task.CompletedTask;
        }
    }

    private sealed class SettingsViewModelScope(
        ServiceProvider provider,
        SettingsViewModel viewModel,
        string settingsPath,
        TestDirectory directory) : IDisposable
    {
        public ServiceProvider Provider { get; } = provider;
        public SettingsViewModel ViewModel { get; } = viewModel;
        public string SettingsPath { get; } = settingsPath;

        public void Dispose()
        {
            Provider.Dispose();
            directory.Dispose();
        }
    }
}
