using System.Xml.Linq;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Composition;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;
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

        var coreSources = Directory.GetFiles(TestRepository.CorePath, "*.cs", SearchOption.AllDirectories);
        Assert.DoesNotContain(coreSources, path =>
            File.ReadAllText(path).Contains("using Avalonia", StringComparison.Ordinal));
    }

    [Fact]
    public void SettingsPersistenceAndDataRoot_AreOwnedByCore()
    {
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/LauncherDataRoot.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Services/LauncherSettingsService.cs")));
        Assert.True(File.Exists(TestRepository.FromCoreRoot("Helpers/AtomicJsonFileStore.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/LauncherDataRoot.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Services/LauncherSettingsService.cs")));
        Assert.False(File.Exists(TestRepository.FromHostRoot("Helpers/AtomicJsonFileStore.cs")));
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
    public void LauncherMessage_Create_SeparatesPresentationArgumentsFromDiagnostics()
    {
        var message = LauncherMessage.Create(
            LauncherMessageCode.DownloadFailed,
            "HTTP 503 from example.invalid",
            "manifest.json",
            "503");

        Assert.Equal(LauncherMessageCode.DownloadFailed, message.Code);
        Assert.Equal(["manifest.json", "503"], message.Arguments);
        Assert.Equal("HTTP 503 from example.invalid", message.DiagnosticDetail);
    }

    [Fact]
    public void AddLauncherCore_RegistersTheExtractedBackendSliceExactlyOnce()
    {
        var services = new ServiceCollection();
        var identity = new LauncherBuildIdentity("1.2.3", "abc1234", "2026-09-27", "Debug");

        services.AddLauncherCore(identity);
        services.AddLauncherCore(identity);

        using var provider = services.BuildServiceProvider();
        Assert.Same(identity, provider.GetRequiredService<LauncherBuildIdentity>());
        Assert.NotNull(provider.GetRequiredService<Crc64Service>());
        Assert.NotNull(provider.GetRequiredService<LocalInstallationStateStore>());
        Assert.NotNull(provider.GetRequiredService<AuthorizationHeaderFactory>());
        Assert.Single(provider.GetServices<Crc64Service>());
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
    public void HostRegistersCoreBeforePresentationComposition()
    {
        var source = File.ReadAllText(TestRepository.FromHostRoot("App.axaml.cs"));
        var coreRegistration = source.IndexOf("AddLauncherCore", StringComparison.Ordinal);
        var presentationRegistration = source.IndexOf("AddLauncherPresentation", StringComparison.Ordinal);

        Assert.True(coreRegistration >= 0, "宿主必须先调用 AddLauncherCore。");
        Assert.True(presentationRegistration > coreRegistration,
            "Core 必须先注册，确保容器反向释放时 UI 先于 Core 释放。");
    }

    private static string[] ProjectReferences(XDocument project) => project
        .Descendants("ProjectReference")
        .Select(element => element.Attribute("Include")?.Value)
        .OfType<string>()
        .ToArray();
}
