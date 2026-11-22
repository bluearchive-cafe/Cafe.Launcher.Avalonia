using System;

namespace Cafe.Launcher.Core.Models;

/// <summary>需要全局独占游戏文件写入位的操作类型；下载同时涵盖安装与更新。</summary>
public enum GameFileOperationKind
{
    /// <summary>安装或更新所需的下载与文件应用旅程。</summary>
    Download,
    /// <summary>校验并修复游戏文件的完整旅程。</summary>
    Repair,
    /// <summary>删除所属游戏安装的完整旅程；执行期间拒绝真正退出。</summary>
    Uninstall
}

/// <summary>文件任务即时准入结果的稳定代码；拒绝不会排队或开始业务操作。</summary>
public enum GameFileOperationAdmissionCode
{
    /// <summary>已取得全局独占令牌。</summary>
    Allowed,
    /// <summary>另一文件旅程尚未完整收尾；同游戏重入也受此限制。</summary>
    Busy,
    /// <summary>退出准备已原子关闭文件任务准入。</summary>
    ShuttingDown,
    /// <summary>目标不属于内置支持清单；不回退到其他游戏。</summary>
    UnknownGame,
    /// <summary>此游戏的设置提交尚未完成；不使用提交前的配置开始文件任务。</summary>
    SettingsChanging
}

/// <summary>一次文件任务的固定身份；选择变化不能修改目标，操作标识区分同一游戏的不同旅程。</summary>
public sealed record GameFileOperationInfo(Guid OperationId, string GameId, GameFileOperationKind Kind);

/// <summary>
/// 一次文件任务持有的独占令牌。只有整个旅程（包括检查点、错误处理与最终刷新）完成后，
/// 才由最外层 finally 释放；暂停、取消请求已发送或运行标志变为 false 都不是释放屏障。
/// </summary>
public interface IGameFileOperationLease : IDisposable
{
    /// <summary>本令牌固定拥有的目标与操作身份；重复释放不会影响其他令牌。</summary>
    GameFileOperationInfo Operation { get; }
}

/// <summary>即时准入决定；成功时仅携带独占令牌，阻挡信息仅描述决定时已存在的任务。</summary>
public sealed class GameFileOperationAdmission
{
    internal GameFileOperationAdmission(
        GameFileOperationAdmissionCode code,
        IGameFileOperationLease? lease = null,
        GameFileOperationInfo? blockingOperation = null)
    {
        Code = code;
        Lease = lease;
        BlockingOperation = blockingOperation;
    }

    /// <summary>稳定的允许或拒绝代码。</summary>
    public GameFileOperationAdmissionCode Code { get; }

    /// <summary>本次请求是否已原子取得文件任务位。</summary>
    public bool IsAllowed => Code == GameFileOperationAdmissionCode.Allowed;

    /// <summary>允许时持有的唯一令牌；调用方必须在整个旅程最终收尾后释放。</summary>
    public IGameFileOperationLease? Lease { get; }

    /// <summary>拒绝时正在阻挡的游戏任务；没有活动任务时为空。</summary>
    public GameFileOperationInfo? BlockingOperation { get; }
}
