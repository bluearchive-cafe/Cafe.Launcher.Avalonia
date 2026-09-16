using System;
using System.Linq;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// <see cref="ToastSeverityProfile"/> 的完备性与区分度守卫。
/// </summary>
/// <remarks>
/// 枚举可以有未命名值，编译器无法为 <c>switch</c> 表达式要求穷尽（CS8524），所以「新增一档严重度
/// 必须同时给出图标与颜色键」只能由测试来守。颜色键能否真的解析成画刷由
/// <c>ConverterHeadlessTests</c> 覆盖（资源字典只在无头环境里可用）。
/// </remarks>
public sealed class ToastSeverityProfileTests
{
    [Fact]
    public void For_EveryDeclaredSeverity_ReturnsAnIconAndABrushKey()
    {
        foreach (var severity in Enum.GetValues<ToastSeverity>())
        {
            var profile = ToastSeverityProfile.For(severity);

            Assert.False(
                string.IsNullOrWhiteSpace(profile.IconKind),
                $"{severity} has no icon kind.");
            Assert.StartsWith("Launcher.Color.", profile.BrushResourceKey, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void For_DistinctSeverities_UseDistinctIconsAndBrushKeys()
    {
        var profiles = Enum.GetValues<ToastSeverity>()
            .Select(severity => ToastSeverityProfile.For(severity))
            .ToList();

        // 两档严重度共用同一个图标或颜色，用户就分不出它们的区别。
        Assert.Equal(
            profiles.Count,
            profiles.Select(profile => profile.IconKind).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            profiles.Count,
            profiles.Select(profile => profile.BrushResourceKey).Distinct(StringComparer.Ordinal).Count());
    }
}
