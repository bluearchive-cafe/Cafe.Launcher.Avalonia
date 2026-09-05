using System;
using System.Buffers.Binary;
using System.IO;
using System.Globalization;
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
    private static readonly ulong[][] Tables = BuildTables();

    public async Task<string> ComputeFileAsync(
        string filePath,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ulong crc = XorOut;
        var buffer = new byte[1024 * 1024];
        long readTotal = 0;

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            crc = Update(crc, buffer.AsSpan(0, read));
            readTotal += read;

            if (stream.Length > 0)
                progress?.Invoke((int)Math.Round(readTotal * 100d / stream.Length));
        }

        progress?.Invoke(100);
        return (crc ^ XorOut).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reflected (right-shifting) CRC64 update. Slicing-by-8: 每次消费 8 字节，
    /// 查表结果与逐字节算法逐位一致，但吞吐量高一个数量级——该函数位于
    /// 下载验证热路径（整个安装目录被完整读盘哈希）。
    /// </summary>
    private static ulong Update(ulong crc, ReadOnlySpan<byte> data)
    {
        var tables = Tables;
        var offset = 0;
        var end = data.Length;

        while (offset + 8 <= end)
        {
            crc ^= BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
            crc =
                tables[7][crc & 0xFF] ^
                tables[6][(crc >> 8) & 0xFF] ^
                tables[5][(crc >> 16) & 0xFF] ^
                tables[4][(crc >> 24) & 0xFF] ^
                tables[3][(crc >> 32) & 0xFF] ^
                tables[2][(crc >> 40) & 0xFF] ^
                tables[1][(crc >> 48) & 0xFF] ^
                tables[0][(crc >> 56) & 0xFF];
            offset += 8;
        }

        while (offset < end)
        {
            crc = tables[0][(byte)(crc ^ data[offset])] ^ (crc >> 8);
            offset++;
        }

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

    /// <summary>Slicing-by-8 tables: T0 是经典逐字节表，T(k)[i] = T0[T(k-1)[i] 低字节] ^ (T(k-1)[i] >> 8)。</summary>
    private static ulong[][] BuildTables()
    {
        var tables = new ulong[8][];
        tables[0] = BuildTable();
        for (var k = 1; k < 8; k++)
        {
            var previous = tables[k - 1];
            var current = new ulong[256];
            for (var i = 0; i < 256; i++)
            {
                var previousCrc = previous[i];
                current[i] = tables[0][(byte)previousCrc] ^ (previousCrc >> 8);
            }
            tables[k] = current;
        }
        return tables;
    }
}
