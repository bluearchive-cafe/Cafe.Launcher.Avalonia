using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using static Cafe.Launcher.Avalonia.Features.GameOperations.GameOperationPolicy;
using static Cafe.Launcher.Avalonia.Models.LauncherRuntimeState;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 操作权限全表：4 操作 × 7 状态，28 格全部显式断言。新增
/// LauncherRuntimeState 或 Operation 值时，下面的枚举长度守卫会先失败，
/// 强制把新格子在本表里显式表态——这是策略表唯一允许的更新方式。
/// </summary>
public sealed class GameOperationPolicyTests
{
    [Fact]
    public void EnumCoverage_WhenGameOperationStatesOrOperationsChange_ForcesExplicitTableUpdate()
    {
        Assert.Equal(7, Enum.GetValues<LauncherRuntimeState>().Length);
        Assert.Equal(4, Enum.GetValues<Operation>().Length);
    }

    /// <summary>
    /// 逐格断言判定结果。写成「遍历枚举 × 显式允许集」而不是 28 行 InlineData：删掉一行
    /// InlineData 时剩下的用例照样绿（漏行没有任何信号），而遍历枚举时每个格子都被断言过，
    /// 允许集的规模另有断言兜住「表被删空」这种退化（2026-09-15 复核轮）。
    /// 枚举维度由 <see cref="EnumCoverage_WhenGameOperationStatesOrOperationsChange_ForcesExplicitTableUpdate"/>
    /// 钉住，遍历因此不会因为枚举变空而变成空转。
    /// </summary>
    [Fact]
    public void Decide_ForEveryOperationAndState_MatchesTheDocumentedPermissionMatrix()
    {
        var permitted = new HashSet<(Operation Operation, LauncherRuntimeState State)>
        {
            (Operation.Launch, Ready),
            (Operation.InstallOrUpdate, NotInstalled),
            (Operation.InstallOrUpdate, BelowLowestVersion),
            (Operation.InstallOrUpdate, UpdateAvailable),
            (Operation.Repair, Corrupted),
            (Operation.Repair, Ready),
            (Operation.Uninstall, Ready),
        };

        // 28 格里允许 7 格。这个数字是这张表的形状本身：改了它就必须同时改策略，
        // 而改策略会让下面的遍历红。
        Assert.Equal(7, permitted.Count);

        foreach (var operation in Enum.GetValues<Operation>())
        {
            foreach (var state in Enum.GetValues<LauncherRuntimeState>())
            {
                var expected = permitted.Contains((operation, state))
                    ? GameOperationDecision.Allowed
                    : GameOperationDecision.RejectedForCurrentState;

                Assert.Equal(expected, GameOperationPolicy.Decide(operation, state));
            }
        }
    }

    /// <summary>
    /// 形状约束：策略只给判定结果，不给裸布尔。调用方因此必须对「被拒绝」这一支表态，
    /// 「拒绝之后什么都不做」只能是有意写出来的（ADR-027）。
    /// </summary>
    [Fact]
    public void Decide_IsTheOnlyPublicVerdict_NoBareBoolean()
    {
        var verdicts = typeof(GameOperationPolicy)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(method => (method.Name, method.ReturnType))
            .ToArray();

        Assert.Contains(verdicts, verdict => verdict.Name == "Decide" && verdict.ReturnType == typeof(GameOperationDecision));
        Assert.DoesNotContain(verdicts, verdict => verdict.ReturnType == typeof(bool));
    }
}
