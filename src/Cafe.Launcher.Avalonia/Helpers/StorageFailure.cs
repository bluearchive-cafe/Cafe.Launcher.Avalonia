using System;
using System.IO;
using System.Text.Json;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 「哪些文件系统失败算可恢复」的唯一出处。
/// </summary>
/// <remarks>
/// <para>这条判据此前在十余个 catch 过滤里各写一遍。逐处重写的问题不在于行数，而在于
/// <b>刻意的加宽与漂移长得一模一样</b>：调用方无法从一行
/// <c>is IOException or UnauthorizedAccessException</c> 判断它是本策略，还是有人为某个
/// 具体场景临时放宽的。收成具名判据后，加宽必须显式绕开它，读者一眼可辨。</para>
/// <para>路径探测类调用方（<c>DiskSpaceService</c>）刻意容忍更多失败类型（非法/不支持的
/// 路径参数），继续写自己的过滤而不是走这里——那是不同的策略，不是本策略的实例。</para>
/// </remarks>
internal static class StorageFailure
{
    /// <summary>读写用户数据时由文件系统抛出、且原因在程序控制之外的失败。</summary>
    public static bool IsRecoverable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    /// <summary>同上，外加「磁盘上的 JSON 已损坏」——只有读自身持久化文件的服务需要。</summary>
    public static bool IsRecoverableOrInvalidJson(Exception exception) =>
        exception is JsonException or IOException or UnauthorizedAccessException;
}
