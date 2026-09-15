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
        var contract = typeof(IGameOperationActivity);
        var members = contract.GetMethods();

        Assert.Contains(members, method => method.Name == "RequestStop" && method.GetParameters().Length == 0);

        var stop = Assert.Single(members, method => method.Name == "StopOperation");
        Assert.Equal(typeof(GameOperationStopIntent), Assert.Single(stop.GetParameters()).ParameterType);

        // 反面不能只看参数（2026-09-15 复核轮）：返回 DownloadStopReason 的方法、类型是它的
        // 属性或事件同样把决策权递到了域外——它们都从「这个接口提到了检查点决策」这一条判。
        var mentions = MentionsCheckpointDecision(contract).ToArray();

        Assert.True(
            mentions.Length == 0,
            "窄视图上出现了检查点决策类型（DownloadStopReason），域外又能替下载模块做决定了："
            + string.Join(", ", mentions));
    }

    /// <summary>
    /// 真接口上现在写不出这种成员（<see cref="DownloadStopReason"/> 在
    /// <c>Features/GameOperations</c> 下，<c>Services</c> 侧引用它编译不过——窄视图与它同侧），
    /// 所以「三种位置都查得到」只能靠夹具证明，否则上面那条断言可能只是恒真。
    /// </summary>
    private interface IScratchActivityWithCheckpointDecisionLeak
    {
        DownloadStopReason LeakByReturnType();

        DownloadStopReason LeakByProperty { get; }

        event Action<DownloadStopReason>? LeakByEvent;
    }

    [Fact]
    public void MentionsCheckpointDecision_DetectsReturnTypesPropertiesAndEvents()
    {
        var mentions = MentionsCheckpointDecision(typeof(IScratchActivityWithCheckpointDecisionLeak)).ToArray();

        Assert.Equal(
            ["LeakByReturnType 的返回类型", "LeakByProperty 的类型", "LeakByEvent 的类型"],
            mentions);
    }

    /// <summary>接口里哪些位置提到了检查点决策类型：返回类型、参数、属性、事件（含泛型实参）。</summary>
    private static IEnumerable<string> MentionsCheckpointDecision(Type contract)
    {
        // 属性访问器（get_/set_）与事件访问器（add_/remove_）由属性/事件那两条覆盖，跳过。
        foreach (var method in contract.GetMethods())
        {
            if (method.IsSpecialName)
            {
                continue;
            }

            if (MentionsCheckpointDecisionType(method.ReturnType))
            {
                yield return $"{method.Name} 的返回类型";
            }

            foreach (var parameter in method.GetParameters())
            {
                if (MentionsCheckpointDecisionType(parameter.ParameterType))
                {
                    yield return $"{method.Name} 的参数 {parameter.Name}";
                }
            }
        }

        foreach (var property in contract.GetProperties())
        {
            if (MentionsCheckpointDecisionType(property.PropertyType))
            {
                yield return $"{property.Name} 的类型";
            }
        }

        foreach (var @event in contract.GetEvents())
        {
            if (MentionsCheckpointDecisionType(@event.EventHandlerType))
            {
                yield return $"{@event.Name} 的类型";
            }
        }
    }

    private static bool MentionsCheckpointDecisionType(Type? type) =>
        type is not null
        && (type == typeof(DownloadStopReason)
            || (type.IsGenericType && type.GetGenericArguments().Any(MentionsCheckpointDecisionType)));

    /// <summary>
    /// 生产源码：扫描域是应用工程全树，排除 <c>bin/obj</c>（那里有生成的 <c>.cs</c>，算成
    /// 「域外命名」是假红），并钉一条反空转基线——本守卫的形状是「offenders 为空」，
    /// 「一个文件都没扫到」与「树是干净的」否则不可区分（2026-09-15 复核轮）。
    /// </summary>
    private static string[] SourceFiles()
    {
        var root = ProjectFile(".");
        var files = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            files.Length >= 230,
            $"只扫到 {files.Length} 个 .cs 文件，低于基线 230——先确认扫描域没退化成子目录或后缀写错。");
        return files;
    }

    private static bool IsBuildArtifact(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var separator = Path.DirectorySeparatorChar;
        return relative.StartsWith($"bin{separator}", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith($"obj{separator}", StringComparison.OrdinalIgnoreCase)
            || relative.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
            || relative.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
    }

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
