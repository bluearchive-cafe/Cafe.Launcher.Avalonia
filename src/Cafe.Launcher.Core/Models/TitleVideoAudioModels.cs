using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cafe.Launcher.Core.Models;

/// <summary>
/// Blue Archive 客户端的 <c>TitleVideoAudioSaveData</c> 文件内容。
/// </summary>
/// <remarks>
/// 该文件记录当前账号已打开/可选的标题画面 PV，以及最后一个打开的条目。
/// <c>DeviceOption.TitleVideoAudioId</c> 只是选中条目的 <see cref="TitleVideoAudioEntry.Id"/>，
/// 不包含视频路径；本模型用于把 ID 解析成 PV 名称。
/// </remarks>
public sealed record TitleVideoAudioSelection(
    IReadOnlyList<TitleVideoAudioEntry> Saved,
    long LastOpenedId,
    int Version);

/// <summary>一条已记录的标题画面 PV；<see cref="Id"/> 是 <c>ScenarioResourceInfoExcel</c> 的行 ID。</summary>
public sealed record TitleVideoAudioEntry(long Id, string VideoPath, long BgmId, string BgmPath);

/// <summary>读取 <c>TitleVideoAudioSaveData</c> 的稳定结果代码。</summary>
public enum TitleVideoAudioReadStatus
{
    /// <summary>文件存在且结构有效。</summary>
    Supported,

    /// <summary>文件不存在；通常表示尚未打开过标题画面选择。</summary>
    Missing,

    /// <summary>文件存在但当前用户无法读取。</summary>
    Unreadable,

    /// <summary>内容不是有效的 JSON 或缺少必需字段。</summary>
    InvalidJson
}

/// <summary>一次 <c>TitleVideoAudioSaveData</c> 只读解析结果。</summary>
public sealed record TitleVideoAudioReadResult(
    TitleVideoAudioReadStatus Status,
    TitleVideoAudioSelection? Selection);

/// <summary>1.74 实机 <c>ScenarioResourceInfoExcel</c> 中标题画面 PV 的稳定映射。</summary>
/// <remarks>
/// <see cref="DisplayOrder"/> 是 <c>PVDisplayOrder</c>，1–4 对应前四个 PV，5 是 4.5th，
/// 6–8 对应 5th–7th；<see cref="Id"/> 是表主键，和显示顺序无关。
/// </remarks>
public sealed record TitleVideoAudioPvDefinition(long Id, int DisplayOrder, string VideoPath);

/// <summary>标题画面 PV 的校准表；数据来自 1.74 <c>ScenarioResourceInfoExcel</c>。</summary>
public static class TitleVideoAudioCatalog
{
    /// <summary>随机选择的专用 ID。</summary>
    public const long RandomId = -1;

    /// <summary>按 <c>PVDisplayOrder</c> 排列的已知 PV。</summary>
    public static IReadOnlyList<TitleVideoAudioPvDefinition> Pvs { get; } =
    [
        new(1, 1, "Video/title"),
        new(8, 2, "Video/title_2nd_1"),
        new(9, 3, "Video/title_3rd_1"),
        new(2, 4, "Video/title_4nd_2"),
        new(4, 5, "Video/title_4nd_Ep"),
        new(6, 6, "Video/title_5th_1"),
        new(10, 7, "Video/title_6th_1"),
        new(14, 8, "Video/title_7th_1")
    ];

    /// <summary>按表主键查找；未知 ID 返回 <see langword="null"/>。</summary>
    public static TitleVideoAudioPvDefinition? FindById(long id)
    {
        foreach (var pv in Pvs)
        {
            if (pv.Id == id)
            {
                return pv;
            }
        }

        return null;
    }

    /// <summary>按视频路径查找；未知路径返回 <see langword="null"/>。</summary>
    public static TitleVideoAudioPvDefinition? FindByVideoPath(string? videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            return null;
        }

        foreach (var pv in Pvs)
        {
            if (string.Equals(pv.VideoPath, videoPath, StringComparison.OrdinalIgnoreCase))
            {
                return pv;
            }
        }

        return null;
    }

    /// <summary>把 <c>PVDisplayOrder</c> 转成界面标签；4.5th 是唯一的非整数。</summary>
    public static string DisplayLabel(int displayOrder) => displayOrder switch
    {
        1 => "1",
        2 => "2",
        3 => "3",
        4 => "4",
        5 => "4.5",
        6 => "5",
        7 => "6",
        8 => "7",
        _ => displayOrder.ToString(CultureInfo.InvariantCulture)
    };
}
