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

    [Theory]
    [InlineData(Operation.Launch, NotInstalled, false)]
    [InlineData(Operation.Launch, Corrupted, false)]
    [InlineData(Operation.Launch, IoFailure, false)]
    [InlineData(Operation.Launch, RemoteUnavailable, false)]
    [InlineData(Operation.Launch, BelowLowestVersion, false)]
    [InlineData(Operation.Launch, UpdateAvailable, false)]
    [InlineData(Operation.Launch, Ready, true)]
    [InlineData(Operation.InstallOrUpdate, NotInstalled, true)]
    [InlineData(Operation.InstallOrUpdate, Corrupted, false)]
    [InlineData(Operation.InstallOrUpdate, IoFailure, false)]
    [InlineData(Operation.InstallOrUpdate, RemoteUnavailable, false)]
    [InlineData(Operation.InstallOrUpdate, BelowLowestVersion, true)]
    [InlineData(Operation.InstallOrUpdate, UpdateAvailable, true)]
    [InlineData(Operation.InstallOrUpdate, Ready, false)]
    [InlineData(Operation.Repair, NotInstalled, false)]
    [InlineData(Operation.Repair, Corrupted, true)]
    [InlineData(Operation.Repair, IoFailure, false)]
    [InlineData(Operation.Repair, RemoteUnavailable, false)]
    [InlineData(Operation.Repair, BelowLowestVersion, false)]
    [InlineData(Operation.Repair, UpdateAvailable, false)]
    [InlineData(Operation.Repair, Ready, true)]
    [InlineData(Operation.Uninstall, NotInstalled, false)]
    [InlineData(Operation.Uninstall, Corrupted, false)]
    [InlineData(Operation.Uninstall, IoFailure, false)]
    [InlineData(Operation.Uninstall, RemoteUnavailable, false)]
    [InlineData(Operation.Uninstall, BelowLowestVersion, false)]
    [InlineData(Operation.Uninstall, UpdateAvailable, false)]
    [InlineData(Operation.Uninstall, Ready, true)]
    public void Allows_WhenCheckedAgainstFullPolicyTable_MatchesTheDocumentedPermissionMatrix(
        Operation operation,
        LauncherRuntimeState state,
        bool expected)
    {
        Assert.Equal(expected, GameOperationPolicy.Allows(operation, state));
    }
}
