using System.Text.RegularExpressions;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

/// <summary>
/// 立「已保存设置只有一条写入路线」这件事的结构守卫：settings.json 的写入在日常开发里
/// 很容易被顺手照抄成「读盘、改字段、落盘」，而漏掉回写编辑器正是「用户的下一次保存把
/// 别人的字段写回旧值」的成因。行为守卫（<see cref="SavedSettingsWriterTests"/>）只能钉住
/// 已知的几条路线，这里钉住的是「没有第五条」。
/// </summary>
/// <remarks>
/// 判据按「声明」而不是按某一个拼写认人（2026-09-15 复核轮更正）：旧写法要求文件里出现字面量
/// <c>LauncherSettingsService settingsService</c>，于是以 <c>SettingsService</c> 持有它的
/// <c>DownloadSessionContext</c>（record 主构造参数）既不在声明表里、也不可见；调用扫描同样
/// 大小写敏感，<c>SettingsService.SaveAsync(</c> 可以整条绕过。现在持有者按类型声明（任何名字）
/// 与容器解析两种形态识别，写入按「本文件里指向设置服务的标识符」识别——换个名字、换个大小写
/// 都还在判据里。
/// </remarks>
public sealed class SettingsWriteOwnershipTests
{
    /// <summary>
    /// 允许持有 <see cref="LauncherSettingsService"/> 的生产文件。这些是读取方与组合根；
    /// 新增一个持有者就要在这里登记——登记时必然会看到本测试的另一半断言：写入只能经
    /// <see cref="ISavedSettingsWriter"/>。
    /// </summary>
    private static readonly string[] SettingsServiceHolders =
    [
        "src/Cafe.Launcher.UI/Composition/LauncherPresentationServiceCollectionExtensions.cs",
        "src/Cafe.Launcher.UI/Composition/LauncherPresentationServiceRegistrations.cs",
        "src/Cafe.Launcher.UI/Features/Diagnostics/DebugViewModel.cs",
        "src/Cafe.Launcher.UI/Features/GameOperations/DownloadSession.cs",
        "src/Cafe.Launcher.UI/Features/GameOperations/DownloadSessionContext.cs",
        "src/Cafe.Launcher.UI/Features/GameOperations/GameDownloadService.cs",
        "src/Cafe.Launcher.UI/Features/ResourcePanel/ResourcePanelUidService.cs",
        "src/Cafe.Launcher.UI/Features/Settings/SettingsViewModel.cs",
        "src/Cafe.Launcher.UI/Features/Shell/ShellLifecycle.cs",
        "src/Cafe.Launcher.UI/LauncherPresentationSession.cs",
        "src/Cafe.Launcher.UI/ViewModels/MainWindowViewModel.cs",
        "src/Cafe.Launcher.Core/Composition/LauncherCoreServiceCollectionExtensions.cs",
        "src/Cafe.Launcher.Core/Services/LauncherCoreService.cs",
    ];

    private const string WriterFile = "src/Cafe.Launcher.Core/Services/SavedSettingsWriter.cs";

    /// <summary>
    /// 持有形态：字段/参数/局部变量声明（名字任意），以及从容器解析。
    /// 只认某一个拼写会让换个名字的持有者隐身。
    /// </summary>
    private static readonly Regex HolderPattern = new(
        @"I?LauncherSettingsService\s+[A-Za-z_]|Get(?:Required)?Service<I?LauncherSettingsService>",
        RegexOptions.Compiled);

    /// <summary>
    /// 一个文件里指向设置服务的标识符：<c>LauncherSettingsService x</c> 的 <c>x</c>，
    /// 以及 <c>var x = …GetRequiredService&lt;LauncherSettingsService&gt;()</c> 的 <c>x</c>。
    /// </summary>
    private static readonly Regex AliasPattern = new(
        @"LauncherSettingsService\s+(?<alias>[A-Za-z_]\w*)"
        + @"|(?<alias>[A-Za-z_]\w*)\s*=\s*[^;\r\n]*Get(?:Required)?Service<LauncherSettingsService>",
        RegexOptions.Compiled);

