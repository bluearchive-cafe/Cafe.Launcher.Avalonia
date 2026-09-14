using System.Runtime.CompilerServices;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Testing;

internal static class TestUserDataIsolation
{
    // 层级必须保持浅短：LauncherUserDataDirectory.Root 会派生 Unix 域套接字路径
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
            LauncherUserDataDirectory.TestOverrideEnvironmentVariable,
            userDataDirectory);
    }
}
