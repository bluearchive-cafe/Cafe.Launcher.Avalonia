using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Downloads a single manifest file with retry domain cycling, range resume,
/// CRC64 verification, cooperative pause, and cleanup.
/// </summary>
public interface IFileDownloadService
{
    /// <summary>Downloads one manifest file with CDN retry domain cycling.</summary>
    /// <param name="request">Immutable file identity, source, size, and hash values.</param>
    /// <param name="control">Shared transport and cooperative pause/progress controls.</param>
    /// <param name="cancellationToken">Propagates cancellation.</param>
    Task DownloadAsync(
        FileDownloadRequest request,
        FileDownloadOperationControl control,
        CancellationToken cancellationToken);
}
