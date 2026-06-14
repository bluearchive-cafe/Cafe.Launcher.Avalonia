using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Avalonia.Media;
using System.Reflection;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly LauncherApiClient apiClient = new();
    private readonly ImageCacheService imageCacheService = new();

    public MainWindowViewModelTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public void Constructor_DoesNotLoadLauncherState()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService);

        Assert.Equal(0, coreService.LoadCount);
    }

    [Fact]
    public async Task InitializeAsync_WhenCalledTwice_LoadsLauncherStateOnce()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService);

        await viewModel.InitializeAsync();
        await viewModel.InitializeAsync();

        Assert.Equal(1, coreService.LoadCount);
    }

    [Fact]
    public async Task InitializeAsync_WhenNewsAndNoticesExist_AddsBothToNewsItems()
    {
        var snapshot = CreateSnapshot();
        snapshot.Remote.OperationsResource = CreateOperationsResource();
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = CreateViewModel(coreService);

        await viewModel.InitializeAsync();

        Assert.Contains(viewModel.NewsItems, item => item.Title == "news title");
        Assert.Contains(viewModel.NewsItems, item => item.Title == "notice title");
    }

    [Fact]
    public async Task InitializeAsync_WhenShowRemoteContentCardIsFalse_HidesRemoteContentCard()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.ShowRemoteContentCard = false;
        snapshot.Remote.OperationsResource = CreateOperationsResource();
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = CreateViewModel(coreService);

        await viewModel.InitializeAsync();

        Assert.Contains(viewModel.NewsItems, item => item.Title == "news title");
        Assert.Contains(viewModel.NewsItems, item => item.Title == "notice title");
        Assert.True(viewModel.HasNewsItems);
        Assert.False(viewModel.HasRemoteContent);
    }

    [Fact]
    public async Task ResolveRandomBackgroundImage_WhenFolderHasSupportedImage_ReturnsImageFromFolder()
    {
        var folderPath = Path.Combine(tempDir, "wallpapers");
        Directory.CreateDirectory(folderPath);
        var imagePath = Path.Combine(folderPath, "wallpaper.PNG");
        await WriteTestPngAsync(imagePath);

        var resolved = MainWindowViewModel.ResolveRandomBackgroundImage(folderPath);

        Assert.Equal(imagePath, resolved);
    }

    [Fact]
    public async Task ResolveRandomBackgroundImage_WhenOnlySubfolderHasImage_ReturnsNull()
    {
        var folderPath = Path.Combine(tempDir, "wallpapers");
        var nestedFolderPath = Path.Combine(folderPath, "nested");
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(nestedFolderPath);
        await WriteTestPngAsync(Path.Combine(nestedFolderPath, "wallpaper.png"));

        var resolved = MainWindowViewModel.ResolveRandomBackgroundImage(folderPath);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveRandomBackgroundImage_WhenFolderHasNoSupportedImage_ReturnsNull()
    {
        var folderPath = Path.Combine(tempDir, "empty-wallpapers");
        Directory.CreateDirectory(folderPath);

        var resolved = MainWindowViewModel.ResolveRandomBackgroundImage(folderPath);

        Assert.Null(resolved);
    }

    [Fact]
    public void ApplyProgress_WhenProgressCannotPause_HidesPauseResume()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService);

        ApplyProgress(viewModel, new GameOperationProgress
        {
            OperationKind = GameOperationKinds.Uninstall,
            Stage = "uninstall",
            Progress = 50,
            CanPause = false
        });

        Assert.False(viewModel.CanPauseOperation);
    }

    [Fact]
    public void ApplyProgress_WhenProgressCanPause_ShowsPauseResume()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService);

        ApplyProgress(viewModel, new GameOperationProgress
        {
            OperationKind = GameOperationKinds.Download,
            Stage = "download",
            Progress = 50,
            CanPause = true
        });

        Assert.True(viewModel.CanPauseOperation);
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenPatchUrlGroupChangesForInstalledGame_ShowsRepairPrompt()
    {
        var snapshot = CreateSnapshot();
        snapshot.IsInstalled = true;
        snapshot.Settings.PatchUrlGroup = PatchUrlGroups.Official;
        snapshot.LocalGame.GameConfig = new GameLauncherConfig
        {
            Name = "BlueArchive",
            Version = "1.0.0"
        };
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings
        {
            GamePath = snapshot.Settings.GamePath,
            PatchUrlGroup = PatchUrlGroups.Official
        });
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = CreateViewModel(coreService, settingsService);
        await viewModel.InitializeAsync();

        viewModel.SelectedPatchUrlGroup = PatchUrlGroups.Cafe;
        await SaveSettingsAsync(viewModel);

        Assert.True(viewModel.IsRepairConfirmVisible);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.RepairConfirmText));
    }

    [Fact]
    public void SelectedThemeColorMode_WhenSettingsVisible_MarksSettingsDirty()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService);
        viewModel.IsSettingsVisible = true;
        viewModel.IsSettingsDirty = false;

        viewModel.SelectedThemeColorMode = ThemeColorModes.System;

        Assert.True(viewModel.IsSettingsDirty);
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenCustomThemeColorSelected_PersistsColor()
    {
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings());
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService, settingsService);

        viewModel.SelectedThemeColorMode = ThemeColorModes.Custom;
        viewModel.SelectedCustomThemeColor = Color.FromArgb(0xFF, 0x33, 0x66, 0x99);
        await SaveSettingsAsync(viewModel);

        var settings = await settingsService.ReadAsync();
        Assert.Equal(ThemeColorModes.Custom, settings.ThemeColorMode);
        Assert.Equal("#FF336699", settings.CustomThemeColor);
    }

    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenBackgroundHasMultipleColors_ReturnsAtMostFiveColors()
    {
        var buffer = CreateStripedBgraBuffer(
            12,
            6,
            [
                Color.FromRgb(0xD8, 0x20, 0x38),
                Color.FromRgb(0x20, 0x90, 0x40),
                Color.FromRgb(0x30, 0x50, 0xD8),
                Color.FromRgb(0xE0, 0xA0, 0x20),
                Color.FromRgb(0x90, 0x30, 0xB8),
                Color.FromRgb(0x20, 0xB8, 0xD8)
            ]);

        var palette = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(buffer, 12, 6, 12 * 4);

        Assert.InRange(palette.Count, 1, 5);
    }

    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenSaturatedAndGrayExist_PrioritizesSaturatedColor()
    {
        var buffer = CreateStripedBgraBuffer(
            8,
            4,
            [
                Color.FromRgb(0x80, 0x80, 0x80),
                Color.FromRgb(0xD8, 0x20, 0x38)
            ]);

        var palette = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(buffer, 8, 4, 8 * 4);

        Assert.NotEmpty(palette);
        Assert.True(palette[0].R > palette[0].G);
        Assert.True(palette[0].R > palette[0].B);
    }

    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenBackgroundHasNoUsableColor_ReturnsEmpty()
    {
        var buffer = CreateSolidBgraBuffer(0x80, 0x80, 0x80, 8, 8, 0xFF);

        var palette = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(buffer, 8, 8, 8 * 4);

        Assert.Empty(palette);
    }

    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenBackgroundIsTransparent_ReturnsEmpty()
    {
        var buffer = CreateSolidBgraBuffer(0xD8, 0x20, 0x38, 8, 8, 0x00);

        var palette = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(buffer, 8, 8, 8 * 4);

        Assert.Empty(palette);
    }

    [Fact]
    public void SelectedThemeColorPaletteIndex_WhenSettingsVisible_MarksSettingsDirtyAndUpdatesSelection()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService);
        viewModel.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 0,
            ColorHex = "#FFD82038",
            Brush = new SolidColorBrush(Color.FromRgb(0xD8, 0x20, 0x38))
        });
        viewModel.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 1,
            ColorHex = "#FF2050D8",
            Brush = new SolidColorBrush(Color.FromRgb(0x20, 0x50, 0xD8))
        });
        viewModel.SelectedThemeColorMode = ThemeColorModes.Wallpaper;
        viewModel.IsSettingsVisible = true;
        viewModel.IsSettingsDirty = false;

        viewModel.SelectedThemeColorPaletteIndex = 1;

        Assert.True(viewModel.IsSettingsDirty);
        Assert.False(viewModel.ThemeColorPaletteItems[0].IsSelected);
        Assert.True(viewModel.ThemeColorPaletteItems[1].IsSelected);
        var preview = Assert.IsType<SolidColorBrush>(viewModel.ThemeColorPreviewBrush);
        Assert.Equal(Color.FromArgb(0xFF, 0x20, 0x50, 0xD8), preview.Color);
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenWallpaperPaletteSelected_PersistsPaletteAndIndex()
    {
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings());
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService, settingsService);
        viewModel.SelectedThemeColorMode = ThemeColorModes.Wallpaper;
        viewModel.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 0,
            ColorHex = "#FFD82038",
            Brush = new SolidColorBrush(Color.FromRgb(0xD8, 0x20, 0x38))
        });
        viewModel.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 1,
            ColorHex = "#FF2050D8",
            Brush = new SolidColorBrush(Color.FromRgb(0x20, 0x50, 0xD8))
        });
        viewModel.SelectedThemeColorPaletteIndex = 1;

        await SaveSettingsAsync(viewModel);

        var settings = await settingsService.ReadAsync();
        Assert.Equal(ThemeColorModes.Wallpaper, settings.ThemeColorMode);
        Assert.Equal(["#FFD82038", "#FF2050D8"], settings.ThemeColorPalette);
        Assert.Equal(1, settings.SelectedThemeColorPaletteIndex);
    }

    private MainWindowViewModel CreateViewModel(
        ILauncherCoreService coreService,
        LauncherSettingsService? settingsService = null)
    {
        settingsService ??= new LauncherSettingsService(
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var localGameStateService = new LocalGameStateService();
        var diagnostics = new LocalDiagnostics();
        var manifestValidationService = new ManifestValidationService(apiClient);
        var gameLaunchService = new GameLaunchService(manifestValidationService, new ClickCodeService());
        var downloadStateService = new DownloadStateService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "download_state.json"));
        var gameDownloadService = new GameDownloadService(
            apiClient,
            localGameStateService,
            settingsService,
            new ProxySettingsService(),
            new Crc64Service(),
            new DiskSpaceService(),
            diagnostics,
            downloadStateService);

        return new MainWindowViewModel(
            coreService,
            settingsService,
            localGameStateService,
            gameLaunchService,
            gameDownloadService,
            new GameUninstallService(localGameStateService, diagnostics),
            new ExternalLinkService(),
            new DiskSpaceService(),
            new LocalizationService(),
            new ToastService(),
            diagnostics,
            new NoticeStateService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "shown_notices.json")),
            imageCacheService);
    }

    private LauncherStatusSnapshot CreateSnapshot()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        return new LauncherStatusSnapshot
        {
            Settings = new LauncherSettings
            {
                GamePath = gamePath
            },
            LocalGame = new LocalGameState
            {
                GamePath = gamePath
            },
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse
                {
                    GameLatestVersion = "1.0.0",
                    GameStartExeName = "BlueArchive"
                }
            },
            CheckedAt = DateTimeOffset.Now
        };
    }

    private static OperationsResourceResponse CreateOperationsResource()
    {
        return new OperationsResourceResponse
        {
            OperationsResourceOpen = true,
            NewsList = new NewsListEnvelope
            {
                Code = 0,
                Data = new NewsListData
                {
                    News =
                    [
                        new NewsTypeItem
                        {
                            TypeLabel = "news",
                            Rows =
                            [
                                new NewsRowItem
                                {
                                    Title = "news title",
                                    PublishTime = 0,
                                    Link = "https://example.invalid/news"
                                }
                            ]
                        }
                    ]
                }
            },
            NoticeList =
            [
                new NoticeTypeItem
                {
                    NoticeType = "notice",
                    NoticeDetailList =
                    [
                        new NoticeDetailItem
                        {
                            NoticeTitle = "notice title",
                            NoticeTime = "2026-06-12",
                            JumpUrl = "https://example.invalid/notice"
                        }
                    ]
                }
            ]
        };
    }

    private static Task WriteTestPngAsync(string path)
    {
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        return File.WriteAllBytesAsync(path, bytes);
    }

    private static void ApplyProgress(MainWindowViewModel viewModel, GameOperationProgress progress)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyProgress",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, [progress]);
    }

    private static byte[] CreateSolidBgraBuffer(byte r, byte g, byte b, int width, int height, byte alpha)
    {
        var rowBytes = width * 4;
        var buffer = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + (x * 4);
                buffer[offset] = b;
                buffer[offset + 1] = g;
                buffer[offset + 2] = r;
                buffer[offset + 3] = alpha;
            }
        }

        return buffer;
    }

    private static byte[] CreateStripedBgraBuffer(int width, int height, IReadOnlyList<Color> colors)
    {
        var rowBytes = width * 4;
        var buffer = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var color = colors[Math.Min(colors.Count - 1, x * colors.Count / width)];
                var offset = rowOffset + (x * 4);
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = color.A;
            }
        }

        return buffer;
    }

    private static async Task SaveSettingsAsync(MainWindowViewModel viewModel)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            "SaveSettingsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = (Task?)method.Invoke(viewModel, []);
        Assert.NotNull(task);
        await task;
    }

    public void Dispose()
    {
        imageCacheService.Dispose();
        apiClient.Dispose();
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class CountingCoreService : ILauncherCoreService
    {
        private readonly LauncherStatusSnapshot snapshot;

        public CountingCoreService(LauncherStatusSnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        public int LoadCount { get; private set; }

        public Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(snapshot);
        }
    }
}
