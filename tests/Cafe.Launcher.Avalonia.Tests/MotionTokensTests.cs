using System.Globalization;
using System.Xml.Linq;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class MotionTokensTests
{
    [Fact]
    public void Defaults_ExposeSharedDurationsForMotionConsumers()
    {
        var tokenType = typeof(AnimationTimings).Assembly.GetType(
            "Cafe.Launcher.Avalonia.Helpers.MotionTokens");

        Assert.NotNull(tokenType);
        Assert.Equal(
            TimeSpan.FromMilliseconds(83),
            tokenType.GetField("FasterDuration")?.GetValue(null));
        Assert.Equal(
            TimeSpan.FromMilliseconds(167),
            tokenType.GetField("FastDuration")?.GetValue(null));
        Assert.Equal(
            TimeSpan.FromMilliseconds(250),
            tokenType.GetField("NormalDuration")?.GetValue(null));
        Assert.Equal(
            TimeSpan.FromMilliseconds(333),
            tokenType.GetField("SpatialDuration")?.GetValue(null));
    }

    [Fact]
    public void AppAxamlDurationLadder_MatchesMotionTokens()
    {
        // 双源守卫（2026-08-28 审计）：声明式动画消费 App.axaml 的 x:TimeSpan 阶梯，
        // 代码编排动画消费 MotionTokens 常量；两侧必须逐档一致，防止单边漂移。
        var document = XDocument.Load(ProjectFile("App.axaml"));
        var durations = document
            .Descendants()
            .Where(element => element.Name.LocalName == "TimeSpan")
            .Select(element => (
                Key: element.Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName == "Key")?.Value,
                Value: element.Value.Trim()))
            .Where(entry => entry.Key?.StartsWith("Launcher.Motion.Duration.", StringComparison.Ordinal) == true)
            .ToDictionary(entry => entry.Key!, entry => entry.Value, StringComparer.Ordinal);

        var ladder = new (string Key, TimeSpan Token)[]
        {
            ("Launcher.Motion.Duration.Faster", MotionTokens.FasterDuration),
            ("Launcher.Motion.Duration.Fast", MotionTokens.FastDuration),
            ("Launcher.Motion.Duration.Normal", MotionTokens.NormalDuration),
            ("Launcher.Motion.Duration.Spatial", MotionTokens.SpatialDuration),
        };

        Assert.Equal(ladder.Length, durations.Count);
        foreach (var (key, token) in ladder)
        {
            Assert.True(
                durations.TryGetValue(key, out var raw),
                $"App.axaml is missing motion duration token '{key}'.");
            Assert.Equal(
                token,
                TimeSpan.Parse(raw, CultureInfo.InvariantCulture));
        }
    }

    private static string ProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(
                   directory.FullName,
                   "src",
                   "Cafe.Launcher.Avalonia",
                   "Cafe.Launcher.Avalonia.csproj")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("Project root was not found."),
            "src",
            "Cafe.Launcher.Avalonia",
            relativePath);
    }
}
