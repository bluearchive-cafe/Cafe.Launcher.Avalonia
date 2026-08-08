using Cafe.Launcher.Avalonia.Helpers;

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

    [Theory]
    // Pre-release < stable
    [InlineData("1.0.0-beta.1", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0-beta.1", 1)]
    // Same pre-release
    [InlineData("1.0.0-beta.1", "1.0.0-beta.1", 0)]
    // Numeric suffix comparison within pre-release
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1", 1)]
    [InlineData("1.0.0-beta.11", "1.0.0-beta.2", 1)]
    // ASCII sort: alpha < beta
    [InlineData("1.0.0-alpha.1", "1.0.0-beta.1", -1)]
    // Numeric < alpha per SemVer §11
    [InlineData("1.0.0-1", "1.0.0-alpha", -1)]
    // More pre-release fields: higher
    [InlineData("1.0.0-beta.1.fix", "1.0.0-beta.1", 1)]
    // Equal stable
    [InlineData("1.0.0", "1.0.0", 0)]
    // Higher major with pre-release still wins
    [InlineData("2.0.0-beta.1", "1.9.9", 1)]
    public void Compare_HandlesPreReleaseSuffixes(string left, string right, int expected)
    {
        Assert.Equal(expected, VersionComparer.Compare(left, right));
    }
}
