using System;
using System.IO;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// A content hash the planning pass already computed for a file it found healthy, together with
/// the size and last-write time that file had when it was hashed. The install phase reuses the
/// hash only while the file still matches that witness, so content that changed after planning is
/// read again rather than trusted — the repair session therefore keeps its content-corruption
/// self-heal while dropping the second full read of an untouched install.
/// </summary>
internal readonly record struct PlannedFileHash(string Hash, long Length, DateTime LastWriteUtc)
{
    /// <summary>Captures the witness of <paramref name="filePath"/> as it is right now.</summary>
    public static PlannedFileHash Capture(string filePath, string hash)
    {
        var info = new FileInfo(filePath);
        return new PlannedFileHash(hash, info.Length, info.LastWriteTimeUtc);
    }

    /// <summary>Gets whether <paramref name="filePath"/> still looks exactly as it did when hashed.</summary>
    public bool Matches(string filePath)
    {
        var info = new FileInfo(filePath);
        return info.Exists
            && info.Length == Length
            && info.LastWriteTimeUtc == LastWriteUtc;
    }
}
