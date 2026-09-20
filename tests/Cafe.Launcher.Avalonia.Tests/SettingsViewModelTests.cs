using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 更新检查的「服务→UI」粘合层回归：CheckForUpdatesCommand 决定错误 toast、
/// 已最新 toast、打开更新对话框三个分支及失败消息格式化。LauncherUpdateService
/// 与 DialogsViewModel 各自正确不等于组合正确——此处以脚本化传输 + 真实
/// ToastService/DialogsViewModel 断言最终用户可见行为。
/// </summary>
[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class SettingsViewModelTests : IDisposable
{
    /// <summary>
    /// 本类各静态装配辅助方法共用的临时父目录：xUnit 为每个测试方法新建实例，故它本身
    /// 就是每测试一个；每次装配再取一个子目录，保持「每个上下文一个独立数据根」的原语义。
    /// </summary>
    private readonly TestDirectory tempDir = TestDirectory.Create();

    public void Dispose() => tempDir.Dispose();

    // 降级语义按 URI 区分两端点，常量与 LauncherUpdateServiceTests 一致。
    private static readonly Uri ProxyReleasesUri =
        new(new Uri(ApiConfig.LauncherApiBaseUrl), ApiConfig.LauncherReleasesPath);

    private static readonly Uri GitHubReleasesUri = new(ApiConfig.GitHubReleasesApiUrl);

    private readonly ToastService toastService = new();
    private readonly List<ToastNotification> raisedToasts = [];

    static SettingsViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    public SettingsViewModelTests()
    {
        toastService.ToastRaised += raisedToasts.Add;
    }

    [Fact]
    public async Task CheckForUpdatesCommand_WhenBothEndpointsFail_ShowsFriendlyNetworkAttribution()
    {
        var localizer = new LocalizationService();
        var transport = new StubRemoteHttpTransport(
            _ => new HttpRequestException("simulated update endpoint outage"));
        using var settings = CreateSettingsViewModel(localizer, transport);

        await settings.CheckForUpdatesCommand.ExecuteAsync(null);

        var toast = Assert.Single(raisedToasts);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Contains(
            localizer.T(LocalizationKeys.LauncherUpdateCheckFailed),
            toast.Message,
            StringComparison.Ordinal);
        // 网络家族失败给友好归因：原文不进 toast（已在诊断日志中）。
        Assert.Contains(
            localizer.T(LocalizationKeys.ErrorNetworkUnavailable),
            toast.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("simulated update endpoint outage", toast.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckForUpdatesCommand_WhenNonNetworkFailure_ShowsErrorToastWithExceptionDetail()
    {
        // JsonException 会被更新检查转为失败结果，且不属于网络家族：
        // 原文保留在 toast 的「类型：消息」链里。
        var localizer = new LocalizationService();
        var transport = new StubRemoteHttpTransport(
            _ => new JsonException("simulated update payload corruption"));
        using var settings = CreateSettingsViewModel(localizer, transport);

        await settings.CheckForUpdatesCommand.ExecuteAsync(null);

        var toast = Assert.Single(raisedToasts);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Contains(
            localizer.T(LocalizationKeys.LauncherUpdateCheckFailed),
            toast.Message,
            StringComparison.Ordinal);
        Assert.Contains("simulated update payload corruption", toast.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckForUpdatesCommand_WhenNoReleaseMetadata_ShowsErrorToastWithFailureMessage()
    {
        var localizer = new LocalizationService();
        var transport = new StubRemoteHttpTransport(_ => "[]");
        using var settings = CreateSettingsViewModel(localizer, transport);

        await settings.CheckForUpdatesCommand.ExecuteAsync(null);

        // 无异常的失败走「操作消息：FailureMessage」拼接格式，而不是异常摘要。
        var toast = Assert.Single(raisedToasts);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Contains(
            localizer.T(LocalizationKeys.LauncherUpdateCheckFailed),
            toast.Message,
            StringComparison.Ordinal);
        Assert.Contains("No release metadata was returned.", toast.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckForUpdatesCommand_WhenLatestMatchesCurrentVersion_ShowsUpToDateToast()
    {
        var localizer = new LocalizationService();
        var transport = CreateReleasesTransport(BuildInfo.LauncherVersion);
        using var settings = CreateSettingsViewModel(localizer, transport);

        await settings.CheckForUpdatesCommand.ExecuteAsync(null);

        var toast = Assert.Single(raisedToasts);
        Assert.Equal(ToastSeverity.Success, toast.Severity);
        Assert.Equal(
            localizer.F(LocalizationKeys.LauncherUpdateUpToDate, BuildInfo.LauncherVersion),
            toast.Message);
    }

    [Fact]
    public async Task CheckForUpdatesCommand_WhenNewerReleaseAvailable_OpensUpdateDialogWithFiles()
    {
        var localizer = new LocalizationService();
        var dialogs = CreateDialogsViewModel();
        var transport = CreateReleasesTransport("9.9.9");
        using var settings = CreateSettingsViewModel(localizer, transport, dialogs);

        await settings.CheckForUpdatesCommand.ExecuteAsync(null);

        // 有更新时不弹 toast，唯一感知通道是更新对话框。
        Assert.Empty(raisedToasts);
        Assert.True(dialogs.IsUpdateAvailableVisible);
        Assert.Equal("9.9.9", dialogs.UpdateAvailableVersion);
        var file = Assert.Single(dialogs.UpdateAvailableFiles);
        Assert.Equal(
            "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v9.9.9/Cafe.Launcher_v9.9.9.zip",
            file.Url);
    }

    private SettingsViewModel CreateSettingsViewModel(
        LocalizationService localizer,
        StubRemoteHttpTransport transport,
        DialogsViewModel? dialogs = null) =>
        new(
            null!,
            null!,
            localizer,
            toastService,
            new LauncherUpdateService(transport),
            dialogs ?? CreateDialogsViewModel(),
            null!,
            null!,
            new SettingsOptionsViewModel(localizer, new DiskSpaceService()),
            new SettingsAppearanceViewModel(new SettingsEditor(), new ThemeApplier()),
            new RecordingErrorHandlingService(),
            new StubGameRuntime(),
            new StubFilePickerService());

    private string NextDataRoot() => tempDir.Sub(Guid.NewGuid().ToString("N"));

    private DialogsViewModel CreateDialogsViewModel()
    {
        return new DialogsViewModel(
            new LocalizationService(),
            new NoticeStateService(TestDataRoot.ForDirectory(NextDataRoot())),
            new SetupWizardViewModel(
                new LocalizationService(),
                new GameInstallationPath(),
                new LocalInstallationStateStore(),
                new LocalDiagnostics(),
                new StubFilePickerService()),
            new LocalDiagnostics(),
            action =>
            {
                action();
                return Task.CompletedTask;
            });
    }

    private static StubRemoteHttpTransport CreateReleasesTransport(string version) =>
        new(uri =>
        {
            if (uri == ProxyReleasesUri)
            {
                return $$"""
                [
                  {
                    "version": "{{version}}",
                    "files": [
                      {
                        "name": "Cafe.Launcher_v{{version}}.zip",
                        "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v{{version}}/Cafe.Launcher_v{{version}}.zip",
                        "sha512": "abc",
                        "size": 100
                      }
                    ],
                    "releaseDate": "2026-06-15T00:00:00Z"
                  }
                ]
                """;
            }

            throw new InvalidOperationException($"Unexpected request URI: {uri}");
        });

    private sealed class StubGameRuntime : IGameRuntime
    {
        public Task<GameRuntimeLaunchResult> LaunchAsync(
            GameLaunchRequest request,
            GameRuntimeConfiguration configuration,
            System.Threading.CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("StubGameRuntime never launches games.");

        public Task<IReadOnlyList<GameRuntimeStatusEntry>> GetStatusesAsync(
            GameRuntimeConfiguration configuration,
            System.Threading.CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameRuntimeStatusEntry>>([]);
    }
}
