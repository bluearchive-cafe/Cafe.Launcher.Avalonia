using System.Collections;
using System.Collections.Generic;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// 收集回调的线程安全列表。进度回调不是单线程抵达的：安装校验阶段由 ≤8 个 worker 上报、
/// 下载阶段由 ≤10 个并发传输上报，而未加锁的 <c>List&lt;T&gt;</c> 在并发 <c>Add</c> 下会丢掉
/// 条目或写重复项（2026-09-15：两条用例因此在 CI 上偶发红——400 文件去重用例与 12 文件
/// 并行校验用例）。断言在 <c>await</c> 之后进行，此时生产者已结束，读走快照。
/// </summary>
public sealed class CallbackRecorder<T> : IReadOnlyList<T>
{
    private readonly List<T> items = [];
    private readonly object gate = new();

    /// <summary>当作 <c>Action&lt;T&gt;</c> 的方法组传给回调。</summary>
    public void Add(T item)
    {
        lock (gate)
        {
            items.Add(item);
        }
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return items.Count;
            }
        }
    }

    public T this[int index]
    {
        get
        {
            lock (gate)
            {
                return items[index];
            }
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        lock (gate)
        {
            return new List<T>(items).GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
