namespace Cafe.Launcher.Core.Services;

public readonly record struct DiskSpaceCheckResult(long RequiredBytes, long? AvailableBytes)
{
    public bool IsAvailableKnown => AvailableBytes.HasValue;

    public bool HasEnoughSpace => AvailableBytes.HasValue && AvailableBytes.Value >= RequiredBytes;
}
