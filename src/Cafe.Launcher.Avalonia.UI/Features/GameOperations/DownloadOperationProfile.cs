using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// 安装/更新与修复两种操作在一个下载会话里的全部差异：操作种类、检查与完成两个
/// 阶段的阶段名、零差异与完成两条文案的资源键、日志类别，以及构建计划的分支。
/// 模式位只在入口映射（<see cref="ForDownload"/>/<see cref="ForRepair"/>/
/// <see cref="FromCheckpoint"/>）出现，会话持有 profile，不再散布 repair 布尔（D4）。
/// 检查点字段 <c>IsRepair</c> 是持久化契约，保留原名，续传路径据它重建 profile。
/// </summary>
internal sealed class DownloadOperationProfile
{
    private DownloadOperationProfile(
        GameOperationKind kind,
        GameOperationStage checkStage,
        GameOperationStage completedStage,
        string noChangesKey,
        string completedKey,
        string logCategory)
    {
        Kind = kind;
        CheckStage = checkStage;
        CompletedStage = completedStage;
        NoChangesKey = noChangesKey;
        CompletedKey = completedKey;
        LogCategory = logCategory;
    }

    public GameOperationKind Kind { get; }

    /// <summary>计划构建完成、开始逐文件检查的阶段。</summary>
    public GameOperationStage CheckStage { get; }

    /// <summary>全部文件验证通过的阶段。</summary>
    public GameOperationStage CompletedStage { get; }

    /// <summary>清单差异为零时的成功文案。</summary>
    public string NoChangesKey { get; }

    /// <summary>操作完成时的成功文案。</summary>
    public string CompletedKey { get; }

    /// <summary>完成日志的类别；失败诊断一律记在 GameDownload 下，与本字段无关。</summary>
    public string LogCategory { get; }

    public static DownloadOperationProfile ForDownload() => new(
        GameOperationKind.Download,
        GameOperationStage.UpdateCheck,
        GameOperationStage.DownloadCompleted,
        LocalizationKeys.GameAlreadyCurrent,
        LocalizationKeys.InstallUpdateCompleted,
        "GameDownload");

    public static DownloadOperationProfile ForRepair() => new(
        GameOperationKind.Repair,
        GameOperationStage.RepairCheck,
        GameOperationStage.RepairCompleted,
        LocalizationKeys.RepairNoChanges,
        LocalizationKeys.RepairCompleted,
        "GameRepair");

    /// <summary>检查点续传的唯一映射点：持久化的模式位重建为 profile。</summary>
    public static DownloadOperationProfile FromCheckpoint(bool isRepair) =>
        isRepair ? ForRepair() : ForDownload();

    /// <summary>检查点字段 <c>IsRepair</c> 由此写出，与续传的 <see cref="FromCheckpoint"/> 对应。</summary>
    public bool IsRepair => Kind is GameOperationKind.Repair;

    /// <summary>
    /// 构建本次操作的差异计划：修复只重验盘上文件的完整性，安装/更新与本地安装状态
    /// 做差分。两个 <c>BuildXPlanAsync</c> 签名保持不变（D4 的边界）。
    /// </summary>
    public Task<DownloadPlan> BuildPlanAsync(
        ManifestDiffCalculator diffCalculator,
        string gamePath,
        LocalInstallationState localGame,
        GameConfigResponse gameConfig,
        string patchUrlGroup,
        Action<GameOperationProgress> progress,
        CancellationToken cancellationToken) =>
        IsRepair
            ? diffCalculator.BuildRepairPlanAsync(
                gamePath,
                gameConfig,
                patchUrlGroup,
                progress,
                cancellationToken)
            : diffCalculator.BuildInstallOrUpdatePlanAsync(
                gamePath,
                localGame,
                gameConfig,
                patchUrlGroup,
                progress,
                cancellationToken);
}