    [Fact]
    public void LauncherSettingsService_SaveAsyncIsCalledByTheWriterOnly()
    {
        var callers = SourceFiles()
            .Where(file => WritesSettingsThroughAnAlias(File.ReadAllText(file)))
            .Select(RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([WriterFile], callers);
    }

    [Fact]
    public void LauncherSettingsService_HoldersAreDeclared()
    {
        var holders = SourceFiles()
            .Where(file => HolderPattern.IsMatch(File.ReadAllText(file)))
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

    [Theory]
    [InlineData("private readonly LauncherSettingsService settingsService;")]
    [InlineData("LauncherSettingsService settingsService,")]
    [InlineData("LauncherSettingsService SettingsService,")]
    [InlineData("var settingsService = provider.GetRequiredService<LauncherSettingsService>();")]
    [InlineData("sp.GetService<LauncherSettingsService>()")]
    public void HolderPattern_DetectsEveryHoldingForm(string source)
    {
        Assert.Matches(HolderPattern, source);
    }

    [Theory]
    [InlineData("/// <see cref=\"Cafe.Launcher.Core.Services.LauncherSettingsService\"/>")]
    [InlineData("/// <c>LauncherSettingsService.NormalizeSettings</c>")]
    public void HolderPattern_IgnoresDocumentationMentions(string source)
    {
        // 只在注释里提一句不算持有：这两种形态在 Models/ 与 AGENTS.md 里到处都是。
        Assert.DoesNotMatch(HolderPattern, source);
    }

    [Theory]
    [InlineData("LauncherSettingsService settingsService;\nsettingsService.SaveAsync(x);")]
    [InlineData("LauncherSettingsService SettingsService,\nSettingsService.SaveAsync(x);")]
    [InlineData("var settingsService = provider.GetRequiredService<LauncherSettingsService>();\n"
        + "settingsService.SaveAsync(x);")]
    public void SettingsWrite_IsDetectedWhateverTheHolderIsCalled(string source)
    {
        Assert.True(WritesSettingsThroughAnAlias(source));
    }

    [Fact]
    public void SettingsWrite_IsNotReportedForAnotherServiceWithTheSameMethodName()
    {
        // 防误报：检查点也是 SaveAsync，持有设置服务的文件里本来就有这类调用。
        Assert.False(WritesSettingsThroughAnAlias(
            "LauncherSettingsService settingsService;\ncheckpointStore.SaveAsync(x);"));
        Assert.False(WritesSettingsThroughAnAlias("checkpointStore.SaveAsync(x);"));
    }

    /// <summary>
    /// 该文件是否通过本文件里任一指向设置服务的标识符调用了 <c>SaveAsync</c>。
    /// 别名取自同文件的声明，因此换个名字不隐身，也不会把别的服务的同名方法算进来。
    /// </summary>
    private static bool WritesSettingsThroughAnAlias(string text) =>
        AliasesOf(text).Any(alias => Regex.IsMatch(
            text,
            $@"\b{Regex.Escape(alias)}\s*\.\s*SaveAsync\s*\(",
            RegexOptions.IgnoreCase));

    private static string[] AliasesOf(string text) =>
        AliasPattern
            .Matches(text)
            .Select(match => match.Groups["alias"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// 生产源码（host + Core）。<c>bin</c> 与 <c>obj</c> 必须排除：生成代码（AssemblyInfo、XAML 的
    /// <c>.g.cs</c>）与旧输出副本会让「只有写入方调用 SaveAsync」变成假红，而判据本身并不需要它们。
    /// 程序集拆分后写入方住在 Core，路径一律相对仓库根，两个工程的文件不会互相遮蔽。
    /// </summary>
    private static IEnumerable<string> SourceFiles() =>
        new[] { TestRepository.HostPath, TestRepository.CorePath, TestRepository.PresentationPath }
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsBuildArtifact(file));

    private static bool IsBuildArtifact(string path) =>
        Path.GetRelativePath(TestRepository.Root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    private static string RelativePath(string absolutePath) =>
        Path.GetRelativePath(TestRepository.Root, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
}
