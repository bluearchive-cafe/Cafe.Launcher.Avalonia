using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.VideoWallpaper;

internal sealed class VideoWallpaperLoadGate : IDisposable
{
    private readonly TaskCompletionSource<bool> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration cancellationRegistration;

    public VideoWallpaperLoadGate(CancellationToken cancellationToken)
    {
        cancellationRegistration = cancellationToken.Register(
            static state =>
            {
                var (source, token) = ((TaskCompletionSource<bool>, CancellationToken))state!;
                source.TrySetCanceled(token);
            },
            (completion, cancellationToken));
    }

    public Task<bool> WaitAsync() => completion.Task;

    public void Succeed() => completion.TrySetResult(true);

    public void Fail() => completion.TrySetResult(false);

    public void Dispose() => cancellationRegistration.Dispose();
}
