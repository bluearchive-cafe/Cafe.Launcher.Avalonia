using System.Runtime.CompilerServices;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Testing;

internal static class TestUserDataIsolation
{
    // 层级必须保持浅短：LauncherDataRoot 会派生 Unix 域套接字路径
    // （CrossProcessLaunchSignal，形如 <Root>/cl-signal-<12hex>.sock），AF_UNIX 的
    // sockaddr_un 路径上限是 108 字节——目录过深会让绑定永远失败（AUD-CI-002）。
    private static readonly string userDataDirectory = Path.Combine(
        Path.GetTempPath(),
        "cl-tests",
        Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(userDataDirectory);
        Environment.SetEnvironmentVariable(
            LauncherDataRoot.TestOverrideEnvironmentVariable,
            userDataDirectory);

        // 受管兼容子树另有来源：GameCompatibilityPaths 是 ADR-025 的已声明例外
        // （静态助手、无 DI 接缝），Unix 分支读 XDG 数据主目录而不是数据根覆盖。
        // 不一起改，彻底清除的用例会在开发机上写好并删掉真实的
        // ~/.local/share/cafe-launcher/compatibility/<gameId> 整棵。Windows 分支
        // 复用数据根，已随上面的覆盖隔离。
        if (!OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable(UnixDataHomeVariable, userDataDirectory);
        }
    }

    internal const string UnixDataHomeVariable = "XDG_DATA_HOME";
}
