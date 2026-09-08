using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Views;

/// <summary>Independent terminal window for a persisted crash report.</summary>
public partial class CrashReportWindow : Window
{
    private readonly CrashReportWindowViewModel viewModel;

    public CrashReportWindow()
        : this(new CrashReport
        {
            Id = "CR-PREVIEW",
            OccurredAt = DateTimeOffset.Now,
            Source = "Preview",
            AppVersion = Constants.BuildInfo.LauncherVersion,
            OperatingSystem = Environment.OSVersion.ToString(),
            UiCulture = System.Globalization.CultureInfo.CurrentUICulture.Name,
            ExceptionType = nameof(InvalidOperationException),
            TechnicalDetails = "Preview crash details"
        })
    {
    }

    public CrashReportWindow(CrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        InitializeComponent();
        viewModel = new CrashReportWindowViewModel(report);
        DataContext = viewModel;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        Dispatcher.UIThread.Post(() => ExitButton.Focus(), DispatcherPriority.Background);
    }

    private async void OnCopyDetailsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            await clipboard.SetTextAsync(viewModel.TechnicalDetails);
            CopyDetailsButton.Content = viewModel.CopiedText;
        }
        catch
        {
            // Clipboard is optional on some platforms; the selectable details remain available.
        }
    }

    private void OnOpenLogsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            _ = ShellFolderOpener.OpenInFileManager(viewModel.SnapshotDirectory);
        }
        catch
        {
            // Opening the shell must not destabilize the already-failing process.
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
