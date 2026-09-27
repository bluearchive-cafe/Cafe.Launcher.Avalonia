using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Collaborator cluster for one download/repair session, assembled once by the
/// download service and passed to <see cref="DownloadSession"/> as a single
/// object — the session interface no longer exposes a positional 17-parameter
/// parameter list.
/// </summary>
internal sealed record DownloadSessionContext(
    LauncherApiClient ApiClient,
    RemoteManifestService RemoteManifestService,
    IFileDownloadService FileDownloadService,
    IDownloadTransportSource TransportSource,
    ICrc64Service ICrc64Service,
    ILocalInstallationStateStore ILocalInstallationStateStore,
    ILauncherSettingsService SettingsService,
    IDiskSpaceService IDiskSpaceService,
    ILauncherDiagnostics Diagnostics,
    LocalizationService Localizer,
    IGameInstallationPath InstallationPath,
    DownloadCheckpointStore CheckpointStore,
    IGameProcessTracker GameProcessTracker);
