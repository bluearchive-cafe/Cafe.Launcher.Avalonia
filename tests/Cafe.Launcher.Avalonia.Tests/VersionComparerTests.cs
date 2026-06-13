using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class VersionComparerTests
{
    [Theory]
    [InlineData("1.7.3", "1.7.2", 1)]
    [InlineData("1.7.2", "1.7.3", -1)]
    [InlineData("1.7.2", "1.7.2", 0)]
    [InlineData("1.7", "1.7.0", 0)]
    [InlineData("1.bad.1", "1.0.2", -1)]
    public void Compare_UsesExistingNumericSegmentBehavior(string left, string right, int expected)
    {
        Assert.Equal(expected, VersionComparer.Compare(left, right));
    }
}
