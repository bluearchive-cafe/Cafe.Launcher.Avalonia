using System.Text.RegularExpressions;
using Cafe.Launcher.Avalonia.Services.Update;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Updater;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class UpdateHelperCommandTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly Regex UpdaterAssemblyName = new(
        @"<AssemblyName>(?<name>[^<]+)</AssemblyName>",
        RegexOptions.Compiled);

    /// <summary>
    /// helper 的文件名是同一条字面量链：启动器在自己旁边找
    /// <see cref="UpdateHelperCommand.HelperExecutableName"/>，updater 工程按 AssemblyName 产出它，
    /// win-x64 打包步骤把它放进同一个目录。任何一环脱钩都不会编译失败，只会让每台 Windows
    /// 机器都失去应用内更新——静默退化成发布页，比报错更难发现。
    /// </summary>
    [Fact]
    public void HelperExecutableName_MatchesTheUpdaterProjectAndTheWindowsPackagingStep()
    {
        var updaterProject = File.ReadAllText(
            TestRepository.FromRepositoryRoot("src/Cafe.Launcher.Updater/Cafe.Launcher.Updater.csproj"));
        var distributionScript = File.ReadAllText(
            TestRepository.FromRepositoryRoot("scripts/Build-Distribution.ps1"));
        var assemblyName = AssemblyNameOf(updaterProject);

        Assert.Equal(UpdateHelperCommand.HelperExecutableName, $"{assemblyName}.exe");
        Assert.Contains(
            "src/Cafe.Launcher.Updater/Cafe.Launcher.Updater.csproj",
            distributionScript,
            StringComparison.Ordinal);
        // helper 只随 win-x64 包发布，且落到与启动器同一份发布目录里——IsAvailable 找的就是那里。
        Assert.Contains("if ($rid -eq \"win-x64\")", distributionScript, StringComparison.Ordinal);
        Assert.Contains(
            "Invoke-Checked \"dotnet\" @(\"publish\", $updaterProject, \"-c\", \"Release\", \"-r\", \"win-x64\", \"-o\", $publishDir)",
            distributionScript,
            StringComparison.Ordinal);
        // 安装版收编整份发布目录，因此 helper 也随安装包落地。
        Assert.Contains(
            "Source: \"{#PUBLISH_GLOB}\"",
            File.ReadAllText(TestRepository.FromRepositoryRoot("installer/windows/Cafe.Launcher.Avalonia.iss")),
            StringComparison.Ordinal);
    }

    private static string AssemblyNameOf(string projectFile)
    {
        var match = UpdaterAssemblyName.Match(projectFile);
        Assert.True(match.Success, "Cafe.Launcher.Updater.csproj 必须声明 <AssemblyName>。");
        return match.Groups["name"].Value;
    }

    [Theory]
    [InlineData(LauncherUpdateTarget.WindowsInstaller, "installer")]
    [InlineData(LauncherUpdateTarget.WindowsPortable, "portable")]
    public void ModeName_MapsWindowsTargets(LauncherUpdateTarget target, string expected)
    {
        Assert.Equal(expected, UpdateHelperCommand.ModeName(target));
    }

    [Theory]
    [InlineData(LauncherUpdateTarget.ExternalDownload)]
    public void ModeName_WhenTargetHasNoApplyMode_Throws(LauncherUpdateTarget target)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UpdateHelperCommand.ModeName(target));
    }

    [Theory]
    [InlineData(LauncherUpdateTarget.WindowsInstaller)]
    [InlineData(LauncherUpdateTarget.WindowsPortable)]
    public void BuildArguments_RoundTripsThroughTheHelperParser(LauncherUpdateTarget target)
    {
        var arguments = UpdateHelperCommand.BuildArguments(
            target,
            packagePath: "/opt/downloads/pkg",
            installDirectory: "/opt/app",
            executableName: "Cafe.Launcher.Avalonia.exe",
            parentProcessId: 4242,
            expectedSha256: Sha,
            logPath: "/opt/data/update-apply.log");

        Assert.True(UpdaterArguments.TryParse(arguments, out var parsed, out var error), error);
        Assert.NotNull(parsed);
        Assert.Equal(UpdateHelperCommand.ModeName(target), parsed!.Mode.ToString().ToLowerInvariant());
        Assert.Equal("/opt/downloads/pkg", parsed.PackagePath);
        Assert.Equal("/opt/app", parsed.InstallDirectory);
        Assert.Equal("Cafe.Launcher.Avalonia.exe", parsed.ExecutableName);
        Assert.Equal(4242, parsed.ParentProcessId);
        Assert.Equal(Sha, parsed.ExpectedSha256);
        Assert.Equal("/opt/data/update-apply.log", parsed.LogPath);
    }
}
