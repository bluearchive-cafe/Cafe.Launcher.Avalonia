using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>How a download call terminated for one manifest file.</summary>
public enum DownloadOutcomeKind
{
    /// <summary>This call transferred and CRC64-verified the file; <see cref="DownloadOutcome.Crc64"/> carries the verified hash.</summary>
    Transferred,

    /// <summary>The temp file was already complete on entry (resumed session); the caller must still verify it at install time.</summary>
    AlreadyComplete
}

/// <summary>
/// Explicit result of one file download. The kind carries every fact the
/// caller needs — there is no null contract to compensate for.
/// </summary>
public readonly record struct DownloadOutcome(DownloadOutcomeKind Kind, string? Crc64)
{
    public static DownloadOutcome Transferred(string crc64) =>
        new(DownloadOutcomeKind.Transferred, crc64);

    public static DownloadOutcome AlreadyComplete() =>
        new(DownloadOutcomeKind.AlreadyComplete, null);
}

/// <summary>
/// Downloads a single manifest file with retry domain cycling, range resume,
/// CRC64 verification, cooperative pause, and cleanup. Owns the .tmp staging
/// state machine: presence, completeness, and oversize handling are decided
/// here and expressed through <see cref="DownloadOutcome"/> — callers never
/// stat temporary files themselves (except via
/// <see cref="GetExistingDownloadedSize"/> for batch-level progress seeding,
/// which reads the same single implementation of the length semantics).
/// </summary>
public interface IFileDownloadService
{
    /// <summary>
    /// Returns the resumable byte count of an existing temp file: 0 when the
    /// file is absent or larger than expected, its length when it is a partial
    /// or exact download. Used by batch orchestration to seed progress before
    /// any transfer starts.
    /// </summary>
    long GetExistingDownloadedSize(string targetTempPath, long expectedSize);

    /// <summary>Downloads one manifest file with CDN retry domain cycling.</summary>
    /// <param name="request">Immutable file identity, source, size, and hash values.</param>
    /// <param name="control">Batch transport plus cooperative pause/progress controls.</param>
    /// <param name="cancellationToken">Propagates cancellation.</param>
    /// <returns>
    /// <see cref="DownloadOutcomeKind.Transferred"/> with the CRC64 verified
    /// against <c>request.ExpectedHash</c> when this call transferred the file
    /// itself, or <see cref="DownloadOutcomeKind.AlreadyComplete"/> when the
    /// temp file was already complete on entry (resumed session) — the caller
    /// must still verify those at install time.
    /// </returns>
    Task<DownloadOutcome> DownloadAsync(
        FileDownloadRequest request,
        FileDownloadOperationControl control,
        CancellationToken cancellationToken);
}
