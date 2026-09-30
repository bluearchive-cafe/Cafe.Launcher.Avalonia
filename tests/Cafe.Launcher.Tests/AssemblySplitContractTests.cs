using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Composition;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Auth;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

public sealed class AssemblySplitContractTests
{
    [Fact]
    public void CoreProject_DoesNotReferenceAvaloniaOrPresentationPackages()
    {
        var project = XDocument.Load(TestRepository.FromCoreRoot("Cafe.Launcher.Core.csproj"));
        var packageNames = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.DoesNotContain(packageNames, package => package.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain(packageNames, package => package.StartsWith("MarkView", StringComparison.Ordinal));
        Assert.DoesNotContain(packageNames, package => package.StartsWith("Material.Icons", StringComparison.Ordinal));
        // MVVM 工具包属于表现层：Core 的设置模型只实现 BCL 的 INotifyPropertyChanged
        // （Models/SettingsModel.cs），引用它会让「无表现依赖」只剩名义。
        Assert.DoesNotContain(packageNames, package => package.StartsWith("CommunityToolkit", StringComparison.Ordinal));

        var separator = Path.DirectorySeparatorChar;
        var coreSources = Directory.GetFiles(TestRepository.CorePath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.DoesNotContain(coreSources, path =>
            File.ReadAllText(path).Contains("using Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain(coreSources, path =>
            File.ReadAllText(path).Contains("using CommunityToolkit", StringComparison.Ordinal));
    }

    [Fact]
    public void CoreSources_UseOnlyTheCoreNamespaces()
    {
        // Core 的源命名空间只能是 Cafe.Launcher.Core.*；回退到 Cafe.Launcher.* 会让一个物理上
        // 属于 Core 的类型看起来属于宿主，边界从源码上读不出来。宿主的根命名空间就是
        // Cafe.Launcher，所以判据必须按「Core 前缀」而不是「宿主前缀」写。
        var separator = Path.DirectorySeparatorChar;
        var offenders = Directory.GetFiles(TestRepository.CorePath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadAllLines(path)
                .Where(line => line.StartsWith("namespace Cafe.Launcher", StringComparison.Ordinal)
                    && !line.StartsWith("namespace Cafe.Launcher.Core", StringComparison.Ordinal))
                .Select(line => $"{Path.GetFileName(path)}: {line.Trim()}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SettingsPersistenceAndDataRoot_AreOwnedByCore()
    {
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/LauncherDataRoot.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/LauncherSettingsService.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Helpers/AtomicJsonFileStore.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/RemoteHttpUrlValidator.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/BestHttpCookieLibraryService.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Models/LauncherApiContracts.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Models/LauncherReleaseResponse.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/RetryPolicy.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/RemoteBodyReader.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/ResponseBodyReader.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/RemoteHttpRequestService.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/RemoteHttpTransport.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/IRemoteHttpClientLeaseSource.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/PatchUrlGroupService.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/LauncherApiClient.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Helpers/HttpClientLease.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/LauncherDataRoot.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/LauncherSettingsService.cs")));
        Assert.False(File.Exists(TestRepository.FromPresentationRoot("Helpers/AtomicJsonFileStore.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/RemoteHttpUrlValidator.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/BestHttpCookieLibraryService.cs")));
        Assert.False(File.Exists(TestRepository.FromPresentationRoot("Models/LauncherApiContracts.cs")));
        Assert.False(File.Exists(TestRepository.FromPresentationRoot("Models/LauncherReleaseResponse.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/RetryPolicy.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/RemoteBodyReader.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/ResponseBodyReader.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/RemoteHttpRequestService.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/RemoteHttpTransport.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/IRemoteHttpClientLeaseSource.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/PatchUrlGroupService.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/LauncherApiClient.cs")));
        Assert.False(File.Exists(TestRepository.FromPresentationRoot("Helpers/HttpClientLease.cs")));
    }

    [Fact]
    public void BuildIdentity_ReadsTheSuppliedHostAssembly()
    {
        var identity = LauncherBuildIdentity.FromAssembly(typeof(Constants.BuildInfo).Assembly);

        Assert.Equal(Constants.BuildInfo.LauncherVersion, identity.LauncherVersion);
        Assert.Equal(Constants.BuildInfo.CommitSha, identity.CommitSha);
        Assert.Equal(Constants.BuildInfo.BuildTime, identity.BuildTime);
    }

    [Fact]
    public void BuildIdentity_FromAnAssemblyWithoutHostMetadata_DegradesInsteadOfThrowing()
    {
        // 只有 WinExe 注入 CommitSha/BuildTime 元数据；类库程序集读它必须退回 unknown/空串，
        // 而不是抛异常或把别人的版本当成产品版本（Core 默认的 1.0.0 就是这么暴露出来的）。
        var identity = LauncherBuildIdentity.FromAssembly(typeof(LauncherBuildIdentity).Assembly);

        Assert.Equal("unknown", identity.CommitSha);
        Assert.Equal("", identity.BuildTime);
        Assert.False(string.IsNullOrWhiteSpace(identity.LauncherVersion));
    }

    [Fact]
    public void AddLauncherCore_IsIdempotent_SoRepeatedCompositionKeepsOneRegistrationPerService()
    {
        var services = new ServiceCollection();
        var identity = new LauncherBuildIdentity("1.2.3", "abc1234", "2026-09-27", "Debug");
        var dataRoot = new LauncherDataRoot(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        // 重复登记必须收敛成一份：组合根在测试与辅助宿主里会被反复调用。
        services.AddLauncherCore(identity, dataRoot, LauncherProfiles.BlueArchiveJapan, LauncherProfiles.Cafe);
        services.AddLauncherCore(identity, dataRoot, LauncherProfiles.BlueArchiveJapan, LauncherProfiles.Cafe);

        using var provider = services.BuildServiceProvider();
        Assert.Same(identity, provider.GetRequiredService<LauncherBuildIdentity>());
        Assert.Same(dataRoot, provider.GetRequiredService<LauncherDataRoot>());
        Assert.NotNull(provider.GetRequiredService<Crc64Service>());
        Assert.NotNull(provider.GetRequiredService<LocalInstallationStateStore>());
        Assert.NotNull(provider.GetRequiredService<AuthorizationHeaderFactory>());
        Assert.NotNull(provider.GetRequiredService<RemoteHttpUrlValidator>());
        Assert.NotNull(provider.GetRequiredService<BestHttpCookieLibraryService>());
        Assert.NotNull(provider.GetRequiredService<PatchUrlGroupService>());
        Assert.NotNull(provider.GetRequiredService<LauncherSettingsService>());
        Assert.Single(provider.GetServices<Crc64Service>());
        Assert.Single(provider.GetServices<LauncherSettingsService>());
    }

    [Fact]
    public void ProjectReferences_FollowTheOneWayAssemblyGraph()
    {
        var core = XDocument.Load(TestRepository.FromCoreRoot("Cafe.Launcher.Core.csproj"));
        var presentation = XDocument.Load(TestRepository.FromPresentationRoot("Cafe.Launcher.UI.csproj"));
        var host = XDocument.Load(TestRepository.FromHostRoot("Cafe.Launcher.csproj"));

        Assert.Contains(ProjectReferences(core), reference =>
            reference.EndsWith("Cafe.Launcher.Updater.Core\\Cafe.Launcher.Updater.Core.csproj", StringComparison.Ordinal));
        Assert.Contains(ProjectReferences(presentation), reference =>
            reference.EndsWith("Cafe.Launcher.Core\\Cafe.Launcher.Core.csproj", StringComparison.Ordinal));
        Assert.Contains(ProjectReferences(host), reference =>
            reference.EndsWith("Cafe.Launcher.UI\\Cafe.Launcher.UI.csproj", StringComparison.Ordinal));
        Assert.DoesNotContain(ProjectReferences(core), reference =>
            reference.Contains("Avalonia.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(ProjectReferences(presentation), reference =>
            reference.EndsWith("Cafe.Launcher.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionAssemblies_GrantInternalsOnlyToTestAssemblies()
    {
        // 两种声明形状都要覆盖：源码里的 [assembly: InternalsVisibleTo("…")] 与 csproj/props/targets
        // 里的 <InternalsVisibleTo Include="…" />——只扫 AssemblyInfo.cs 的守卫会静默漏掉后者。
        // 反空转基线：一条 friend 都没扫到时必须红，否则换个声明形状就能让本守卫永真。
        var friendNames = new List<string>();
        foreach (var source in Directory.EnumerateFiles(
                     TestRepository.FromRepositoryRoot("src"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            AddMatches(friendNames, File.ReadAllText(source), "InternalsVisibleTo\\(\\\"(?<name>[^\\\"]+)\\\"\\)");
        }

        foreach (var projectFile in ProjectFilePaths())
        {
            AddMatches(
                friendNames,
                File.ReadAllText(projectFile),
                "<InternalsVisibleTo[^>]*Include=\\\"(?<name>[^\\\"]+)\\\"");
        }

        Assert.True(
            friendNames.Count >= 2,
            $"只扫到 {friendNames.Count} 条 friend 声明，本守卫已空转（声明形状变了而守卫没跟上）。");
        Assert.All(friendNames, name => Assert.EndsWith("Tests", name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Core 仍是「实现默认 public」，因此这里把「Models/ 之外的顶层 public class」钉成显式声明集：
    /// 新增或移除都必须同时改这张表，收窄只会前进，不会因为顺手加一个 public 实现而静默回退。
    /// 判据：<c>src/Cafe.Launcher.Core</c> 下 <c>Models/</c> 之外的顶层 class 声明（文件作用域
    /// 命名空间让顶层类型顶格，按行首匹配因此天然排除嵌套类型）。<c>Models/</c> 是数据模型的豁免域。
    /// </summary>
    [Fact]
    public void CorePublicImplementationsOutsideModels_AreTheDeclaredSet()
    {
        string[] declared =
        [
            "AtomicJsonFileStore",
            "BestHttpCookieLibraryService",
            "CrashOriginExtensions",
            "DirectoryDeletionPlan", // Returned by DirectoryTreeDeleter; UI executes it after the game-process gate.
            "DirectorySizeProbe",
            "DirectoryTreeDeleter",
            "DirectoryWriteProbe",
            "ExecutableLocator",
            "FileDownloadService",
            "FileSizeFormatter",
            "FlexibleBoolConverter",
            "GSettingsCli",
            "GameCompatibilityPaths",
            "GamePathValidator",
            "GameProcessNames",
            "GameProcessTracker",
            "GraphicsInfoProbe",
            "HttpClientLease",
            "JsonDefaults",
            "LauncherConstants",
            "LauncherCoreServiceCollectionExtensions",
            "LauncherDataRoot",
            "LauncherLog",
            "LauncherPaths",
            "LauncherProfiles",
            "LauncherUpdateCheckResult",
            "LeaseBackedDownloadTransportSource",
            "LinuxProcessScanner",
            "LinuxProcessSnapshot",
            "LogEntryReader",
            "NoticeStateService",
            "OfficialHashService",
            "ProcessService",
            "ProtonBuildDiscovery",
            "RemoteBodyReader",
            "RemoteBodyTooLargeException",
            "RemoteHttpRequestService",
            "ResponseBodyReader",
            "RetryPolicy",
            "RuntimeVersionProbe",
            "ShellFolderOpener",
            "StorageFailure",
            "SystemAnimationSettingsProvider",
            "UnifiedLogger",
            "UnixProcessRecordParser",
            "VersionComparer"
        ];
        const string publicClassPattern =
            "^public\\s+(?:(?:sealed|abstract|static|partial|readonly|unsafe)\\s+)*class\\s+(?<name>\\w+)";

        var actual = Directory
            .EnumerateFiles(TestRepository.CorePath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}Models{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(File.ReadAllLines)
            .Select(line => Regex.Match(line, publicClassPattern))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(declared.OrderBy(name => name, StringComparer.Ordinal).ToArray(), actual);
    }

    /// <summary>
    /// 表现层程序集的公开面同样是显式声明集。Core 早有这条守卫，UI 一直没有——于是拆分后
    /// 「宿主点名了哪些 UI 内部实现」只能靠读源码发现，而 ADR-042 的收窄目标正是把宿主
    /// 对表现层的接触面压到窄接口/门面上。声明集把这个接触面摊开：新增公开类型必须同时改表，
    /// 收窄（改 internal）也必须改表，两边都不会静默发生。
    /// </summary>
    /// <remarks>
    /// 判据与 Core 那条同形：文件作用域命名空间让顶层类型顶格，按行首匹配因此天然排除嵌套类型；
    /// 只比较顶层类型名（不含命名空间），所以文件内搬动不触发本守卫。
    /// </remarks>
    [Fact]
    public void PresentationPublicSurface_IsTheDeclaredSet()
    {
        string[] declared =
        [
            // 宿主在 pre-DI 阶段必须亲手装配的崩溃路径：Program 与 CrashReportApp 直接 new
            // 这些类型（身份由宿主注入），所以它们是公开的实现类型而不是窄接口。
            "CrashReport",
            "CrashReportBootstrap",
            "CrashReportStore",
            "CrashReportWindow",
            "FatalCrashService",
            "IFatalCrashService",
            "ICrashReporterLauncher",

            // 宿主与表现层之间的生命周期/登记门面（宿主只经这些入口接触表现层）。
            "LauncherPresentationServiceCollectionExtensions",
            "LauncherPresentationServiceRegistrations",
            "LauncherPresentationSession",

            // XAML/宿主登记直接引用的表现层类型。
            "BannerCarouselTransition",
            "LocalizationKeys",
            "LocalizationService",
            "LocalizedTextCatalog",
            "LocalizationFailureEventArgs",
            "LanguageFontFamilyService",
            "SystemCultureSnapshot",

            // 测试经 InternalsVisibleTo 之外仍需公开的数据表面。
            "CriticalErrorInfo",
            "DownloadStopReason",
            "ErrorHandlingOptions",
            "GameOperationKind",
            "GameOperationStage",
            "LanguageOption",
            "ModalKind",
            "SelectableOption",
            "ToastDuration",
            "ToastSeverity",
            "UninstallScope"
        ];
        const string publicTypePattern =
            "^public\\s+(?:(?:sealed|abstract|static|partial|readonly|unsafe)\\s+)*(?:class|record|interface|enum|struct)\\s+(?<name>\\w+)";

        var actual = Directory
            .EnumerateFiles(TestRepository.PresentationPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(File.ReadAllLines)
            .Select(line => Regex.Match(line, publicTypePattern))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // 反空转基线：扫不到任何公开类型时本守卫没有意义（正则或目录约定变了）。
        Assert.True(
            actual.Length >= 20,
            $"只扫到 {actual.Length} 个表现层公开类型，本守卫已空转（目录或正则约定变了）。");

        Assert.Equal(declared.OrderBy(name => name, StringComparer.Ordinal).ToArray(), actual);
    }

    /// <summary>
    /// 两个测试工程的程序集并行化声明必须与它们各自的 xUnit 主版本配对。
    /// xUnit 4 把 <c>[assembly: CollectionBehavior]</c> 的 <c>DisableTestParallelization</c>
    /// 标成 obsolete 且**不可调用**，而 HeadlessTests 被 Avalonia.Headless.XUnit 的精确版本
    /// 钉在 3.2.2、只能用这个旧形状。两侧不匹配的后果不是编译失败，而是并行化在升级那一刻
    /// 静默恢复——共享静态状态（AnimationTimings、Application 级资源、生产静态改写）会开始
    /// 互相污染。（另注意 xUnit 4 默认的 collections 模式本就允许不同 collection 并行。）
    /// </summary>
    [Theory]
    [InlineData("tests/Cafe.Launcher.Tests", "Parallelization")]
    [InlineData("tests/Cafe.Launcher.HeadlessTests", "CollectionBehavior")]
    public void TestProjects_DeclareTheAssemblyParallelizationFormTheirXunitMajorSupports(
        string projectDirectory,
        string expectedAttribute)
    {
        var projectFile = $"{projectDirectory}/{Path.GetFileName(projectDirectory)}.csproj";
        var project = XDocument.Load(TestRepository.FromRepositoryRoot(projectFile));
        var reference = project.Descendants("PackageReference")
            .Single(element => element.Attribute("Include")?.Value == "xunit.v3");

        // 有效版本有三个来源，按优先级：工程内 VersionOverride → 工程内 Version → 中央版本清单
        // （Directory.Packages.props）。单元工程正是第三种（它没有覆盖），Headless 是第一种。
        var xunitVersion = reference.Attribute("VersionOverride")?.Value
            ?? reference.Attribute("Version")?.Value
            ?? XDocument.Load(TestRepository.FromRepositoryRoot("Directory.Packages.props"))
                .Descendants("PackageVersion")
                .Single(element => element.Attribute("Include")?.Value == "xunit.v3")
                .Attribute("Version")?.Value;

        Assert.True(
            xunitVersion is not null,
            $"{projectFile} 与 Directory.Packages.props 都读不到 xunit.v3 的版本：本守卫无法判定 xUnit 主版本。");

        var major = int.Parse(xunitVersion!.Split('.')[0], CultureInfo.InvariantCulture);
        var assemblyInfo = File.ReadAllText(TestRepository.FromRepositoryRoot($"{projectDirectory}/AssemblyInfo.cs"));

        // 配对关系：3.x 只能用 CollectionBehavior，4.x 只能用 Parallelization。
        var expected = major >= 4 ? "Parallelization" : "CollectionBehavior";
        Assert.Equal(expectedAttribute, expected);
        Assert.Contains($"assembly: {expectedAttribute}(", assemblyInfo, StringComparison.Ordinal);

        var obsoleteForm = expectedAttribute == "Parallelization" ? "CollectionBehavior" : "Parallelization";
        Assert.DoesNotContain($"assembly: {obsoleteForm}(", assemblyInfo, StringComparison.Ordinal);
    }

    [Fact]
    public void Projects_ResolveCoreNamespacesWithExplicitUsings()
    {
        // 迁移期为了让命名空间收口不必全仓改写 using，四个工程曾在 csproj 里用
        // <Using Include="Cafe.Launcher.Core.*" /> 过渡解析；调用方收到窄接口后应改回显式 using。
        // 这条守卫挡住「顺手再加一个项目级 using」——它会让 Core 命名空间再次变成隐式依赖。
        var offenders = new[]
            {
                "src/Cafe.Launcher/Cafe.Launcher.csproj",
                "src/Cafe.Launcher.UI/Cafe.Launcher.UI.csproj",
                "tests/Cafe.Launcher.Tests/Cafe.Launcher.Tests.csproj",
                "tests/Cafe.Launcher.HeadlessTests/Cafe.Launcher.HeadlessTests.csproj"
            }
            .Where(relative => File.ReadAllText(TestRepository.FromRepositoryRoot(relative))
                .Contains("<Using Include=\"Cafe.Launcher.Core", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CompositionRoot_RegistersCoreFirstAndOnlyOnce()
    {
        var hostSource = File.ReadAllText(TestRepository.FromHostRoot("App.axaml.cs"));
        var compositionRoot = TestRepository.FromHostRoot("Composition/ServiceConfiguration.cs");
        var rootSource = File.ReadAllText(compositionRoot);

        // 只匹配调用点：文档注释里也会出现这些方法名，按第一次出现定位会落进注释。
        var coreRegistration = rootSource.IndexOf("services.AddLauncherCore(", StringComparison.Ordinal);
        var firstSingleton = rootSource.IndexOf("services.AddSingleton", StringComparison.Ordinal);
        Assert.True(coreRegistration >= 0, "组合根必须先调用 AddLauncherCore。");
        Assert.True(firstSingleton > coreRegistration,
            "Core 必须先注册，确保容器反向释放时 UI 先于 Core 释放。");

        Assert.True(
            hostSource.Contains("AddLauncherServices", StringComparison.Ordinal),
            "宿主必须调用组合根 AddLauncherServices。");
        // 表现层登记整体归组合根：宿主只调 AddLauncherServices 这一个入口，
        // 不再自己拼表现层（那会让宿主重新命名 UI 内部类型）。
        Assert.False(
            hostSource.Contains("AddLauncherPresentation", StringComparison.Ordinal),
            "宿主不得直接登记表现层：表现层登记属于组合根。");
        var presentationCall = rootSource.IndexOf("services.AddLauncherPresentationServices(", StringComparison.Ordinal);
        Assert.True(presentationCall > coreRegistration, "表现层必须在 Core 之后注册。");
        Assert.True(
            rootSource.Contains("services.AddLauncherPresentation()", StringComparison.Ordinal),
            "组合根必须登记表现层生命周期门面。");

        // 数据根与 Core 登记只允许一个调用点：宿主若再调一次 AddLauncherCore，顺序契约就分裂成
        // 两处，并迫使宿主自己解析进程数据根（ADR-025 的单点解析），Core 也只能再提供
        // ForCurrentProcess 兜底——那正是 TestUserDataIsolationTests 要挡住的形状。
        var callSites = ProductionSources()
            .Where(path => File.ReadAllText(path).Contains(".AddLauncherCore(", StringComparison.Ordinal))
            .Select(Path.GetFullPath)
            .ToArray();
        Assert.Equal([Path.GetFullPath(compositionRoot)], callSites);
    }

    private static IEnumerable<string> ProductionSources() =>
        new[] { TestRepository.HostPath, TestRepository.CorePath, TestRepository.PresentationPath }
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));

    private static string[] ProjectReferences(XDocument project) => project
        .Descendants("ProjectReference")
        .Select(element => element.Attribute("Include")?.Value)
        .OfType<string>()
        .ToArray();

    private static void AddMatches(List<string> names, string text, string pattern)
    {
        foreach (Match match in Regex.Matches(text, pattern))
        {
            names.Add(match.Groups["name"].Value);
        }
    }

    /// <summary>工程与属性文件：friend 声明也可能写在这里，而不是 <c>Properties/AssemblyInfo.cs</c>。</summary>
    private static IEnumerable<string> ProjectFilePaths() =>
        new[] { "src", "tests" }
            .Select(TestRepository.FromRepositoryRoot)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(TestRepository.Root, "*.props"))
            .Concat(Directory.EnumerateFiles(TestRepository.Root, "*.targets"))
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsBuildOutput(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
