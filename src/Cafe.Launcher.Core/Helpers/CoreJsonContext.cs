using System.Collections.Generic;
using System.Text.Json.Serialization;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Auth;
using Cafe.Launcher.Core.Services.GameRuntime;

namespace Cafe.Launcher.Core.Helpers;

[JsonSerializable(typeof(AuthorizationHeaderFactory.AuthorizationHead))]
[JsonSerializable(typeof(AuthorizationHeaderFactory.AuthorizationHeader))]
[JsonSerializable(typeof(HashSet<string>))]
[JsonSerializable(typeof(RemoteManifest))]
[JsonSerializable(typeof(LauncherApiEnvelope<GameConfigResponse>), TypeInfoPropertyName = "GameConfigEnvelope")]
[JsonSerializable(typeof(LauncherApiEnvelope<BaseConfigResponse>), TypeInfoPropertyName = "BaseConfigEnvelope")]
[JsonSerializable(typeof(LauncherApiEnvelope<CdnConfigResponse>), TypeInfoPropertyName = "CdnConfigEnvelope")]
[JsonSerializable(typeof(LauncherApiEnvelope<OperationsResourceResponse>), TypeInfoPropertyName = "OperationsResourceEnvelope")]
[JsonSerializable(typeof(LauncherApiEnvelope<SocialMediaResourceResponse>), TypeInfoPropertyName = "SocialMediaResourceEnvelope")]
[JsonSerializable(typeof(LauncherApiEnvelope<InstallationConfigResponse>), TypeInfoPropertyName = "InstallationConfigEnvelope")]
[JsonSerializable(typeof(LauncherApiEnvelope<ManifestUrlResponse>), TypeInfoPropertyName = "ManifestUrlEnvelope")]
[JsonSerializable(typeof(List<LauncherReleaseResponse>))]
[JsonSerializable(typeof(LauncherReleaseNotesResponse))]
[JsonSerializable(typeof(List<LauncherUpdateService.GitHubRelease>))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LauncherSettings))]
[JsonSerializable(typeof(GameSettings))]
[JsonSerializable(typeof(LocalManifest))]
[JsonSerializable(typeof(GameLauncherConfig))]
internal sealed partial class CoreStorageJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PrefixMetadataStore.PrefixMetadataDocument))]
[JsonSerializable(typeof(CompatibilityEnvironmentPrecheck.CompatibilityReportDocument))]
internal sealed partial class GameRuntimeJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(GameDeviceOptionDocument))]
internal sealed partial class GameDeviceOptionJsonContext : JsonSerializerContext;
