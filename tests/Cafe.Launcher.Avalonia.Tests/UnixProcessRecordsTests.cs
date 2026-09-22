using System.Text;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class UnixProcessRecordsTests
{
    // Blue Archive 实机启动后的三个进程名字，与 GameProcessNamesTests 同源。
    private static readonly IReadOnlyList<string> BlueArchiveFamily = GameProcessNames.FromLaunchConfiguration(
        "xldr_BlueArchiveOnline_JP_loader_x64",
        ["BlueArchive.exe"]);

    private static readonly UnixGameProcessQuery EmptyQuery = new([], null, null, null);

    [Fact]
    public void ParseArguments_SplitsOnNulAndDropsTheTrailingTerminator()
    {
        Assert.Equal(
            ["umu-run", "/game/BlueArchive.exe"],
            UnixProcessRecordParser.ParseArguments(Bytes("umu-run\0/game/BlueArchive.exe\0")));
    }

    [Fact]
    public void ParseArguments_KeepsEmptyEntriesAndSpacesInsideArguments()
    {
        Assert.Equal(["a", "", "b c"], UnixProcessRecordParser.ParseArguments(Bytes("a\0\0b c\0")));
        Assert.Empty(UnixProcessRecordParser.ParseArguments(Bytes("")));
    }

    [Fact]
    public void ParseEnvironment_ParsesNulSeparatedKeyValuePairs()
    {
        var environment = UnixProcessRecordParser.ParseEnvironment(
            Bytes("WINEPREFIX=/home/u/pfx\0GAMEID=umu-bluearchive\0"));

        Assert.Equal("/home/u/pfx", environment["WINEPREFIX"]);
        Assert.Equal("umu-bluearchive", environment["GAMEID"]);
    }

    [Fact]
    public void ParseEnvironment_IgnoresEntriesWithoutAKeyOrSeparator()
    {
        var environment = UnixProcessRecordParser.ParseEnvironment(Bytes("=x\0NOEQUALS\0EMPTY=\0"));

        Assert.False(environment.ContainsKey(""));
        Assert.False(environment.ContainsKey("NOEQUALS"));
        Assert.Equal("", environment["EMPTY"]);
    }

    [Fact]
    public void ParseComm_TrimsTheTrailingNewline()
    {
        Assert.Equal("xldr_BlueArchive", UnixProcessRecordParser.ParseComm(Bytes("xldr_BlueArchive\n")));
    }

    [Fact]
    public void ParseMappedFiles_ExtractsDedupesAndDropsPseudoPaths()
    {
        var maps = Bytes(
            "7f00-7f10 r-xp 00000000 08:01 123 /home/u/game/BlueArchive.exe\n"
            + "7f10-7f20 rw-p 00000000 00:00 0 [heap]\n"
            + "7f20-7f30 r--p 00000000 08:01 456 /usr/lib/libc.so.6\n"
            + "7f30-7f40 r--p 00000000 08:01 123 /home/u/game/BlueArchive.exe\n");

        Assert.Equal(
            ["/home/u/game/BlueArchive.exe", "/usr/lib/libc.so.6"],
            UnixProcessRecordParser.ParseMappedFiles(maps));
    }

    [Fact]
    public void ParseMappedFiles_UnescapesSpaces()
    {
        Assert.Equal(
            ["/home/u/My Games/BlueArchive.exe"],
            UnixProcessRecordParser.ParseMappedFiles(
                Bytes("7f00-7f10 r-xp 00000000 08:01 123 /home/u/My\\040Games/BlueArchive.exe\n")));
    }

    [Fact]
    public void Match_WhenOwnershipMarkerMatches_IdentifiesByMarkerAndShowsTheUntruncatedName()
    {
        var record = Record(
            comm: "wine64-preloader",
            arguments: ["wine64-preloader", "C:\\game\\BlueArchive.exe"],
            environment: [(UnixGameProcessMatcher.OwnershipMarkerKey, "bluearchive-jp")]);

        var match = UnixGameProcessMatcher.Match(
            record,
            EmptyQuery with { KnownNames = BlueArchiveFamily, GameId = "bluearchive-jp" });

        Assert.NotNull(match);
        Assert.Equal(UnixProcessMatchSignal.OwnershipMarker, match.Signal);
        Assert.Equal("BlueArchive", match.DisplayName);
    }

    [Fact]
    public void Match_WhenOwnershipMarkerValueDiffers_DoesNotMatch()
    {
        var record = Record(environment: [(UnixGameProcessMatcher.OwnershipMarkerKey, "other-game")]);

        Assert.Null(UnixGameProcessMatcher.Match(record, EmptyQuery with { GameId = "bluearchive-jp" }));
    }

    [Fact]
    public void Match_WhenTheOwnershipMarkerIsPresentWithoutAGameId_IdentifiesByMarker()
    {
        var record = Record(
            comm: "winedevice.exe",
            environment: [(UnixGameProcessMatcher.OwnershipMarkerKey, "any-game")]);

        var match = UnixGameProcessMatcher.Match(record, EmptyQuery);

        Assert.NotNull(match);
        Assert.Equal(UnixProcessMatchSignal.OwnershipMarker, match.Signal);
    }

    [Fact]
    public void Match_WhenWinePrefixMatches_IdentifiesByPrefixIgnoringATrailingSlash()
    {
        var record = Record(environment: [("WINEPREFIX", "/home/u/pfx/bluearchive-jp/")]);

        var match = UnixGameProcessMatcher.Match(
            record,
            EmptyQuery with { PrefixPath = "/home/u/pfx/bluearchive-jp" });

        Assert.NotNull(match);
        Assert.Equal(UnixProcessMatchSignal.WinePrefix, match.Signal);
    }

    [Fact]
    public void Match_WhenWinePrefixDiffers_DoesNotMatch()
    {
        var record = Record(environment: [("WINEPREFIX", "/home/u/pfx/other-game")]);

        Assert.Null(UnixGameProcessMatcher.Match(
            record,
            EmptyQuery with { PrefixPath = "/home/u/pfx/bluearchive-jp" }));
    }

    [Fact]
    public void Match_WhenAMappedFileIsUnderTheInstallDirectory_IdentifiesByMapping()
    {
        var record = Record(mappedFiles: ["/usr/lib/libc.so.6", "/games/BlueArchive/BlueArchive.exe"]);

        var match = UnixGameProcessMatcher.Match(
            record,
            EmptyQuery with { InstallDirectory = "/games/BlueArchive/" });

        Assert.NotNull(match);
        Assert.Equal(UnixProcessMatchSignal.MappedInstallFile, match.Signal);
        Assert.Equal("BlueArchive", match.DisplayName);
    }

    [Fact]
    public void Match_WhenAMappedFileSitsBesideTheInstallDirectory_DoesNotMatch()
    {
        var record = Record(mappedFiles: ["/games/BlueArchiveBackup/BlueArchive.exe"]);

        Assert.Null(UnixGameProcessMatcher.Match(
            record,
            EmptyQuery with { InstallDirectory = "/games/BlueArchive" }));
    }

    [Fact]
    public void Match_WhenOnlyTheTruncatedCommIsInFamily_ReportsTheWeakSignal()
    {
        var record = Record(comm: "BlueArchive.exe");

        var match = UnixGameProcessMatcher.Match(record, EmptyQuery with { KnownNames = BlueArchiveFamily });

        Assert.NotNull(match);
        Assert.Equal(UnixProcessMatchSignal.TruncatedNameFamily, match.Signal);
        Assert.Equal("BlueArchive", match.DisplayName);
    }

    [Fact]
    public void Match_WhenNothingIdentifiesTheProcess_ReturnsNull()
    {
        // 误报反例：无关长名、同前缀不同族、被截断到对不上的宿主名，以及 argv 只是提到游戏路径
        // （判定故意不看 argv，见设计稿 §3）。
        Assert.Null(UnixGameProcessMatcher.Match(
            Record(comm: "explorer"),
            EmptyQuery with { KnownNames = BlueArchiveFamily }));
        Assert.Null(UnixGameProcessMatcher.Match(
            Record(comm: "BlueArchiveData"),
            EmptyQuery with { KnownNames = BlueArchiveFamily }));
        Assert.Null(UnixGameProcessMatcher.Match(
            Record(comm: "xldr_BlueArchive"),
            EmptyQuery with { KnownNames = BlueArchiveFamily }));
        Assert.Null(UnixGameProcessMatcher.Match(
            Record(comm: "vim", arguments: ["vim", "/games/BlueArchive/BlueArchive.exe"]),
            EmptyQuery with { KnownNames = BlueArchiveFamily }));
    }

    [Fact]
    public void Match_WhenNoQueryKeysAreGiven_ReturnsNull()
    {
        Assert.Null(UnixGameProcessMatcher.Match(
            Record(comm: "xldr_BlueArchiveOnline_JP_loader_x64"),
            EmptyQuery));
    }

    private static UnixProcessRecord Record(
        string comm = "proc",
        string[]? arguments = null,
        string[]? mappedFiles = null,
        (string Key, string Value)[]? environment = null) =>
        new(
            ProcessId: 1,
            ParentProcessId: 0,
            Comm: comm,
            Arguments: arguments ?? [],
            MappedFiles: mappedFiles ?? [],
            Environment: environment?.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal));

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
}
