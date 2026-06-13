using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Cafe.Launcher.Avalonia.Converters;

/// <summary>
/// Converts a URL string to an Avalonia Bitmap by downloading the image.
/// Returns null while loading, then updates via property change notification.
/// </summary>
public sealed class UrlToBitmapConverter : IValueConverter
{
    public static readonly UrlToBitmapConverter Instance = new();
    private static readonly SocketsHttpHandler httpHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15)
    };
    private static readonly HttpClient httpClient = new(httpHandler) { Timeout = TimeSpan.FromSeconds(30) };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;

        // Return a placeholder immediately, then load asynchronously
        var task = LoadBitmapAsync(url);
        return new TaskCompletionSourceNotifying<Bitmap?>(task);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static async Task<Bitmap?> LoadBitmapAsync(string url)
    {
        try
        {
            var bytes = await httpClient.GetByteArrayAsync(url);
            return await Dispatcher.UIThread.InvokeAsync(() => new Bitmap(new System.IO.MemoryStream(bytes)));
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Wraps a Task so that Avalonia's binding system can await it and display the result.
/// </summary>
internal sealed class TaskCompletionSourceNotifying<T> : INotifyTaskCompletion<T>
{
    private readonly Task<T> task;

    public TaskCompletionSourceNotifying(Task<T> task)
    {
        this.task = task;
        task.ContinueWith(_ => Dispatcher.UIThread.InvokeAsync(() => Completed?.Invoke(this, EventArgs.Empty)),
            TaskScheduler.Default);
    }

    public Task<T> Task => task;
    public T? Result => task.IsCompletedSuccessfully ? task.Result : default;
    public bool IsCompleted => task.IsCompleted;
    public event EventHandler? Completed;
}

/// <summary>
/// Interface for notifying bindings when a task completes.
/// </summary>
internal interface INotifyTaskCompletion<T>
{
    Task<T> Task { get; }
    T? Result { get; }
    bool IsCompleted { get; }
    event EventHandler? Completed;
}
