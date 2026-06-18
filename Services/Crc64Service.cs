using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class Crc64Service
{
    // CRC-64 as used by Blue Archive catalog:
    //   crcmod.mkCrcFun(0x142F0E1EBA9EA3693, initCrc=0, rev=True, xorOut=0xFFFFFFFFFFFFFFFF)
    // crcmod internally computes: xorOut ^ fun(data, xorOut ^ initCrc, table)
    // Equivalent to init=0xFFFFFFFFFFFFFFFF, xorOut=0xFFFFFFFFFFFFFFFF (CRC-64/XZ)
    // crcmod _mkTable_r: poly = _bitrev(poly & mask, 64)
    // 0x42F0E1EBA9EA3693 reversed → 0xC96C5795D7870F42
    private const ulong Polynomial = 0xC96C5795D7870F42;
    private const ulong XorOut = 0xFFFFFFFFFFFFFFFF;
    private static readonly ulong[] Table = BuildTable();

    public async Task<string> ComputeFileAsync(
        string filePath,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("File does not exist.", filePath);

        ulong crc = XorOut;
        var buffer = new byte[1024 * 1024];
        long readTotal = 0;

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;

            crc = Update(crc, buffer.AsSpan(0, read));
            readTotal += read;

            if (fileInfo.Length > 0)
                progress?.Invoke((int)Math.Round(readTotal * 100d / fileInfo.Length));
        }

        progress?.Invoke(100);
        return (crc ^ XorOut).ToString();
    }

    /// <summary>Reflected (right-shifting) CRC64 update.</summary>
    private static ulong Update(ulong crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
            crc = Table[(byte)(crc ^ value)] ^ (crc >> 8);
        return crc;
    }

    /// <summary>Build reflected CRC64 lookup table (LSB-first).</summary>
    private static ulong[] BuildTable()
    {
        var table = new ulong[256];
        for (var i = 0; i < 256; i++)
        {
            ulong crc = (ulong)i;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
            table[i] = crc;
        }
        return table;
    }
}
