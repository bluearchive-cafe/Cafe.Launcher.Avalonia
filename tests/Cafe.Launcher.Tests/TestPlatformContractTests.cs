using System.Text.Json;
using System.Xml.Linq;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

/// <summary>
/// MTP 平台代际与 xUnit 扩展 API 的代际是两个独立契约：Headless 保留 xUnit 3，
/// 两套仍必须共用 MTP 2 的报告、覆盖率与挂起扩展。
/// </summary>
public sealed class TestPlatformContractTests
{
    [Fact]
    public void GlobalJson_SelectsTheNativeMtpRunner()
    {
        using var global = JsonDocument.Parse(File.ReadAllText(TestRepository.FromRepositoryRoot("global.json")));

        Assert.Equal("Microsoft.Testing.Platform", global.RootElement.GetProperty("test").GetProperty("runner").GetString());
    }

    [Theory]
    [InlineData("Cafe.Launcher.Tests", "4.")]
    [InlineData("Cafe.Launcher.HeadlessTests", "3.")]
    public void TestProjects_KeepTheirFrameworkVersionOnTheSameMtpGeneration(string projectName, string frameworkVersionPrefix)
    {
        var project = XDocument.Load(TestRepository.FromRepositoryRoot($"tests/{projectName}/{projectName}.csproj"));
        Assert.Equal("Exe", project.Descendants("OutputType").Single().Value);
        Assert.Null(project.Descendants("OutputType").Single().Attribute("Condition"));
        Assert.Equal("true", project.Descendants("UseMicrosoftTestingPlatformRunner").Single().Value);

        using var dependencies = JsonDocument.Parse(File.ReadAllText(
            TestRepository.FromRepositoryRoot($"tests/{projectName}/packages.lock.json")));
        var graph = dependencies.RootElement.GetProperty("dependencies").GetProperty("net10.0");

        Assert.StartsWith(frameworkVersionPrefix, graph.GetProperty("xunit.v3.extensibility.core").GetProperty("resolved").GetString()!, StringComparison.Ordinal);
        var platformVersion = graph.GetProperty("Microsoft.Testing.Platform").GetProperty("resolved").GetString();
        Assert.NotNull(platformVersion);
        Assert.StartsWith("2.", platformVersion, StringComparison.Ordinal);
        foreach (var extension in new[] { "Microsoft.Testing.Extensions.HangDump", "Microsoft.Testing.Extensions.TrxReport" })
        {
            Assert.Equal(platformVersion, graph.GetProperty(extension).GetProperty("resolved").GetString());
        }

        Assert.True(graph.TryGetProperty("Microsoft.Testing.Extensions.CodeCoverage", out var codeCoverage));
        Assert.StartsWith("18.", codeCoverage.GetProperty("resolved").GetString()!, StringComparison.Ordinal);
        Assert.False(graph.TryGetProperty("coverlet.MTP", out _));
        Assert.False(graph.TryGetProperty("Microsoft.NET.Test.Sdk", out _));
        Assert.False(graph.TryGetProperty("xunit.runner.visualstudio", out _));
        Assert.False(graph.TryGetProperty("coverlet.msbuild", out _));
    }
}
