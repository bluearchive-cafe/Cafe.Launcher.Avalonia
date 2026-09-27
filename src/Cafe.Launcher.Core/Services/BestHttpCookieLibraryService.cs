using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class BestHttpCookieLibraryService
{
    public BestHttpCookieLibrary Read(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);

        using var stream = File.OpenRead(libraryPath);
        return Read(stream);
    }

    public BestHttpCookieLibrary Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var version = reader.ReadInt32();
        var count = reader.ReadInt32();
        if (count is < 0 or > 10_000)
        {
            throw new InvalidDataException(
                $"BestHTTP cookie library count is out of range: {count} (expected 0–10,000).");
        }

        var cookies = new List<BestHttpCookie>(count);
        for (var index = 0; index < count; index++)
        {
            cookies.Add(ReadCookie(reader));
        }

        if (stream.CanSeek && stream.Position != stream.Length)
        {
            throw new InvalidDataException("BestHTTP cookie library contains trailing data.");
        }

        return new BestHttpCookieLibrary(version, cookies);
    }

    private static BestHttpCookie ReadCookie(BinaryReader reader)
    {
        try
        {
            return new BestHttpCookie(
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadString(),
                DateTime.FromBinary(reader.ReadInt64()),
                DateTime.FromBinary(reader.ReadInt64()),
                DateTime.FromBinary(reader.ReadInt64()),
                reader.ReadInt64(),
                reader.ReadBoolean(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadBoolean(),
                reader.ReadBoolean());
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("Corrupted BestHTTP cookie data.", ex);
        }
    }
}
