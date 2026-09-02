using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// Shell 的呈现族协作者聚合：窗口壳与各 Feature ViewModel 的单一承载参数。
/// 新增呈现协作者时只改本记录与组合根，不再扩散 ShellLifecycle /
/// MainWindowViewModel 的构造器签名（对齐 GameShortcutService.ShortcutEnvironment
/// 的聚合模式）。
/// </summary>
public sealed record ShellPresentationFamily(
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
    DebugViewModel Debug,
    ModalHostViewModel ModalHost);
