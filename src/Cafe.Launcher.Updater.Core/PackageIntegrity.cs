using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Updater;

/// <summary>SHA-256 integrity helpers shared by the helper's argument validation and apply step.</summary>
public static class PackageIntegrity
{
    /// <summary>True when <paramref name="value"/> is exactly 64 hexadecimal characters.</summary>
    public static bool IsHexSha256(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Recomputes the SHA-256 of <paramref name="path"/> and compares it to the expected digest.</summary>
    public static async Task<bool> MatchesSha256Async(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81_920,
            useAsync: true);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(
            Convert.ToHexString(actual),
            expectedSha256,
            StringComparison.OrdinalIgnoreCase);
    }
}
