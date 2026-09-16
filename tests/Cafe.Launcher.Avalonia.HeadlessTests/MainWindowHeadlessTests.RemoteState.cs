using System;
using System.Threading;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// 远内容用例的公共装配：卡片开关、取消令牌与「显示窗口并跑一次调度」在每个用例里都一样，
/// 只有 payload 不同，故收在这里，替掉各用例手抄的那段尾。
/// </summary>
public sealed partial class MainWindowHeadlessTests
{
    /// <summary>
    /// 构造一个远内容状态并交给视图模型，随后显示窗口并推进调度器。
    /// <paramref name="configure"/> 只填该用例需要的 payload；未填的部分保持模型默认值
    /// （例如 <c>OperationsBannerList</c> 默认是空列表而不是 null）。
    /// </summary>
    private static void ApplyRemoteState(TestContext context, Action<LauncherRemoteState>? configure = null)
    {
        var state = new LauncherRemoteState();
        configure?.Invoke(state);

        context.ViewModel.RemoteContent.Apply(
            state,
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
    }
}
