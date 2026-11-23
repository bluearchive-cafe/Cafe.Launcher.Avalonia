using System.Collections.Generic;

namespace Cafe.Launcher.Core.Models;

/// <summary>读取 <c>DeviceOption</c> 的稳定结果代码；调用方按代码呈现文案。</summary>
public enum GameDeviceOptionReadStatus
{
    /// <summary>文件存在，且所有已知字段都符合已校准的 1.74 结构。</summary>
    Supported,

    /// <summary>文件不存在；不把它当作损坏。</summary>
    Missing,

    /// <summary>文件存在，但当前用户无法读取。</summary>
    Unreadable,

    /// <summary>内容不是单个合法 JSON 对象。</summary>
    InvalidJson,

    /// <summary>必需字段缺失，或已知枚举出现未校准的值；由 <see cref="GameDeviceOptionReadResult.InvalidField"/> 指出字段。</summary>
    UnsupportedSchema,

    /// <summary>字段值越界，例如音量不在 0–1；由 <see cref="GameDeviceOptionReadResult.InvalidField"/> 指出字段。</summary>
    InvalidValues
}

/// <summary>一次只读解析的结果；失败时不返回部分选项。</summary>
/// <param name="Status">稳定结果代码。</param>
/// <param name="Options">仅 <see cref="GameDeviceOptionReadStatus.Supported"/> 时非空。</param>
/// <param name="UnknownFieldNames">文件中不属于已校准集合的顶层字段，按序数排序；用于发现版本漂移。</param>
/// <param name="InvalidField">导致失败的字段名；没有具体字段时为 <see langword="null"/>。</param>
public sealed record GameDeviceOptionReadResult(
    GameDeviceOptionReadStatus Status,
    GameDeviceOptions? Options,
    IReadOnlyList<string> UnknownFieldNames,
    string? InvalidField)
{
    /// <summary>是否得到可展示的选项快照。</summary>
    public bool IsSupported => Status == GameDeviceOptionReadStatus.Supported;
}
