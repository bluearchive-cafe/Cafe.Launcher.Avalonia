using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class PathMiddleEllipsisTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Games\YostarGames\BlueArchive_JP")]
    public void MiddleEllipsize_PathWithinBudget_ReturnedUnchanged(string? path)
    {
        Assert.Equal(path ?? string.Empty, PathMiddleEllipsis.MiddleEllipsize(path));
    }

    [Fact]
    public void MiddleEllipsize_ExactlyAtBudget_ReturnedUnchanged()
    {
        var path = @"C:\Games\YostarGames\BlueArchive_JP";

        Assert.Equal(35, path.Length);
        Assert.Equal(path, PathMiddleEllipsis.MiddleEllipsize(path, 35));
    }

    [Fact]
    public void MiddleEllipsize_LongPath_KeepsHeadAndLastTwoSegments()
    {
        var path = @"E:\Repos\Cafe.Launcher.Avalonia\src\Cafe.Launcher.Avalonia\bin\Debug\YostarGames\BlueArchive_JP";

        var display = PathMiddleEllipsis.MiddleEllipsize(path);

        Assert.StartsWith(@"E:\", display);
        Assert.EndsWith(@"YostarGames\BlueArchive_JP", display);
        Assert.Contains(PathMiddleEllipsis.Ellipsis, display);
        Assert.True(display.Length <= PathMiddleEllipsis.DefaultMaxCharacters);
    }

    [Fact]
    public void MiddleEllipsize_WholeMiddleSegmentFits_KeepsItUnbroken()
    {
        // 预算内装得下的中间段整段保留（不断段）：head 3 + `aaa\` 4 + … 1 + tail 9 = 17 ≤ 20。
        var display = PathMiddleEllipsis.MiddleEllipsize(@"C:\aaa\bbbbbbbbbbbb\cccc\ddd", 20);

        Assert.Equal(@"C:\aaa\…\cccc\ddd", display);
    }

    [Fact]
    public void MiddleEllipsize_NoMiddleSegmentFits_ShowsHeadEllipsisTail()
    {
        var display = PathMiddleEllipsis.MiddleEllipsize(@"C:\aaaaaaaaaaaa\bbbb\cccc\dddd", 16);

        Assert.Equal(@"C:\…\cccc\dddd", display);
    }

    [Fact]
    public void MiddleEllipsize_TwoSegmentTailExceedsBudget_FallsBackToLastSegment()
    {
        // head 3 + 末两段 25 + … 1 = 29 > 24 → 末尾退为一段（20）：3 + 20 + 1 = 24 恰好入预算。
        var display = PathMiddleEllipsis.MiddleEllipsize(
            @"C:\aa\bbbbbbbbbbbbbbbbbbbb\cccc\dddddddddddddddddddd", 24);

        Assert.Equal(@"C:\…\dddddddddddddddddddd", display);
    }

    [Fact]
    public void MiddleEllipsize_HeadAndAnyTailExceedBudget_FallsBackToCharacterMiddle()
    {
        // 仅 3 段（无中间段可舍）且首段+省略号+末段超出预算：退化为字符级中切。
        var path = @"C:\aaaaaaaaaaaaaaaaaaaaaaaa\bbbbbbbbbbbbbbbbbbbbbbbb";

        var display = PathMiddleEllipsis.MiddleEllipsize(path, 16);

        Assert.Equal(16, display.Length);
        Assert.Equal(path[..7] + PathMiddleEllipsis.Ellipsis + path[^8..], display);
    }

    [Fact]
    public void MiddleEllipsize_UnixStylePath_KeepsRootAndGameSegment()
    {
        // head `/` 1 + 末两段 `games/BlueArchive_JP` 20 + … 1 = 22 > 20 → 末尾退为一段。
        var display = PathMiddleEllipsis.MiddleEllipsize("/home/user/games/BlueArchive_JP", 20);

        Assert.Equal("/…/BlueArchive_JP", display);
    }

    [Fact]
    public void MiddleEllipsize_DefaultBudgetMatchesConstant()
    {
        var path = @"E:\Repos\Cafe.Launcher.Avalonia\src\Cafe.Launcher.Avalonia\bin\Debug\YostarGames\BlueArchive_JP";

        Assert.Equal(
            PathMiddleEllipsis.MiddleEllipsize(path, PathMiddleEllipsis.DefaultMaxCharacters),
            PathMiddleEllipsis.MiddleEllipsize(path));
    }
}
