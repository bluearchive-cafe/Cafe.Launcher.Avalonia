using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.UI.Services;

/// <summary>Which entry point ran the update check; it decides whether an outcome is presented at all.</summary>
internal enum LauncherUpdateTrigger
{
    /// <summary>User-invoked check: failure, up-to-date, and update are all reported.</summary>
    Manual,

    /// <summary>Startup check: only an available update is reported; failure and up-to-date stay silent.</summary>
    Startup
}

/// <summary>
/// Presents one update-check result with the policy of the entry point that requested it.
/// 结果到呈现的映射只在此实现：失败、已最新、有更新三态如何处理，以及哪个入口呈现什么。
/// </summary>
internal interface ILauncherUpdatePresenter
{
    /// <summary>Presents <paramref name="result"/> according to <paramref name="trigger"/>.</summary>
    void Present(LauncherUpdateCheckResult result, LauncherUpdateTrigger trigger);
}
