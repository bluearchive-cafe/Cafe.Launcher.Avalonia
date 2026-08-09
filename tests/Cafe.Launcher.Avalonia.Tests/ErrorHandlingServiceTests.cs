using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ErrorHandlingServiceTests
{
    static ErrorHandlingServiceTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public async Task HandleErrorAsync_WhenErrorOccurs_ShowsErrorToastWithoutChangingOperationNote()
    {
        var (service, toastService, shell) = CreateService();
        var previousNote = shell.OperationNote;
        var exception = new InvalidOperationException("test error");
        ToastNotification? toast = null;
        toastService.ToastRaised += t => toast = t;

        await service.HandleErrorAsync("TestError", exception);

        Assert.NotNull(toast);
        Assert.Equal(ToastSeverity.Error, toast!.Severity);
        Assert.Equal("test error", toast.Message);
        Assert.Equal(previousNote, shell.OperationNote);
    }

    [Fact]
    public async Task HandleErrorAsync_WithCustomToastMessage_UsesProvidedMessage()
    {
        var (service, toastService, _) = CreateService();
        var exception = new InvalidOperationException("original");
        ToastNotification? toast = null;
        toastService.ToastRaised += t => toast = t;

        await service.HandleErrorAsync("TestError", exception, new ErrorHandlingOptions
        {
            ToastMessage = "custom message"
        });

        Assert.NotNull(toast);
        Assert.Equal("custom message", toast!.Message);
    }

    [Fact]
    public async Task HandleErrorAsync_WithShowToastFalse_DoesNotRaiseToast()
    {
        var (service, toastService, _) = CreateService();
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
    public async Task HandleErrorAsync_WithNullOperationNoteKey_DoesNotChangeOperationNote()
    {
        var (service, _, shell) = CreateService();
        var previous = shell.OperationNote;
        var exception = new InvalidOperationException("test error");

        await service.HandleErrorAsync("TestError", exception, new ErrorHandlingOptions
        {
            OperationNoteKey = null
        });

        Assert.Equal(previous, shell.OperationNote);
    }

    [Fact]
    public async Task HandleErrorAsync_WithCustomOperationNoteKey_FormatsWithThatKey()
    {
        var (service, _, shell) = CreateService();
        var exception = new InvalidOperationException("test error");

        await service.HandleErrorAsync("TestError", exception, new ErrorHandlingOptions
        {
            OperationNoteKey = "gameLaunchFailed"
        });

        Assert.StartsWith("Game launch failed:", shell.OperationNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleCriticalErrorAsync_RaisesRequestedEventWithDetailsAndNoToast()
    {
        var (service, toastService, shell) = CreateService();
        var previousNote = shell.OperationNote;
        var exception = new InvalidOperationException("critical failure");
        CriticalErrorInfo? info = null;
        service.CriticalErrorRequested += i => info = i;
        ToastNotification? toast = null;
        toastService.ToastRaised += t => toast = t;

        await service.HandleCriticalErrorAsync("CriticalContext", exception);

        Assert.Null(toast);
        Assert.Equal(previousNote, shell.OperationNote);
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
            var shell = new ShellViewModel(localizer);
            var service = new ErrorHandlingService(localizer, diagnostics, toastService, shell);
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

    private static (ErrorHandlingService Service, ToastService ToastService, ShellViewModel Shell) CreateService()
    {
        var localizer = new LocalizationService();
        var diagnostics = new LocalDiagnostics();
        var toastService = new ToastService();
        var shell = new ShellViewModel(localizer);
        return (new ErrorHandlingService(localizer, diagnostics, toastService, shell), toastService, shell);
    }
}
