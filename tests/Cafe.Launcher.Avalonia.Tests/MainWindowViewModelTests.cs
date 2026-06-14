using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Avalonia.Media;
using System.Net;
using System.Text;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly ProxySettingsService proxySettings = new();
    private readonly HttpClientFactory httpClientFactory;
    private readonly LauncherApiClient apiClient = new(
        new AuthorizationHeaderFactory(),
        new PatchUrlGroupService(),
        new ProxySettingsService());
    private readonly ImageCacheService imageCacheService;

    public MainWindowViewModelTests()
    {
        Directory.CreateDirectory(tempDir);
        httpClientFactory = new HttpClientFactory(proxySettings);
        imageCacheService = new ImageCacheService(httpClientFactory, new Crc64Service());
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

        Assert.Contains(viewModel.RemoteContent.NewsItems, item => item.Title == "news title");
        Assert.Contains(viewModel.RemoteContent.NewsItems, item => item.Title == "notice title");
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

        Assert.Contains(viewModel.RemoteContent.NewsItems, item => item.Title == "news title");
        Assert.Contains(viewModel.RemoteContent.NewsItems, item => item.Title == "notice title");
        Assert.True(viewModel.RemoteContent.HasNewsItems);
        Assert.False(viewModel.RemoteContent.HasRemoteContent);
    }

    [Fact]
    public async Task InitializeAsync_WhenCoreLoadFails_DoesNotShowNetworkLoadedToast()
    {
        var coreService = new ThrowingCoreService();
        var successToasts = new List<string>();
        var toastService = new ToastService();
        using var viewModel = CreateViewModel(coreService, toastService: toastService);
        toastService.ToastRaised += notification =>
        {
            if (notification.Severity == ToastSeverity.Success)
            {
                successToasts.Add(notification.Message);
            }
        };

        await viewModel.InitializeAsync();

        Assert.DoesNotContain(viewModel.Shell.I18n.StatusNetworkLoaded, successToasts);
    }

    [Fact]
    public async Task InitializeAsync_WhenSocialChannelIsPixiv_UsesPaletteIcon()
    {
        var snapshot = CreateSnapshot();
        snapshot.Remote.SocialMediaResource = new SocialMediaResourceResponse
        {
            SocialMediaResourceOpen = true,
            SocialMediaResourceList =
            [
                new SocialMediaResourceItem
                {
                    SocialMediaChannel = "pixiv",
                    JumpUrl = "https://example.invalid/pixiv"
                }
            ]
        };
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = CreateViewModel(coreService);

        await viewModel.InitializeAsync();

        var item = Assert.Single(viewModel.RemoteContent.SocialMediaItems);
        Assert.Equal("Palette", item.SocialIconKind);
    }

    [Fact]
    public async Task ResolveRandomBackgroundImage_WhenFolderHasSupportedImage_ReturnsImageFromFolder()
    {
        var folderPath = Path.Combine(tempDir, "wallpapers");
        Directory.CreateDirectory(folderPath);
        var imagePath = Path.Combine(folderPath, "wallpaper.PNG");
        await WriteTestPngAsync(imagePath);

        var resolved = BackgroundViewModel.ResolveRandomBackgroundImage(folderPath);

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

        var resolved = BackgroundViewModel.ResolveRandomBackgroundImage(folderPath);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveRandomBackgroundImage_WhenFolderHasNoSupportedImage_ReturnsNull()
    {
        var folderPath = Path.Combine(tempDir, "empty-wallpapers");
        Directory.CreateDirectory(folderPath);

        var resolved = BackgroundViewModel.ResolveRandomBackgroundImage(folderPath);

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

        Assert.False(viewModel.Operations.CanPauseOperation);
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

        Assert.True(viewModel.Operations.CanPauseOperation);
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

        viewModel.Settings.SelectedPatchUrlGroup = PatchUrlGroups.Cafe;
        await SaveSettingsAsync(viewModel);

        Assert.True(viewModel.Dialogs.IsRepairConfirmVisible);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Dialogs.RepairConfirmText));
    }

    [Fact]
    public void SelectedThemeColorMode_WhenSettingsVisible_MarksSettingsDirty()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService);
        viewModel.WindowChrome.IsSettingsVisible = true;
        viewModel.Settings.IsSettingsDirty = false;

        viewModel.Settings.SelectedThemeColorMode = ThemeColorModes.System;

        Assert.True(viewModel.Settings.IsSettingsDirty);
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenCustomThemeColorSelected_PersistsColor()
    {
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings());
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService, settingsService);

        viewModel.Settings.SelectedThemeColorMode = ThemeColorModes.Custom;
        viewModel.Settings.SelectedCustomThemeColor = Color.FromArgb(0xFF, 0x33, 0x66, 0x99);
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
        viewModel.Settings.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 0,
            ColorHex = "#FFD82038",
            Brush = new SolidColorBrush(Color.FromRgb(0xD8, 0x20, 0x38))
        });
        viewModel.Settings.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 1,
            ColorHex = "#FF2050D8",
            Brush = new SolidColorBrush(Color.FromRgb(0x20, 0x50, 0xD8))
        });
        viewModel.Settings.SelectedThemeColorMode = ThemeColorModes.Wallpaper;
        viewModel.WindowChrome.IsSettingsVisible = true;
        viewModel.Settings.IsSettingsDirty = false;

        viewModel.Settings.SelectedThemeColorPaletteIndex = 1;

        Assert.True(viewModel.Settings.IsSettingsDirty);
        Assert.False(viewModel.Settings.ThemeColorPaletteItems[0].IsSelected);
        Assert.True(viewModel.Settings.ThemeColorPaletteItems[1].IsSelected);
        var preview = Assert.IsType<SolidColorBrush>(viewModel.Settings.ThemeColorPreviewBrush);
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
        viewModel.Settings.SelectedThemeColorMode = ThemeColorModes.Wallpaper;
        viewModel.Settings.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 0,
            ColorHex = "#FFD82038",
            Brush = new SolidColorBrush(Color.FromRgb(0xD8, 0x20, 0x38))
        });
        viewModel.Settings.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 1,
            ColorHex = "#FF2050D8",
            Brush = new SolidColorBrush(Color.FromRgb(0x20, 0x50, 0xD8))
        });
        viewModel.Settings.SelectedThemeColorPaletteIndex = 1;

        await SaveSettingsAsync(viewModel);

        var settings = await settingsService.ReadAsync();
        Assert.Equal(ThemeColorModes.Wallpaper, settings.ThemeColorMode);
        Assert.Equal(["#FFD82038", "#FF2050D8"], settings.ThemeColorPalette);
        Assert.Equal(1, settings.SelectedThemeColorPaletteIndex);
    }

    [Fact]
    public async Task OpenResourcePanelAsync_WhenCookieUidExists_LoadsStatusAndConfig()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await WriteResourcePanelCookieLibraryAsync(cookiePath, "UID123");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);
        var handler = new ResourcePanelHandler();
        using var apiClient = new ResourcePanelApiClient(handler);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService, settingsService, uidService, apiClient);

        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);

        Assert.True(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.False(viewModel.ResourcePanel.IsResourcePanelUidMissing);
        Assert.Equal("UID123", viewModel.ResourcePanel.ResourcePanelUid);
        Assert.Equal(1, handler.StatusListCount);
        Assert.Equal(1, handler.ConfigGetCount);
        var text = viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Text);
        var voice = viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Voice);
        Assert.Equal(viewModel.Shell.I18n.ResourcePanelReady, text.StatusText);
        Assert.True(text.IsEnabled);
        Assert.Equal(viewModel.Shell.I18n.ResourcePanelWaiting, voice.StatusText);
        Assert.False(voice.IsEnabled);
    }

    [Fact]
    public async Task SaveResourcePanelAsync_SendsCnForEnabledAndJpForDisabled()
    {
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ResourcePanelUid = "UID123" });
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));
        var handler = new ResourcePanelHandler();
        using var apiClient = new ResourcePanelApiClient(handler);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService, settingsService, uidService, apiClient);
        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);
        viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Text).IsEnabled = true;
        viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Voice).IsEnabled = false;
        viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Media).IsEnabled = true;

        await viewModel.ResourcePanel.SaveResourcePanelCommand.ExecuteAsync(null);

        Assert.Equal("GET", handler.LastRequestMethod);
        Assert.Equal("/config/set?uid=UID123&text=cn&voice=jp&media=cn", handler.LastRequestPathAndQuery);
        Assert.Null(handler.LastRequestBody);
        Assert.Equal(1, handler.ConfigSetCount);
    }

    [Fact]
    public async Task OpenResourcePanelAsync_WhenUidMissing_ShowsManualInputAndSkipsApiCalls()
    {
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));
        var handler = new ResourcePanelHandler();
        using var apiClient = new ResourcePanelApiClient(handler);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = CreateViewModel(coreService, settingsService, uidService, apiClient);

        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);

        Assert.True(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.True(viewModel.ResourcePanel.IsResourcePanelUidMissing);
        Assert.Equal("", viewModel.ResourcePanel.ResourcePanelUid);
        Assert.Equal(0, handler.StatusListCount);
        Assert.Equal(0, handler.ConfigGetCount);
        Assert.Equal(0, handler.ConfigSetCount);
    }

    private MainWindowViewModel CreateViewModel(
        ILauncherCoreService coreService,
        LauncherSettingsService? settingsService = null,
        ResourcePanelUidService? resourcePanelUidService = null,
        ResourcePanelApiClient? resourcePanelApiClient = null,
        ToastService? toastService = null)
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
        resourcePanelUidService ??= new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing-resource-panel-cookie"));
        resourcePanelApiClient ??= new ResourcePanelApiClient(new ResourcePanelHandler());

        var localizationService = new LocalizationService();
        toastService ??= new ToastService();
        var diskSpaceService = new DiskSpaceService();
        var externalLinkService = new ExternalLinkService();
        var settingsViewModel = new SettingsViewModel(
            settingsService, localizationService, toastService,
            imageCacheService, externalLinkService, diskSpaceService);
        var resourcePanelViewModel = new ResourcePanelViewModel(
            resourcePanelUidService, resourcePanelApiClient, localizationService,
            toastService, diagnostics);
        var noticeStateService = new NoticeStateService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "shown_notices.json"));
        var gameUninstallService = new GameUninstallService(localGameStateService, diagnostics);

        return new MainWindowViewModel(
            coreService,
            settingsService,
            localizationService,
            toastService,
            diagnostics,
            new ShellViewModel(localizationService),
            new BackgroundViewModel(imageCacheService, diagnostics),
            new RemoteContentViewModel(localizationService, imageCacheService),
            new DialogsViewModel(localizationService, noticeStateService),
            new GameOperationsViewModel(
                gameLaunchService,
                gameDownloadService,
                gameUninstallService,
                localizationService,
                toastService,
                diagnostics),
            new ToastHostViewModel(toastService),
            new WindowChromeViewModel(externalLinkService),
            settingsViewModel,
            resourcePanelViewModel);
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

    private static async Task WriteResourcePanelCookieLibraryAsync(string path, string uid)
    {
        await using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(1);
        writer.Write(1);
        writer.Write(1);
        writer.Write("uid");
        writer.Write(uid);
        writer.Write(DateTime.UtcNow.ToBinary());
        writer.Write(DateTime.UtcNow.ToBinary());
        writer.Write(DateTime.FromBinary(0).ToBinary());
        writer.Write(2147483647L);
        writer.Write(false);
        writer.Write("bluearchive.cafe");
        writer.Write("/");
        writer.Write(false);
        writer.Write(false);
        writer.Flush();
    }

    private static void ApplyProgress(MainWindowViewModel viewModel, GameOperationProgress progress)
    {
        viewModel.Operations.ApplyProgress(progress);
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
        await viewModel.Settings.SaveSettingsCommand.ExecuteAsync(null);
    }

    public void Dispose()
    {
        imageCacheService.Dispose();
        apiClient.Dispose();
        httpClientFactory.Dispose();
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

    private sealed class ThrowingCoreService : ILauncherCoreService
    {
        public Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("load failed");
        }
    }

    private sealed class ResourcePanelHandler : HttpMessageHandler
    {
        public int StatusListCount { get; private set; }
        public int ConfigGetCount { get; private set; }
        public int ConfigSetCount { get; private set; }
        public string LastRequestMethod { get; private set; } = "";
        public string LastRequestPathAndQuery { get; private set; } = "";
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestMethod = request.Method.Method;
            LastRequestPathAndQuery = request.RequestUri?.PathAndQuery ?? "";
            LastRequestBody = request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            var path = request.RequestUri?.AbsolutePath ?? "";
            var json = "{}";
            if (path == "/status/list")
            {
                StatusListCount++;
                json = """
                {
                  "text": {
                    "official": { "version": "1.0.0" },
                    "localized": { "version": "1.0.0" }
                  },
                  "voice": {
                    "official": { "version": "2.0.0" },
                    "localized": { "version": "2.1.0" }
                  },
                  "media": {
                    "official": { "version": "3.0.0" },
                    "localized": { "version": "3.0.0" }
                  }
                }
                """;
            }
            else if (path == "/config/get")
            {
                ConfigGetCount++;
                json = """
                {
                  "text": "cn",
                  "voice": "jp",
                  "media": "cn"
                }
                """;
            }
            else if (request.RequestUri?.PathAndQuery == "/config/set?uid=UID123&text=cn&voice=jp&media=cn")
            {
                ConfigSetCount++;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
