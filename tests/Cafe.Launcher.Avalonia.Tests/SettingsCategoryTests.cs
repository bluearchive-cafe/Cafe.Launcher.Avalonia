using System.ComponentModel;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cafe.Launcher.Avalonia.Tests;

[Collection("Settings category localization")]
public sealed class SettingsCategoryTests
{
    private static readonly string ResourceDirectory = Path.Combine(
        TestLocalizationHelper.FindProjectRoot(), "Resources");

    private static readonly string[] CategoryCodes =
    [
        SettingsCategoryCodes.General,
        SettingsCategoryCodes.Game,
        SettingsCategoryCodes.DownloadNetwork,
        SettingsCategoryCodes.Appearance,
        SettingsCategoryCodes.Advanced,
        SettingsCategoryCodes.About
    ];

    private static readonly (string Key, Func<LocalizedTextCatalog, string> GetValue)[] CategoryLocalizedValues =
    [
        ("settingsCategoryGeneral", strings => strings["settingsCategoryGeneral"]),
        ("settingsCategoryGame", strings => strings["settingsCategoryGame"]),
        ("settingsCategoryDownloadNetwork", strings => strings["settingsCategoryDownloadNetwork"]),
        ("settingsCategoryAppearance", strings => strings["settingsCategoryAppearance"]),
        ("settingsCategoryAdvanced", strings => strings["settingsCategoryAdvanced"]),
        ("settingsCategoryAbout", strings => strings["settingsCategoryAbout"])
    ];

    private static readonly string[] CategoryDescriptionKeys =
    [
        "settingsCategoryGeneralDescription",
        "settingsCategoryGameDescription",
        "settingsCategoryDownloadNetworkDescription",
        "settingsCategoryAppearanceDescription",
        "settingsCategoryAdvancedDescription",
        "settingsCategoryAboutDescription"
    ];

    private static readonly string[] CategoryPropertyNames =
    [
        nameof(SettingsViewModel.IsGeneralCategorySelected),
        nameof(SettingsViewModel.IsGameCategorySelected),
        nameof(SettingsViewModel.IsDownloadNetworkCategorySelected),
        nameof(SettingsViewModel.IsAppearanceCategorySelected),
        nameof(SettingsViewModel.IsAdvancedCategorySelected),
        nameof(SettingsViewModel.IsAboutCategorySelected)
    ];

    static SettingsCategoryTests() => TestLocalizationHelper.Initialize();

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
    [InlineData("download-network")]
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
            [SettingsCategoryCodes.General, SettingsCategoryCodes.Game,
             SettingsCategoryCodes.DownloadNetwork, SettingsCategoryCodes.Appearance,
             SettingsCategoryCodes.Advanced,
             SettingsCategoryCodes.About],
            options.SettingsCategories.Select(option => option.Code));
        Assert.All(options.SettingsCategories, option => Assert.False(string.IsNullOrWhiteSpace(option.DisplayName)));
    }

    [Fact]
    public void CategoryIcons_UseOutlinedAndFilledPairs()
    {
        var options = new SettingsOptionsViewModel(new LocalizationService(), new DiskSpaceService());
        options.RefreshDisplayNames();

        Assert.Equal(
            [
                ("CogOutline", "Cog"),
                ("GamepadSquareOutline", "GamepadSquare"),
                ("DownloadOutline", "Download"),
                ("PaletteOutline", "Palette"),
                ("Tune", "Tune"),
                ("InformationOutline", "Information")
            ],
            options.SettingsCategories.Select(option => (option.IconKind, option.SelectedIconKind)));
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
            Assert.NotEqual(key, localizer.T(key));
        }

        foreach (var key in CategoryDescriptionKeys)
        {
            Assert.NotEqual(key, localizer.T(key));
        }

        using var strings = new LocalizedTextCatalog(localizer);
        Assert.Equal(localizer.T("settingsCategoryGeneral"), strings["settingsCategoryGeneral"]);
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
    public void LocalizedTextCatalog_MapsAllSixCategoryKeys(string language)
    {
        var localizer = new LocalizationService();
        localizer.SetLanguage(language);
        using var strings = new LocalizedTextCatalog(localizer);

        foreach (var (key, getValue) in CategoryLocalizedValues)
        {
            Assert.Equal(localizer.T(key), getValue(strings));
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
    public void Selection_ForEveryCategoryHasExactlyOneVisibleCategoryAndRaisesAllNotifications()
    {
        using var scope = CreateSettingsViewModel();
        var viewModel = scope.ViewModel;

        foreach (var code in CategoryCodes)
        {
            viewModel.SelectedCategory = code == SettingsCategoryCodes.General
                ? SettingsCategoryCodes.About
                : SettingsCategoryCodes.General;
            var changed = new List<string>();
            PropertyChangedEventHandler handler = (_, args) =>
            {
                if (args.PropertyName is not null)
                {
                    changed.Add(args.PropertyName);
                }
            };
            viewModel.PropertyChanged += handler;

            viewModel.SelectedCategory = code;

            viewModel.PropertyChanged -= handler;
            var changedProperties = changed.ToHashSet(StringComparer.Ordinal);
            Assert.Equal(7, changedProperties.Count);
            Assert.Contains(nameof(SettingsViewModel.SelectedCategory), changedProperties);
            foreach (var propertyName in CategoryPropertyNames)
            {
                Assert.Contains(propertyName, changedProperties);
            }
            Assert.Single(
                new[]
                {
                    viewModel.IsGeneralCategorySelected,
                    viewModel.IsGameCategorySelected,
                    viewModel.IsDownloadNetworkCategorySelected,
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
        var savedCount = 0;
        scope.ViewModel.SettingsSaved += () =>
        {
            savedCount++;
            return Task.CompletedTask;
        };

        scope.ViewModel.SelectedCategory = SettingsCategoryCodes.About;

        Assert.Equal(0, savedCount);
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
    public async Task SaveSettingsCommand_WithMultipleAsyncSubscribers_AwaitsEverySubscriber()
    {
        using var scope = CreateSettingsViewModel();
        var firstSubscriberInvoked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSubscriberRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSubscriberInvoked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        scope.ViewModel.SettingsSaved += async () =>
        {
            firstSubscriberInvoked.SetResult();
            await firstSubscriberRelease.Task;
        };
        scope.ViewModel.SettingsSaved += () =>
        {
            secondSubscriberInvoked.SetResult();
            return Task.CompletedTask;
        };

        var saveTask = scope.ViewModel.SaveSettingsCommand.ExecuteAsync(null);
        await firstSubscriberInvoked.Task;

        Assert.False(saveTask.IsCompleted);
        Assert.False(secondSubscriberInvoked.Task.IsCompleted);
        firstSubscriberRelease.SetResult();
        await secondSubscriberInvoked.Task;
        await saveTask;
    }

    private static SettingsViewModelScope CreateSettingsViewModel()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "settings.json");
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.Replace(ServiceDescriptor.Singleton(new LauncherSettingsService(settingsPath)));
        var provider = services.BuildServiceProvider();
        return new SettingsViewModelScope(
            provider,
            provider.GetRequiredService<SettingsViewModel>(),
            settingsPath,
            directory);
    }

    private sealed class SettingsViewModelScope(
        ServiceProvider provider,
        SettingsViewModel viewModel,
        string settingsPath,
        string directory) : IDisposable
    {
        public SettingsViewModel ViewModel { get; } = viewModel;
        public string SettingsPath { get; } = settingsPath;

        public void Dispose()
        {
            provider.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

[CollectionDefinition("Settings category localization", DisableParallelization = true)]
public sealed class SettingsCategoryLocalizationGroup;
