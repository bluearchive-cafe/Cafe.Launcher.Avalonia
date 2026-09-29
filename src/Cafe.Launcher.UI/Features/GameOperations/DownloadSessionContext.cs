using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>
/// Collaborator cluster for one download/repair session, assembled once by the
/// download service and passed to <see cref="DownloadSession"/> as a single
/// object — the session interface no longer exposes a positional 17-parameter
/// parameter list.
/// </summary>
internal sealed record DownloadSessionContext(
    YostarGameProfile GameProfile,
    ILauncherApiClient ApiClient,
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
