using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 默认设置的发布渠道只能由宿主注入的 <see cref="LauncherBuildIdentity"/> 决定。
/// 迁移期间这里一度改成 <c>Assembly.GetEntryAssembly()</c> 反射：入口程序集不是启动器时
/// （测试宿主、崩溃报告进程、将来的宿主）预发布构建会静默退回 Stable，且没有任何测试能看见。
/// </summary>
public sealed class LauncherSettingsDefaultsTests
{
    [Fact]
    public void CreateDefaults_ForAPrereleaseIdentity_DefaultsToTheBetaChannel()
    {
        var settings = LauncherSettings.CreateDefaults(
            new LauncherBuildIdentity("1.1.0-beta.8", "abc1234", "2026-09-27 17:06", "Release"));

        Assert.Equal(UpdateChannels.Beta, settings.UpdateChannel);
    }

    [Fact]
    public void CreateDefaults_ForAStableIdentity_KeepsTheStableChannel()
    {
        var settings = LauncherSettings.CreateDefaults(
            new LauncherBuildIdentity("1.1.0", "abc1234", "2026-09-27 17:06", "Release"));

        Assert.Equal(UpdateChannels.Stable, settings.UpdateChannel);
    }

    [Fact]
    public void CreateDefaults_WithoutAnIdentity_DoesNotGuessFromTheEntryAssembly()
    {
        // 不传 identity 只允许出现在「与渠道无关」的调用点：它们必须拿到确定值，
        // 而不是「谁碰巧是入口程序集」——测试宿主与启动器在反射实现下会得到不同结果。
        Assert.Equal(UpdateChannels.Stable, LauncherSettings.CreateDefaults().UpdateChannel);
    }

    [Fact]
    public void IsPrerelease_ReadsTheSemVerSuffix()
    {
        Assert.True(new LauncherBuildIdentity("1.1.0-beta.8", "", "", "Release").IsPrerelease);
        Assert.False(new LauncherBuildIdentity("1.1.0", "", "", "Release").IsPrerelease);
        Assert.False(new LauncherBuildIdentity("1.1.0+20f2ba0", "", "", "Release").IsPrerelease);
    }

    [Fact]
    public void LauncherSettingsSource_DoesNotReflectOverTheEntryAssembly()
    {
        var source = File.ReadAllText(TestRepository.FromCoreRoot("Models/LauncherSettings.cs"));

        Assert.DoesNotContain("GetEntryAssembly", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ParameterlessCreateDefaults_HasOnlyTheDeclaredChannelIrrelevantCallSites()
    {
        // 渠道相关的默认值必须拿到注入的 identity；只有「默认值里与渠道无关的字段」才允许
        // 省略参数（当前只有首启动效偏好）。新调用点要么传 identity，要么在此登记并注明理由。
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 只取动效偏好，与更新渠道无关
            Path.Combine(TestRepository.PresentationPath, "Features", "Shell", "ShellStartup.cs")
        };

        var callSites = ProductionSources()
            .Where(path => File.ReadAllText(path)
                .Contains("LauncherSettings.CreateDefaults()", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            declared.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            callSites.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
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
}
