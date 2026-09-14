using System.Text.RegularExpressions;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 立「已保存设置只有一条写入路线」这件事的结构守卫：settings.json 的写入在日常开发里
/// 很容易被顺手照抄成「读盘、改字段、落盘」，而漏掉回写编辑器正是「用户的下一次保存把
/// 别人的字段写回旧值」的成因。行为守卫（<see cref="SavedSettingsWriterTests"/>）只能钉住
/// 已知的几条路线，这里钉住的是「没有第五条」。
/// </summary>
public sealed class SettingsWriteOwnershipTests
{
    /// <summary>
    /// 允许持有 <see cref="LauncherSettingsService"/> 的生产文件。这些是读取方与组合根；
    /// 新增一个持有者就要在这里登记——登记时必然会看到本测试的另一半断言：写入只能经
    /// <see cref="ISavedSettingsWriter"/>。
    /// </summary>
    private static readonly string[] SettingsServiceHolders =
    [
        "Features/Diagnostics/DebugViewModel.cs",
        "Features/GameOperations/DownloadSession.cs",
        "Features/GameOperations/GameDownloadService.cs",
        "Features/ResourcePanel/ResourcePanelUidService.cs",
        "Features/Settings/SettingsViewModel.cs",
        "Features/Shell/ShellLifecycle.cs",
        "Services/LauncherCoreService.cs",
        "ViewModels/MainWindowViewModel.cs"
    ];

    private const string WriterFile = "Services/SavedSettingsWriter.cs";

    [Fact]
    public void LauncherSettingsService_SaveAsyncIsCalledByTheWriterOnly()
    {
        var callers = SourceFiles()
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                @"settingsService\s*\.\s*SaveAsync\s*\("))
            .Select(RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([WriterFile], callers);
    }

    [Fact]
    public void LauncherSettingsService_HoldersAreDeclared()
    {
        var holders = SourceFiles()
            .Where(file => File.ReadAllText(file).Contains(
                "LauncherSettingsService settingsService",
                StringComparison.Ordinal))
            .Select(RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        // 写入方自身也持有设置服务；此外的每个持有者都在上面的声明表里。
        Assert.Equal(
            SettingsServiceHolders
                .Append(WriterFile)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray(),
            holders);
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(
            ProjectFile("."),
            "*.cs",
            SearchOption.AllDirectories);

    private static string RelativePath(string absolutePath) =>
        Path.GetRelativePath(ProjectFile("."), absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    private static string ProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectFile = Path.Combine(
                directory.FullName,
                "src",
                "Cafe.Launcher.Avalonia",
                "Cafe.Launcher.Avalonia.csproj");
            if (File.Exists(projectFile))
            {
                return Path.Combine(
                    Path.GetDirectoryName(projectFile)!,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The application project was not found.");
    }
}
