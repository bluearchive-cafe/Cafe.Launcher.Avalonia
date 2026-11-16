using System.Text.Json.Serialization;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services.Diagnostics;

namespace Cafe.Launcher.UI.Helpers;

[JsonSerializable(typeof(ResourcePanelStatusResponse))]
[JsonSerializable(typeof(ResourcePanelConfigResponse))]
internal sealed partial class ResourcePanelJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LauncherSettings))]
[JsonSerializable(typeof(DownloadTaskState))]
[JsonSerializable(typeof(CrashReport))]
internal sealed partial class UiStorageJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LogExportService.SystemInfoDocument))]
internal sealed partial class LogExportJsonContext : JsonSerializerContext;
