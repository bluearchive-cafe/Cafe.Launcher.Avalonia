using Cafe.Launcher.UI.Features.Diagnostics;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Features.ResourcePanel;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.ViewModels;

namespace Cafe.Launcher.UI.Features.Shell;

/// <summary>
/// Shell 的呈现族协作者聚合：窗口壳与各 Feature ViewModel 的单一承载参数。
/// 新增呈现协作者时只改本记录与组合根，不再扩散 ShellLifecycle /
/// MainWindowViewModel 的构造器签名（对齐 GameShortcutService.ShortcutEnvironment
/// 的聚合模式）。
/// </summary>
internal sealed record ShellPresentationFamily(
    ShellViewModel Shell,
    BackgroundViewModel Background,
    RemoteContentViewModel RemoteContent,
    DialogsViewModel Dialogs,
    GameOperationsViewModel Operations,
    ToastHostViewModel Toasts,
    WindowChromeViewModel WindowChrome,
    SettingsViewModel Settings,
    ResourcePanelViewModel ResourcePanel,
    LogViewerDialogViewModel LogViewer,
    LogExportDialogViewModel LogExport,
    DebugViewModel Debug,
    ModalHostViewModel ModalHost);
