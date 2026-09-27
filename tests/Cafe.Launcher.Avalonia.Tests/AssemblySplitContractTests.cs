using System.Xml.Linq;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Composition;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.Tests;

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
        // Core 的源命名空间已收口为 Cafe.Launcher.Core.*；回退到 Cafe.Launcher.Avalonia.* 会让
        // 一个物理上属于 Core 的类型看起来属于宿主，边界从源码上读不出来。
        var separator = Path.DirectorySeparatorChar;
        var offenders = Directory.GetFiles(TestRepository.CorePath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadAllLines(path)
                .Where(line => line.StartsWith("namespace Cafe.Launcher.Avalonia", StringComparison.Ordinal))
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
        services.AddLauncherCore(identity, dataRoot);
        services.AddLauncherCore(identity, dataRoot);

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
        var presentation = XDocument.Load(TestRepository.FromPresentationRoot("Cafe.Launcher.Avalonia.UI.csproj"));
        var host = XDocument.Load(TestRepository.FromHostRoot("Cafe.Launcher.Avalonia.csproj"));

        Assert.Contains(ProjectReferences(core), reference =>
            reference.EndsWith("Cafe.Launcher.Updater.Core\\Cafe.Launcher.Updater.Core.csproj", StringComparison.Ordinal));
        Assert.Contains(ProjectReferences(presentation), reference =>
            reference.EndsWith("Cafe.Launcher.Core\\Cafe.Launcher.Core.csproj", StringComparison.Ordinal));
        Assert.Contains(ProjectReferences(host), reference =>
            reference.EndsWith("Cafe.Launcher.Avalonia.UI\\Cafe.Launcher.Avalonia.UI.csproj", StringComparison.Ordinal));
        Assert.DoesNotContain(ProjectReferences(core), reference =>
            reference.Contains("Avalonia.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(ProjectReferences(presentation), reference =>
            reference.EndsWith("Cafe.Launcher.Avalonia.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionAssemblyInfo_GrantsInternalsOnlyToTestAssemblies()
    {
        var propertiesDirectories = Directory.GetDirectories(
            TestRepository.FromRepositoryRoot("src"),
            "Properties",
            SearchOption.AllDirectories);

        foreach (var propertiesDirectory in propertiesDirectories)
        {
            var assemblyInfo = Path.Combine(propertiesDirectory, "AssemblyInfo.cs");
            if (!File.Exists(assemblyInfo))
            {
                continue;
            }

            var source = File.ReadAllText(assemblyInfo);
            foreach (System.Text.RegularExpressions.Match friend in System.Text.RegularExpressions.Regex.Matches(
                         source,
                         "InternalsVisibleTo\\(\\\"(?<name>[^\\\"]+)\\\"\\)"))
            {
                var name = friend.Groups["name"].Value;
                Assert.EndsWith("Tests", name, StringComparison.Ordinal);
            }
        }
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
}
