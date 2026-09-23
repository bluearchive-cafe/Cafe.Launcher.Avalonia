using Cafe.Launcher.Avalonia.Services.Update;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherUpdateChecksumManifestTests
{
    private const string DigestA = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string DigestB = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    [Fact]
    public void TryParse_SingleEntry_ReturnsDigest()
    {
        var parsed = LauncherUpdateChecksumManifest.TryParse(
            $"{DigestA}  Cafe.Launcher.Avalonia_v1.2.3_win-x64.zip",
            out var hashes);

        Assert.True(parsed);
        Assert.True(LauncherUpdateChecksumManifest.TryGetHash(hashes, "Cafe.Launcher.Avalonia_v1.2.3_win-x64.zip", out var hash));
        Assert.Equal(DigestA, hash);
    }

    [Fact]
    public void TryParse_MultipleEntries_ReturnsAllDigests()
    {
        var content = $"{DigestA}  a.zip{Environment.NewLine}{DigestB}  b.exe{Environment.NewLine}";

        var parsed = LauncherUpdateChecksumManifest.TryParse(content, out var hashes);

        Assert.True(parsed);
        Assert.Equal(2, hashes.Count);
        Assert.Equal(DigestB, hashes["b.exe"]);
    }

    [Fact]
    public void TryParse_BinaryMarker_StripsAsterisk()
    {
        var parsed = LauncherUpdateChecksumManifest.TryParse($"{DigestA} *a.zip", out var hashes);

        Assert.True(parsed);
        Assert.True(hashes.ContainsKey("a.zip"));
    }

    [Fact]
    public void TryParse_TabSeparator_IsAccepted()
    {
        var parsed = LauncherUpdateChecksumManifest.TryParse($"{DigestA}\ta.zip", out var hashes);

        Assert.True(parsed);
        Assert.True(hashes.ContainsKey("a.zip"));
    }

    [Fact]
    public void TryParse_UppercaseDigest_IsNormalizedToLowercase()
    {
        var upper = DigestA.ToUpperInvariant();

        var parsed = LauncherUpdateChecksumManifest.TryParse($"{upper}  a.zip", out var hashes);

        Assert.True(parsed);
        Assert.Equal(DigestA, hashes["a.zip"]);
    }

    [Theory]
    [InlineData("0123  a.zip")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz  a.zip")]
    public void TryParse_WhenDigestIsInvalid_Fails(string line)
    {
        Assert.False(LauncherUpdateChecksumManifest.TryParse(line, out _));
    }

    [Fact]
    public void TryParse_WhenLineHasNoSeparator_Fails()
    {
        Assert.False(LauncherUpdateChecksumManifest.TryParse(DigestA, out _));
    }

    [Fact]
    public void TryParse_WhenNameIsDuplicated_Fails()
    {
        var content = $"{DigestA}  a.zip{Environment.NewLine}{DigestB}  a.zip";

        Assert.False(LauncherUpdateChecksumManifest.TryParse(content, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void TryParse_WhenContentIsBlank_Fails(string content)
    {
        Assert.False(LauncherUpdateChecksumManifest.TryParse(content, out _));
    }

    [Fact]
    public void TryGetHash_WhenMissing_ReturnsFalse()
    {
        var parsed = LauncherUpdateChecksumManifest.TryParse($"{DigestA}  a.zip", out var hashes);
        Assert.True(parsed);

        Assert.False(LauncherUpdateChecksumManifest.TryGetHash(hashes, "b.zip", out var hash));
        Assert.Equal("", hash);
    }

    [Fact]
    public void IsHexSha256_ValidatesLengthAndCharacters()
    {
        Assert.True(LauncherUpdateChecksumManifest.IsHexSha256(DigestA));
        Assert.False(LauncherUpdateChecksumManifest.IsHexSha256(null));
        Assert.False(LauncherUpdateChecksumManifest.IsHexSha256(DigestA[..63]));
        Assert.False(LauncherUpdateChecksumManifest.IsHexSha256(DigestA + "0"));
        Assert.False(LauncherUpdateChecksumManifest.IsHexSha256("g" + DigestA[1..]));
    }
}
