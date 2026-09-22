using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// <see cref="GameProcessTracker"/> 的测试工厂。
/// </summary>
/// <remarks>
/// <para>用例里不要写 <c>new GameProcessTracker()</c> 当默认：那个构造绑定真实的进程名扫描
/// （<c>ProcessService.FindRunningExeNamesAsync</c>），于是用例结论取决于开发机上有没有游戏在跑。
/// 本仓库实测过一次这种变色——机器上开着 <c>BlueArchive.exe</c>，提交与卸载路径的用例全部撞上
/// 「游戏正在运行」闸门，把一次真回归埋进了噪声里。默认取 <see cref="None"/>，只有「正在运行」
/// 本身是断言对象的用例才注入 <see cref="Running"/>。</para>
/// <para>注入的是名字探针而不是整个接口替身：句柄侧（<c>Register</c>/<c>HasLiveTrackedProcess</c>/
/// <c>LastExit</c>）仍由真实的 <see cref="GameProcessTracker"/> 实现，替身只接管「扫不到就当没跑」
/// 那条兜底路径。</para>
/// </remarks>
public static class TestGameProcessTracker
{
    /// <summary>报告「没有游戏在跑」——进程状态与用例断言无关时的默认。</summary>
    public static GameProcessTracker None() =>
        new((_, _) => Task.FromResult<IReadOnlyList<string>>([]));

    /// <summary>
    /// 报告给定的进程名在跑。名字是<b>不带扩展名</b>的家族名（<c>BlueArchive</c> 而不是
    /// <c>BlueArchive.exe</c>），呈现时的 <c>.exe</c> 由 <c>GameProcessNames.DescribeForDisplay</c> 补回。
    /// </summary>
    public static GameProcessTracker Running(params string[] runningExeNames) =>
        new((_, _) => Task.FromResult<IReadOnlyList<string>>(runningExeNames));

    /// <summary>
    /// 只在调用方请求的名字集合命中 <paramref name="match"/> 时报「在跑」——用于证明闸门的判据
    /// 确实来自配置声明的家族名，而不是替身自说自话。
    /// </summary>
    public static GameProcessTracker RunningWhenKnownFamilyContains(string match) =>
        new((query, _) => Task.FromResult<IReadOnlyList<string>>(
            query.KnownExeNames
                .Where(name => name.Contains(match, StringComparison.OrdinalIgnoreCase))
                .ToList()));
}
