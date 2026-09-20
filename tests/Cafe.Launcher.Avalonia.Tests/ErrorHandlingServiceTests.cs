using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class ErrorHandlingServiceTests
{
    private const string NetworkFailureText = "friendly network attribution";

    static ErrorHandlingServiceTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public async Task HandleErrorAsync_WhenErrorOccurs_ShowsErrorToast()
    {
        var (service, toastService) = CreateService();
        var exception = new InvalidOperationException("test error");
        ToastNotification? toast = null;
        toastService.ToastRaised += t => toast = t;

        await service.HandleErrorAsync("TestError", exception);

        Assert.NotNull(toast);
        Assert.Equal(ToastSeverity.Error, toast!.Severity);
        Assert.Equal("TestError（InvalidOperationException）：test error", toast.Message);
    }

    [Fact]
    public async Task HandleErrorAsync_WithCustomToastMessage_UsesProvidedMessage()
    {
        var (service, toastService) = CreateService();
        var exception = new InvalidOperationException("original");
        ToastNotification? toast = null;
        toastService.ToastRaised += t => toast = t;

        await service.HandleErrorAsync("TestError", exception, new ErrorHandlingOptions
        {
            ToastMessage = "custom message"
        });

        Assert.NotNull(toast);
        Assert.Equal("custom message（InvalidOperationException）：original", toast!.Message);
    }

    [Fact]
    public async Task HandleErrorAsync_WithoutExceptionDetails_ShowsOnlySafeToastMessage()
    {
        var (service, toastService) = CreateService();
        ToastNotification? toast = null;
        toastService.ToastRaised += notification => toast = notification;

        await service.HandleErrorAsync("TestError", new InvalidOperationException("resource key: secret"),
            new ErrorHandlingOptions
            {
                ToastMessage = "Localization unavailable.",
                IncludeExceptionDetails = false
            });

        Assert.NotNull(toast);
        Assert.Equal("Localization unavailable.", toast!.Message);
    }

    [Fact]
    public void FormatToastMessage_WithNestedExceptions_UsesOrderedDetails()
    {
        var exception = new InvalidOperationException(
            "outer",
            new IOException("inner", new IOException("disk")));

        var message = ErrorHandlingService.FormatToastMessage("Download failed", exception, NetworkFailureText);

        Assert.Equal(
            "Download failed（InvalidOperationException）：outer → IOException：inner → IOException：disk",
            message);
    }

    [Fact]
    public void FormatToastMessage_WithBlankExceptionMessage_OmitsBlankMessageSeparator()
    {
        var exception = new InvalidOperationException("", new IOException("disk unavailable"));

        var message = ErrorHandlingService.FormatToastMessage("Save failed", exception, NetworkFailureText);

        Assert.Equal("Save failed（InvalidOperationException） → IOException：disk unavailable", message);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(TaskCanceledException))]
    public void FormatToastMessage_WithNetworkFamilyException_UsesFriendlyAttribution(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "raw socket detail")!;

        var message = ErrorHandlingService.FormatToastMessage("Download failed", exception, NetworkFailureText);

        // 原文不进 toast（已在诊断日志中），代之以可行动的友好归因。
        Assert.DoesNotContain("raw socket detail", message, StringComparison.Ordinal);
        Assert.Equal($"Download failed：{NetworkFailureText}", message);
    }

    [Fact]
    public void FormatToastMessage_WithNetworkFamilyExceptionAndNoOperationMessage_ShowsAttributionOnly()
    {
        var message = ErrorHandlingService.FormatToastMessage(
            null, new HttpRequestException("raw"), NetworkFailureText);

        Assert.Equal(NetworkFailureText, message);
    }

    [Fact]
    public void FormatToastMessage_WithNonNetworkOutermost_KeepsTheDetailChain()
    {
        // 内层是网络异常但最外层不是：不改写为友好归因，保留类型链。
        var exception = new InvalidOperationException("outer", new HttpRequestException("inner"));

        var message = ErrorHandlingService.FormatToastMessage("Save failed", exception, NetworkFailureText);

        Assert.Equal("Save failed（InvalidOperationException）：outer → HttpRequestException：inner", message);
    }

    [Fact]
    public async Task HandleErrorAsync_WithNetworkException_ShowsLocalizedFriendlyToast()
    {
        var (service, toastService) = CreateService();
        ToastNotification? toast = null;
        toastService.ToastRaised += notification => toast = notification;

        await service.HandleErrorAsync("CheckUpdate", new HttpRequestException("socket refused"));

        Assert.NotNull(toast);
        var expected = new LocalizationService().T(LocalizationKeys.ErrorNetworkUnavailable);
        Assert.Contains(expected, toast!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("socket refused", toast.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleErrorAsync_WithShowToastFalse_DoesNotRaiseToast()
    {
        var (service, toastService) = CreateService();
        var exception = new InvalidOperationException("test error");
        ToastNotification? toast = null;
        toastService.ToastRaised += t => toast = t;

        await service.HandleErrorAsync("TestError", exception, new ErrorHandlingOptions
        {
            ShowToast = false
        });

        Assert.Null(toast);
    }

    [Fact]
    public async Task HandleCriticalErrorAsync_RaisesRequestedEventWithDetailsAndNoToast()
    {
        var (service, toastService) = CreateService();
        var exception = new InvalidOperationException("critical failure");
        CriticalErrorInfo? info = null;
        service.CriticalErrorRequested += i => info = i;
        ToastNotification? toast = null;
        toastService.ToastRaised += t => toast = t;

        await service.HandleCriticalErrorAsync("CriticalContext", exception);

        Assert.Null(toast);
        Assert.NotNull(info);
        Assert.Equal("CriticalContext", info!.Context);
        Assert.Equal("critical failure", info.Message);
        Assert.Contains("CriticalContext", info.Details, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", info.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleErrorAsync_WhenDiagnosticsFails_DoesNotThrow()
    {
        var localizer = new LocalizationService();
        // Use a read-only directory so the Serilog file sink fails,
        // exercising the catch in ErrorAsync.
        var readOnlyDir = Path.Combine(Path.GetTempPath(), "Cafe.Launcher.Avalonia.Tests", "ReadOnlyLog");
        Directory.CreateDirectory(readOnlyDir);
        var logFile = Path.Combine(readOnlyDir, "unified.log");
        File.WriteAllText(logFile, "");
        File.SetAttributes(logFile, FileAttributes.ReadOnly);
        try
        {
            using var logger = new UnifiedLogger(readOnlyDir);
            var diagnostics = new LocalDiagnostics(logger);
            var toastService = new ToastService();
            var service = new ErrorHandlingService(localizer, diagnostics, toastService);
            var exception = new InvalidOperationException("test error");

            // Should not throw even when diagnostics logging fails internally.
            await service.HandleErrorAsync("TestError", exception);
        }
        finally
        {
            File.SetAttributes(logFile, FileAttributes.Normal);
            Directory.Delete(readOnlyDir, recursive: true);
        }
    }

    private static (ErrorHandlingService Service, ToastService ToastService) CreateService()
    {
        var localizer = new LocalizationService();
        var diagnostics = new LocalDiagnostics();
        var toastService = new ToastService();
        return (new ErrorHandlingService(localizer, diagnostics, toastService), toastService);
    }
}
