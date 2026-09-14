using System.Reflection;
using System.Text.RegularExpressions;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 「停止的决策权在游戏操作域」的结构守卫：域外的调用方只表达意图
/// （<see cref="GameOperationStopIntent"/>），检查点去留由域翻译成
/// <see cref="DownloadStopReason"/>。行为守卫（<c>GameOperationsViewModelTests</c> 的两条
/// 意图映射用例）只能钉住已知路径，这里钉住的是「没有第二处命名那个原因」。
/// </summary>
public sealed class GameOperationStopOwnershipTests
{
    /// <summary>策略词表的归属地：只有游戏操作域内部可以命名停止原因。</summary>
    private const string FeatureDirectory = "Features/GameOperations/";

    [Fact]
    public void DownloadStopReason_IsNamedOnlyInsideTheGameOperationsFeature()
    {
        var offenders = SourceFiles()
            .Where(file => !RelativePath(file).StartsWith(FeatureDirectory, StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\bDownloadStopReason\b"))
            .Select(RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "这些文件命名了下载停止原因，但检查点去留是游戏操作域内策略。"
            + "请改为表达意图（GameOperationStopIntent），由域翻译；确需在域内使用时放进 "
            + FeatureDirectory + "："
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// 窄视图的形状：用户手势（可能先确认）与「按意图立即停止」是两个入口，
    /// 且接口上不出现检查点决策本身——否则域外又能替下载模块做决定了。
    /// </summary>
    [Fact]
    public void IGameOperationActivity_ExposesIntents_NotTheCheckpointDecision()
    {
        var members = typeof(IGameOperationActivity).GetMethods();

        Assert.Contains(members, method => method.Name == "RequestStop" && method.GetParameters().Length == 0);

        var stop = Assert.Single(members, method => method.Name == "StopOperation");
        Assert.Equal(typeof(GameOperationStopIntent), Assert.Single(stop.GetParameters()).ParameterType);

        Assert.DoesNotContain(
            members,
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(DownloadStopReason)));
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(ProjectFile("."), "*.cs", SearchOption.AllDirectories);

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
