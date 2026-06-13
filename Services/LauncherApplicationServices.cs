using System;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class LauncherApplicationServices : IDisposable
{
    private bool disposed;

    public LauncherApplicationServices()
    {
        ApiClient = new LauncherApiClient();
        LocalGameStateService = new LocalGameStateService();
        SettingsService = new LauncherSettingsService();
        Diagnostics = new LocalDiagnostics();
        ProxySettingsService = new ProxySettingsService();
        Crc64Service = new Crc64Service();
        DiskSpaceService = new DiskSpaceService();
        LocalizationService = new LocalizationService();
        CoreService = new LauncherCoreService(ApiClient, LocalGameStateService, SettingsService);
        ClickCodeService = new ClickCodeService();
        ManifestValidationService = new ManifestValidationService(ApiClient);
        GameLaunchService = new GameLaunchService(ManifestValidationService, ClickCodeService);
        GameUninstallService = new GameUninstallService(LocalGameStateService, Diagnostics);
        ExternalLinkService = new ExternalLinkService(Diagnostics);
        ToastService = new ToastService();
        DownloadStateService = new DownloadStateService();
        ImageCacheService = new ImageCacheService();
        NoticeStateService = new NoticeStateService();
        GameDownloadService = new GameDownloadService(
            ApiClient,
            LocalGameStateService,
            SettingsService,
            ProxySettingsService,
            Crc64Service,
            DiskSpaceService,
            Diagnostics,
            DownloadStateService);
    }

    public LauncherApiClient ApiClient { get; }
    public LocalGameStateService LocalGameStateService { get; }
    public LauncherSettingsService SettingsService { get; }
    public LocalDiagnostics Diagnostics { get; }
    public ProxySettingsService ProxySettingsService { get; }
    public Crc64Service Crc64Service { get; }
    public DiskSpaceService DiskSpaceService { get; }
    public LocalizationService LocalizationService { get; }
    public ILauncherCoreService CoreService { get; }
    public ClickCodeService ClickCodeService { get; }
    public ManifestValidationService ManifestValidationService { get; }
    public GameLaunchService GameLaunchService { get; }
    public GameUninstallService GameUninstallService { get; }
    public ExternalLinkService ExternalLinkService { get; }
    public ToastService ToastService { get; }
    public DownloadStateService DownloadStateService { get; }
    public ImageCacheService ImageCacheService { get; }
    public NoticeStateService NoticeStateService { get; }
    public GameDownloadService GameDownloadService { get; }

    public MainWindowViewModel CreateMainWindowViewModel()
    {
        return new MainWindowViewModel(
            CoreService,
            SettingsService,
            LocalGameStateService,
            GameLaunchService,
            GameDownloadService,
            GameUninstallService,
            ExternalLinkService,
            DiskSpaceService,
            LocalizationService,
            ToastService,
            Diagnostics,
            NoticeStateService,
            ImageCacheService);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        GameDownloadService.Dispose();
        ImageCacheService.Dispose();
        ApiClient.Dispose();
    }
}
