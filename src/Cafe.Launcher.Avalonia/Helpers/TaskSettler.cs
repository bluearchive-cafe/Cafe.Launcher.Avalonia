using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 有界「等到稳定」（D9）：快照当前在飞任务并等待它；等待期间若槽里换了新的一次
/// （引用变化）则继续等，直到引用稳定或预算耗尽。两个设置侧「保存前等预览/取色落定」
/// 的循环此前各持一份同构实现，收拢到这里。null 视为没有在飞任务，立即返回。
/// </summary>
public static class TaskSettler
{
    /// <summary>
    /// Waits until the task returned by <paramref name="current"/> stops being replaced by a
    /// newer one, or until <paramref name="budget"/> elapses (then returns and the caller
    /// proceeds with the current state).
    /// </summary>
    public static async Task WaitAsync(Func<Task?> current, TimeSpan budget)
    {
        using var settleBudget = new CancellationTokenSource(budget);
        while (true)
        {
            var pending = current();
            if (pending is null)
            {
                return;
            }

            try
            {
                await pending.WaitAsync(settleBudget.Token);
            }
            catch (OperationCanceledException) when (settleBudget.IsCancellationRequested)
            {
                return;
            }

            if (ReferenceEquals(pending, current()))
            {
                return;
            }
        }
    }
}
