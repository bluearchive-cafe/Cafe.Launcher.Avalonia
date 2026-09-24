using System;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// 可编程时间戳源替身：手动推进 <see cref="Now"/>，把需要时间流逝的节奏逻辑
/// （节流器、进度累积器、下载执行器）的测试变成确定性断言。注入形态是
/// <see cref="GetTimestamp"/> 方法组（<c>Func&lt;long&gt;</c> 时钟缝）。
/// </summary>
public sealed class MutableTimestampClock
{
    public MutableTimestampClock(long initial = 0) => Now = initial;

    public long Now { get; set; }

    public long GetTimestamp() => Now;
}
