# Cafe Launcher 桌面系统通知与激活集成计划

## 1. 目标

为 Cafe Launcher 增加原生桌面系统通知能力，在保留现有 Avalonia 窗口内 Toast 的前提下：

- Windows 使用 Windows App SDK `AppNotificationManager`。
- Linux 优先使用 XDG Desktop Portal Notification。
- Linux Portal 不可用时回退到 `org.freedesktop.Notifications`。
- `.deb` 安装版支持通过 `org.freedesktop.Application` 实现通知点击后的冷启动。
- 用户可在设置中启用/禁用桌面系统通知。
- 系统通知遵守系统通知设置和平台能力，不以自建弹窗绕过系统策略。
- 系统通知动作只暴露稳定、可跨进程恢复的 activation intent，不复用现有窗口内 `ToastAction` 委托。
- 不改变现有 Toast UI、业务命令和错误处理行为。

该功能必须保持：

- Windows ZIP 可继续独立分发。
- Windows Inno Setup 安装版正常升级。
- Linux `.tar.gz`、AppImage、`.deb` 均继续工作。
- macOS 构建不受 Windows/Linux 平台依赖污染。
- 无原生通知能力时 Launcher 必须无异常降级。

---

# 2. 非目标

第一阶段不做以下内容：

- 不实现 macOS UserNotifications。
- 不把所有窗口内 Toast 都复制成系统通知。
- 不允许系统通知直接执行复杂业务操作。
- 不允许系统通知持有 `Func<>`、ViewModel、DI scope 等进程内对象。
- 不支持从系统通知直接执行：
  - 重试下载；
  - 修复；
  - 卸载；
  - 查看日志；
  - 启动游戏。
- 不建立后台 daemon/service 常驻进程。
- 不尝试绕过 Windows/Linux 用户关闭通知后的系统策略。
- 不要求 ZIP/AppImage/tar.gz 具备与安装包完全相同的冷启动集成能力。

第一版系统通知的稳定动作统一收敛为：

```text
ShowLauncher
```

未来如果增加其他动作，应先证明其具备稳定的跨进程语义，再扩充 activation contract。

---

# 3. 当前仓库基线

主项目目前使用单一：

```xml
<TargetFramework>net10.0</TargetFramework>
```

Release 构建为 .NET self-contained，且 Windows、Linux、macOS RID 当前共享同一套 `dotnet publish` 调用。

现有 Toast 系统由 `ToastService` 发布同步 `ToastRaised` 事件：

```csharp
public event Action<ToastNotification>? ToastRaised;
```

并生成新的 `ToastNotification`。

窗口内 Toast 动作当前直接保存：

```csharp
Func<CancellationToken, Task<ToastActionResult>>
```

属于纯进程内行为，因此不能直接映射成 Windows/Linux 系统通知动作。

当前单实例体系已经具备：

- Windows Named Mutex；
- Windows Named Event；
- Unix local socket；
- `--launch-game` 二次实例转发。

但 Unix local socket 当前只处理 `--launch-game`；显示现有 Launcher 的 `RaiseShowWindow()` 仍是 Windows-only。

现有 `SystemTrayService.ShowWindow()` 已实现：

```text
Show
→ WindowState.Normal
→ Activate
```

因此可以作为窗口恢复逻辑的基础。

---

# 4. 总体设计

整体结构：

```text
                        ┌─────────────────────┐
                        │    Business Logic   │
                        │ GameOperationJourney│
                        │ Update check / etc. │
                        └──────────┬──────────┘
                                   │
                                   ▼
                         ┌───────────────────┐
                         │   ToastService    │
                         │   ToastRaised     │
                         └─────────┬─────────┘
                                   │
                    ┌──────────────┴──────────────┐
                    │                             │
                    ▼                             ▼
          ┌──────────────────┐       ┌─────────────────────────┐
          │ ToastHostViewModel│       │DesktopNotificationCoord.│
          │ Avalonia in-app  │       │policy / lifecycle / IDs │
          └──────────────────┘       └────────────┬────────────┘
                                                  │
                         ┌────────────────────────┼─────────────────────┐
                         │                        │                     │
                         ▼                        ▼                     ▼
              WindowsAppNotification   LinuxPortalNotification  Freedesktop
                    Backend                  Backend             Fallback
```

系统通知是现有 Toast 的**附加输出渠道**，不是替代品。

业务层仍通过 `ToastService` 产生用户反馈。

---

# 5. 通知领域模型

## 5.1 ToastOptions 扩展

在现有 `ToastOptions` 中增加：

```csharp
public DesktopNotificationDelivery DesktopDelivery { get; init; }
    = DesktopNotificationDelivery.InAppOnly;

public string? DesktopNotificationId { get; init; }

public DesktopActivationIntent? DesktopActivation { get; init; }
```

建议：

```csharp
public enum DesktopNotificationDelivery
{
    InAppOnly,
    WhenBackground
}
```

第一版不增加 `Always`。

这样可以防止普通 Toast 被无意复制到通知中心。

---

## 5.2 Activation Intent

不要直接引用 `ToastAction`。

新增：

```csharp
public enum DesktopActivationIntent
{
    ShowLauncher
}
```

后续如果需要参数，再演进为 record，例如：

```csharp
public sealed record DesktopActivation(
    DesktopActivationIntent Intent,
    IReadOnlyDictionary<string, string>? Arguments = null);
```

第一阶段实际上只允许：

```text
ShowLauncher
```

---

# 6. 稳定 Notification ID

现有 `ToastNotification.Id` 是随机 GUID。

随机 ID 适合窗口 Toast，但无法可靠更新或撤回系统通知。

因此系统通知必须允许业务显式提供稳定 ID。

示例：

```text
game-operation:<operation-id>
launcher-update:<version>
```

例如：

```text
game-operation:6f89...
```

如果业务没有提供 `DesktopNotificationId`：

- Coordinator 可以生成一次性 ID；
- 该通知不可更新；
- 只适用于终态通知。

以后如果加入下载进度通知，则必须使用稳定 correlation ID。

---

# 7. DesktopNotification 数据对象

新增与 UI Toast 解耦的数据结构：

```csharp
public sealed record DesktopNotification
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public DesktopActivationIntent Activation { get; init; }
        = DesktopActivationIntent.ShowLauncher;

    public DesktopNotificationLifetime Lifetime { get; init; }
        = DesktopNotificationLifetime.Persistent;
}
```

生命周期建议：

```csharp
public enum DesktopNotificationLifetime
{
    ProcessBound,
    Persistent
}
```

含义：

### ProcessBound

例如未来的：

- 下载进度；
- 正在检查更新；
- 暂时状态。

应用退出时可以移除。

### Persistent

例如：

- 下载完成；
- 更新完成；
- 安装失败；
- Launcher 更新可用。

这些通知必须允许比当前进程活得更久。

**正常退出时不得统一删除 Persistent 通知。**

这是 Linux `.deb` 冷启动能力成立的必要前提。

---

# 8. DesktopNotificationCoordinator

新增：

```text
Services/
  Notifications/
    DesktopNotificationCoordinator.cs
```

职责：

1. 订阅 `ToastService.ToastRaised`。
2. 检查 `DesktopDelivery`。
3. 检查用户设置。
4. 判断 Launcher 是否在前台。
5. 转换成 `DesktopNotification`。
6. 调用当前平台 backend。
7. 捕获所有平台异常。
8. 记录 diagnostics。
9. 管理 ProcessBound notification ID。
10. 应用退出时仅清理 ProcessBound 通知。

---

# 9. Coordinator 生命周期

不能只：

```csharp
services.AddSingleton<DesktopNotificationCoordinator>();
```

然后假定 DI 会创建它。

MS.DI singleton 是惰性实例化的，如果没有任何组件 resolve，该服务就永远不会订阅 `ToastRaised`。

因此必须在 `App.OnFrameworkInitializationCompleted()` 构建 DI 后显式启动：

```csharp
var notifications =
    serviceProvider.GetRequiredService<DesktopNotificationCoordinator>();

await / fire-safe notifications.Initialize...
```

初始化应发生在主要业务初始化之前。

退出时：

```text
Stop
→ unsubscribe ToastRaised
→ remove ProcessBound notifications
→ unregister platform backend
→ dispose
```

---

# 10. ToastRaised 的异步安全

当前：

```csharp
event Action<ToastNotification>
```

是同步事件。

Coordinator 不能简单：

```csharp
async void OnToastRaised(...)
```

然后直接执行平台 D-Bus / Windows API。

建议内部使用：

```text
ToastRaised
   ↓
TryWrite Channel<DesktopNotificationWorkItem>
   ↓
single consumer loop
   ↓
await backend.ShowAsync()
```

例如：

```csharp
Channel<DesktopNotificationWorkItem>
```

优势：

- Toast 生产者不受 D-Bus/WinRT 延迟影响；
- 平台异常不会向业务层传播；
- 保证通知顺序；
- 可在 shutdown 时完成队列；
- 后续容易实现 replace/update。

如果不采用 Channel，也必须有统一的异常保护 fire-and-forget helper。

---

# 11. 前后台判断

新增：

```csharp
IWindowPresenceService
```

或：

```csharp
IWindowActivationService
```

至少提供：

```csharp
bool IsLauncherForeground { get; }
Task ShowLauncherAsync(string? activationToken = null);
```

建议前台定义：

```text
MainWindow.IsVisible
AND WindowState != Minimized
AND MainWindow.IsActive
```

当：

```text
DesktopDelivery == WhenBackground
AND IsLauncherForeground == false
```

才发送系统通知。

窗口内 Toast 仍照常存在。

---

# 12. 统一窗口恢复

将当前：

```text
SystemTrayService.ShowWindow()
```

内部的恢复逻辑收敛到：

```csharp
WindowActivationService
```

例如：

```csharp
public Task ShowLauncherAsync(string? activationToken = null)
{
    mainWindow.Show();

    if (mainWindow.WindowState == WindowState.Minimized)
        mainWindow.WindowState = WindowState.Normal;

    ApplyPlatformActivationTokenIfPossible(activationToken);

    mainWindow.Activate();
}
```

以下入口都调用同一逻辑：

```text
Tray
Windows notification
Linux Portal
Linux Freedesktop notification
second instance show
D-Bus org.freedesktop.Application
```

避免未来出现多个略有不同的恢复实现。

---

# 13. DI 结构

新增抽象：

```csharp
public interface IDesktopNotificationBackend : IAsyncDisposable
{
    bool IsSupported { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task ShowAsync(
        DesktopNotification notification,
        CancellationToken cancellationToken);

    Task RemoveAsync(
        string notificationId,
        CancellationToken cancellationToken);
}
```

平台 backend：

```text
WindowsAppNotificationBackend
LinuxPortalNotificationBackend
LinuxFreedesktopNotificationBackend
NullDesktopNotificationBackend
```

Coordinator 不需要知道 WinRT 或 D-Bus。

---

# 14. Null Backend

必须实现：

```csharp
NullDesktopNotificationBackend
```

以下情况自动使用：

- macOS；
- Windows App Runtime 不可用；
- Linux 没有通知服务；
- Portal 和 freedesktop 均初始化失败；
- 平台 API 不受支持。

行为：

```text
ShowAsync -> no-op
RemoveAsync -> no-op
IsSupported -> false
```

原有 Toast 不受影响。

---

# 15. 设置模型

新增：

```csharp
[JsonPropertyName("desktopNotificationsEnabled")]
public bool DesktopNotificationsEnabled { get; set; } = true;
```

默认建议：

```text
true
```

原因是：

- Launcher 仅在后台为明确事件发通知；
- 系统仍具有最终通知开关；
- backend 不可用时自动降级。

---

# 16. 设置模型必须同步修改的位置

当前 `LauncherSettings.DeepClone()` 最终依赖手写 copy constructor。

因此新增属性时必须同步更新：

```text
LauncherSettings field
LauncherSettings property
LauncherSettings copy constructor
SettingsEditor.SettingsMatch()
LauncherSettingsService tests
SettingsEditor dirty tests
SettingsEditor discard tests
serialization tests
```

当前 `SettingsMatch()` 同样是手写逐字段比较。

遗漏这里会导致：

```text
修改通知设置
→ IsDirty 状态错误
→ Apply/Discard 行为异常
```

---

# 17. 设置 UI

在普通设置页面增加：

```text
桌面通知                [ Switch ]
在 Launcher 位于后台时显示系统通知
```

不同状态：

### Backend 可用

正常可操作。

### Backend 当前不可用

建议：

- 保留 persisted setting；
- Switch 可保持用户意图；
- 显示说明：

```text
当前系统环境不支持桌面通知。
```

不要自动把 persisted setting 改为 false。

这样 Windows ZIP 用户后来安装 Runtime 后，无需再次开启。

---

# 18. 业务通知规则

第一版只对少量终态事件发送系统通知。

## 18.1 游戏安装/更新

### 成功

```text
DesktopDelivery = WhenBackground
Activation = ShowLauncher
Lifetime = Persistent
```

### 终态失败

同样：

```text
WhenBackground
ShowLauncher
Persistent
```

窗口 Toast 可以继续有：

```text
Retry
View log
```

但系统通知不携带这些进程内动作。

---

## 18.2 修复

成功：

```text
WhenBackground
```

失败：

```text
WhenBackground
```

---

# 19. 不发送系统通知的业务

以下第一版保持 `InAppOnly`：

```text
游戏成功启动
操作正在停止
用户主动停止完成
创建桌面快捷方式
普通状态提示
下载暂停
下载恢复
Launcher 初始化失败（窗口启动过程中）
```

特别是：

```text
GameOperationErrorCode.Stopped
```

不能和普通失败共用 `WhenBackground`。

当前 `ShowOperationResult()` 同时处理成功、Stopped 和失败，因此不能直接给整个 helper 添加统一 policy。

应按 result 类型分别决定。

---

# 20. Launcher 更新通知

`LauncherUpdateService` 当前只返回：

```csharp
LauncherUpdateCheckResult
```

本身不负责 UI Toast。

因此：

```text
Launcher update available
```

的系统通知应在消费 update result 的上层发出，而不是让 `LauncherUpdateService` 依赖 notification layer。

ID：

```text
launcher-update:<version>
```

这样同一版本不会不断生成重复通知。

---

# 21. 本地化

系统通知 Title 和 Body 必须来自现有 `LocalizationService` / resx。

不要依赖：

```text
ToastSeverity.Error -> "Error"
```

自动生成标题。

建议增加明确 key：

```text
desktopNotificationGameInstallCompletedTitle
desktopNotificationGameUpdateCompletedTitle
desktopNotificationGameOperationFailedTitle
desktopNotificationRepairCompletedTitle
desktopNotificationLauncherUpdateAvailableTitle
desktopNotificationShowLauncher
desktopNotificationsSettingTitle
desktopNotificationsSettingDescription
desktopNotificationsUnavailable
```

同步：

```text
LauncherStrings.resx
LauncherStrings.zh-Hans.resx
LauncherStrings.zh-Hant.resx
LauncherStrings.ja.resx
LocalizationKeys
ResxResourceContractTests
```

---

# 22. Windows 构建模型

## 22.1 多 TFM

将主项目改为：

```xml
<TargetFrameworks>
  net10.0;
  net10.0-windows10.0.19041.0
</TargetFrameworks>
```

Windows backend 只在 Windows TFM 编译。

Windows App SDK PackageReference 同样 condition：

```xml
Condition="'$(TargetFramework)' == 'net10.0-windows10.0.19041.0'"
```

Windows-specific source 可以使用：

```text
#if WINDOWS
```

或条件 Compile Item。

优先避免让 Windows App SDK 类型进入通用接口。

---

# 23. Windows App SDK 配置

Windows TFM：

```xml
<WindowsPackageType>None</WindowsPackageType>
```

保持应用为 unpackaged desktop app。

`.NET SelfContained` 与 Windows App SDK Runtime 是两个不同概念：

```text
.NET runtime
≠
Windows App Runtime
```

现有 Release 的 `<SelfContained>true</SelfContained>` 不代表 Windows App Runtime 已包含。

---

# 24. Windows Runtime 策略

## Inno Setup

正式安装版负责确保所需 Windows App Runtime 可用。

M0 时：

1. 确认选定 Windows App SDK 固定版本；
2. 下载与其严格匹配的 Runtime installer；
3. 记录：
   - 文件名；
   - 官方来源；
   - SHA-256；
   - silent 参数；
   - architecture；
4. build pipeline 验证 hash；
5. 安装器内嵌该 runtime installer。

安装时：

```text
Windows App Runtime installer --quiet
```

不要使用可能强制结束其他应用的强制参数。

---

# 25. Windows ZIP 策略

ZIP 不强制携带 Runtime。

启动时：

```text
AppNotificationManager.Singleton.IsSupported()
```

若 false：

```text
Windows backend IsSupported = false
→ Null behavior
→ 窗口 Toast 正常
```

因此能力契约：

```text
Inno:
系统通知为正式支持能力。

ZIP:
系统通知属于 best effort；
运行环境缺少必要 Windows Runtime 时自动降级。
```

---

# 26. Windows Backend

文件建议：

```text
Services/
  Notifications/
    Windows/
      WindowsAppNotificationBackend.cs
      WindowsNotificationBootstrap.cs
      WindowsNotificationActivationParser.cs
```

基本流程：

```text
Initialize
  ↓
AppNotificationManager.Singleton
  ↓
IsSupported
  ↓
NotificationInvoked += handler
  ↓
Register
```

退出：

```text
NotificationInvoked -= handler
Unregister
```

所有调用都必须有异常保护。

---

# 27. Windows 冷启动 Activation

需要处理：

```text
应用未运行
↓
用户点击 notification
↓
Windows 启动 Launcher
↓
notification activation 抵达
```

Activation 很可能发生在 MainWindow 完全初始化之前。

因此不能在 Windows backend 里直接操作 View。

新增：

```text
PendingDesktopActivationQueue
```

流程：

```text
Windows notification activation
          │
          ▼
PendingDesktopActivationQueue
          │
     App/MainWindow ready
          │
          ▼
DesktopActivationRouter
          │
          ▼
WindowActivationService.ShowLauncherAsync()
```

warm activation 同样进入 Router。

这样 warm/cold 共用一条逻辑。

---

# 28. Windows Activation Payload

第一版 payload 必须极简：

```text
action=show-launcher
```

Parser 采用严格 allowlist。

未知：

```text
action
argument
version
```

全部忽略。

禁止根据 notification 参数：

- 执行路径；
- 打开任意 URL；
- 运行命令；
- 删除文件；
- 直接启动游戏。

---

# 29. Windows Elevated 限制

Launcher 正常运行不得依赖提升权限。

系统通知能力只保证普通用户上下文。

Inno 安装器可以 elevation，但安装完成后启动 Launcher 时，应保证运行 Launcher 的 token 是用户普通上下文，而不是把安装器的管理员 token 继承给 Launcher。

M2 必须验证：

```text
管理员安装
→ 安装完成
→ Launch Cafe Launcher
→ Launcher 非 elevated
→ notification 正常
```

---

# 30. Windows 发布脚本修订

当前 `Build-Distribution.ps1`：

```text
dotnet publish
-c Release
-r <rid>
-o ...
```

没有 `-f`。

多 TFM 后必须改成显式映射：

```text
win-x64
→ net10.0-windows10.0.19041.0

linux-x64
→ net10.0

osx-arm64
→ net10.0
```

例如：

```powershell
$targetFramework = switch ($rid) {
    "win-x64"   { "net10.0-windows10.0.19041.0" }
    "linux-x64" { "net10.0" }
    "osx-arm64" { "net10.0" }
}
```

然后：

```text
dotnet publish -f $targetFramework
```

这是 M0/M1 的 blocking requirement，不应拖到 Windows backend 完成后再修。

---

# 31. Windows Installer

现有 Inno installer 从：

```text
artifacts/publish/win-x64
```

读取 publish 输出。

保持该目录契约即可。

新增：

```text
installer/windows-runtime/
  WindowsAppRuntimeInstall.exe
  WindowsAppRuntime.sha256
```

或者由 CI 下载到 artifacts staging。

必须避免把“网络实时下载 Runtime”作为安装必要条件。

正式 release installer 应可离线完成 Runtime 安装。

---

# 32. Linux 总体架构

Linux 分成两个阶段：

```text
M3a:
Portal notification
+
freedesktop fallback
+
warm activation

M3b:
.deb
+
org.freedesktop.Application
+
D-Bus activation
+
cold activation
```

AppImage / tar.gz 第一阶段不承诺冷启动。

---

# 33. Tmds.DBus

当前 Avalonia 12.1.1 已经通过 `Avalonia.FreeDesktop` 传递引入 `Tmds.DBus.Protocol`。

产品代码将直接使用该库后，应加入 direct PackageReference。

建议第一阶段固定与当前 Avalonia dependency graph 对齐的 Protocol 版本，而不是同时升级整个 D-Bus dependency stack。

如使用：

```text
Tmds.DBus.Generator
```

同样进入中央包版本管理。

仓库当前使用：

```text
Directory.Packages.props
```

集中维护版本。

---

# 34. Linux Portal Backend

文件：

```text
Services/
  Notifications/
    Linux/
      LinuxPortalNotificationBackend.cs
      LinuxPortalNotificationProxy.cs
      LinuxNotificationCapabilityProbe.cs
```

流程：

```text
session bus
   ↓
org.freedesktop.portal.Desktop
   ↓
org.freedesktop.portal.Notification
   ↓
AddNotification
```

优先 Portal。

---

# 35. Portal Action

对于支持冷启动的 `.deb`：

```text
default-action = app.show-launcher
```

通知点击：

```text
org.freedesktop.Application.ActivateAction(
    "show-launcher",
    ...
)
```

对于没有 `.deb` integration 的 AppImage/tar.gz：

不要声称可冷启动。

可采用 process-local Portal action：

```text
ActionInvoked
```

只保证 Launcher 已运行时恢复窗口。

---

# 36. Linux activation token

Portal action activation 可能提供：

```text
activation-token
```

WindowActivationService 应允许：

```csharp
ShowLauncherAsync(string? activationToken)
```

平台 adapter：

```text
有 activation token
→ 尽可能交给 Wayland activation path
→ 然后 Show/Activate

无 token
→ 普通 Show/Activate best effort
```

M3a 需要在：

```text
GNOME Wayland
KDE Plasma Wayland
```

验证实际行为。

Token 应视为一次性、短生命周期数据：

- 不持久化；
- 不写设置；
- 不写长生命周期日志；
- 不通过本地 socket 长期缓存。

---

# 37. Freedesktop Fallback

只有以下场景才 fallback：

```text
Portal service 不存在
Portal notification interface 不可用
初始化失败
协议不受支持
Portal 调用发生技术性错误/超时
```

以下场景不要尝试通过 legacy service 绕开 Portal：

```text
明确权限拒绝
明确策略拒绝
明确系统禁止通知
```

此时：

```text
系统通知 no-op
窗口 Toast 正常
```

---

# 38. Freedesktop Backend

实现：

```text
org.freedesktop.Notifications.Notify
org.freedesktop.Notifications.CloseNotification
ActionInvoked
NotificationClosed
```

第一版只映射：

```text
default
→ ShowLauncher
```

Launcher 已退出时不保证 legacy notification 能重新启动应用。

因此 fallback 的能力契约是：

```text
display
+
warm activation
```

而不是：

```text
cold activation
```

---

# 39. Linux .deb 冷启动架构

这是原计划必须修正的部分。

不能：

```text
notification
→ 启动第二 Launcher
→ 第二实例发现 mutex
→ 用旧 socket 通知首实例
```

作为 D-Bus 的主要激活机制。

正确结构：

```text
                    Session D-Bus

          io.github....CafeLauncher
                    │
                    │ owner
                    ▼
              Launcher 首实例
                    │
                    ▼
       org.freedesktop.Application
```

正常运行的首实例自身就必须持有 well-known bus name。

---

# 40. .deb 启动流程

正常启动：

```text
Program
  ↓
CrossProcessLaunchBridge
  ↓
赢得现有 singleton
  ↓
尽早 claim D-Bus well-known name
  ↓
创建/导出 org.freedesktop.Application
  ↓
启动 Avalonia
```

D-Bus owner 获取必须尽可能靠前，以缩短：

```text
mutex 已取得
但 bus name 尚未取得
```

的 race window。

在 MainWindow 创建前抵达的 activation：

```text
queue
→ App ready
→ drain
```

---

# 41. D-Bus Application Service

建议 reverse-DNS ID：

```text
io.github.bluearchive_cafe.CafeLauncher
```

最终名称必须统一固定，一旦 release 不应频繁改变。

对应 object path，例如：

```text
/io/github/bluearchive_cafe/CafeLauncher
```

实现至少满足所需的：

```text
org.freedesktop.Application.Activate
org.freedesktop.Application.ActivateAction
```

第一阶段：

```text
Activate
→ ShowLauncher

ActivateAction("show-launcher")
→ ShowLauncher
```

未知 action：

```text
ignore + diagnostics
```

---

# 42. D-Bus service 文件

`.deb` 安装：

```text
/usr/share/dbus-1/services/
  io.github.bluearchive_cafe.CafeLauncher.service
```

内容类似：

```ini
[D-BUS Service]
Name=io.github.bluearchive_cafe.CafeLauncher
Exec=/usr/bin/cafe-launcher
```

实际格式在 M0/M3b 根据 freedesktop 规范与目标发行版实测锁定。

---

# 43. .desktop 文件

当前：

```ini
[Desktop Entry]
Type=Application
Name=Cafe Launcher
Exec=Cafe.Launcher.Avalonia
Icon=cafe-launcher
Categories=Game;
Terminal=false
StartupWMClass=Cafe.Launcher.Avalonia
```



`.deb` 版本需要改成安装包专用 desktop entry：

```ini
Exec=cafe-launcher
DBusActivatable=true
```

AppImage 仍使用不声明 D-Bus activation 的 desktop 文件。

因此建议拆成：

```text
installer/linux/cafe-launcher.desktop
installer/linux/debian/cafe-launcher.desktop
```

不要让 AppImage 意外声明它没有安装的 D-Bus service。

---

# 44. Linux 包型识别

需要明确当前运行实例是否属于 `.deb` integration。

不要依赖猜测：

```text
路径是否 /opt/...
```

建议 Debian wrapper：

```sh
export CAFE_LAUNCHER_DISTRIBUTION=deb
exec /opt/cafe-launcher/Cafe.Launcher.Avalonia "$@"
```

D-Bus service 与 desktop entry 都调用：

```text
/usr/bin/cafe-launcher
```

Launcher 根据该 environment capability 启用：

```text
org.freedesktop.Application registration
app.show-launcher portal action
```

AppImage/tar.gz 则只启用 warm activation。

---

# 45. 现有单实例桥的角色

保留：

```text
CrossProcessLaunchBridge
CrossProcessLaunchSignal
```

继续负责：

```text
普通二次启动
--launch-game
portable Linux
Windows singleton
```

D-Bus integration **不是替代现有 singleton**。

而是：

```text
notification / desktop D-Bus activation
→ D-Bus 首实例

普通 executable 二次启动
→ 现有 CrossProcessLaunchBridge
```

两者最终都路由到：

```text
DesktopActivationRouter
```

---

# 46. 是否需要跨平台 ShowLauncher socket

仍建议补充。

当前 Unix 第二次普通启动时没有等价的 show-window signal。

可以扩展现有 Unix signal protocol：

```text
LaunchGame
ShowLauncher
```

而不是只有：

```text
LaunchGame
```

这样：

```text
./Cafe.Launcher.Avalonia
```

在已有实例运行时，也能在 Linux 上显示首实例。

这同时改善现有单实例行为，与通知功能独立有价值。

---

# 47. Linux 通知生命周期

退出时：

### 删除

```text
ProcessBound notification
```

例如未来：

```text
download-progress:<id>
```

### 不删除

```text
Persistent terminal notification
```

例如：

```text
install completed
update failed
launcher update available
```

否则：

```text
通知刚出现
→ Launcher 退出
→ 通知立即被移除
```

会直接破坏 `.deb` 冷启动设计。

---

# 48. 用户关闭通知设置时

`DesktopNotificationsEnabled=false` 后：

1. 停止产生新的系统通知；
2. 清理当前进程追踪的 ProcessBound notification；
3. 不主动清空历史 Persistent terminal notifications；
4. 保留窗口内 Toast。

重新打开：

```text
只影响未来通知
```

---

# 49. Diagnostics

新增结构化 diagnostics。

建议事件：

```text
DesktopNotifications.Initialized
DesktopNotifications.Unsupported
DesktopNotifications.ShowFailed
DesktopNotifications.Activated
DesktopNotifications.Removed
DesktopNotifications.PortalUnavailable
DesktopNotifications.FreedesktopFallback
DesktopNotifications.WindowsRuntimeUnavailable
DesktopNotifications.DBusNameClaimFailed
```

不要记录：

```text
完整 activation token
任意用户路径
复杂 notification payload
```

---

# 50. 安全边界

所有系统 notification activation 都必须经过：

```text
DesktopActivationRouter
```

Router 是 allowlist：

```text
ShowLauncher
```

不允许：

```text
arbitrary command
arbitrary path
arbitrary URL
shell execution
reflection invocation
ViewModel method by name
```

Windows/Linux backend 只负责：

```text
platform payload
→ DesktopActivationIntent
```

---

# 51. Package Lock

新增：

```text
Microsoft.WindowsAppSDK
Tmds.DBus.Protocol
Tmds.DBus.Generator（如使用）
```

后必须更新 lock files。

但是计划已知工作树中的 `packages.lock.json` 可能存在用户未提交修改。

实施原则：

```text
先保存当前 diff
→ 执行 restore
→ 查看 lock diff
→ 手工合并
→ 不覆盖用户已有修改
```

不要简单删除并重新生成整个 lock file。

---

# 52. 测试策略

现有仓库已经有较强测试基础，包括：

```text
ToastServiceTests
ToastHostViewModelTests
CrossProcessLaunchBridgeTests
CrossProcessLaunchSignalTests
LauncherSettingsServiceTests
SettingsEditorTests
InstallerContractTests
ReleaseScriptTests
```



在这些基础上扩充。

---

# 53. Unit Tests

新增：

```text
DesktopNotificationCoordinatorTests
DesktopNotificationPolicyTests
DesktopNotificationIdTests
DesktopActivationRouterTests
NullDesktopNotificationBackendTests
WindowsNotificationActivationParserTests
LinuxPortalNotificationMappingTests
LinuxFreedesktopNotificationMappingTests
LinuxDbusApplicationTests
```

---

# 54. Coordinator 核心测试

至少覆盖：

```text
InAppOnly never forwards
WhenBackground + foreground -> no system notification
WhenBackground + background -> forwards
setting disabled -> no forwarding
backend unsupported -> no exception
backend Show throws -> Toast caller unaffected
persistent notification not removed on shutdown
process-bound notification removed on shutdown
stable ID preserved
missing ID generates one-shot ID
```

---

# 55. 设置测试

`SettingsEditorTests`：

```text
DesktopNotificationsEnabled change => IsDirty true
Discard => restores value
ApplySnapshot => copied correctly
GetSnapshot => copied correctly
GetSavedSnapshot => copied correctly
```

`LauncherSettingsServiceTests`：

```text
missing old JSON field => true default
false persists
true persists
round trip
```

---

# 56. Business Policy Tests

`GameOperationJourneyTests`：

验证：

```text
install success -> WhenBackground
terminal failure -> WhenBackground
repair success -> WhenBackground
repair failure -> WhenBackground
Stopped -> InAppOnly
game launch -> InAppOnly
```

并继续确认现有：

```text
Retry
ViewLog
```

窗口动作没有变成 desktop actions。

---

# 57. Windows Build Gate

CI 必须分别执行：

```text
dotnet build -f net10.0
```

以及：

```text
dotnet build -f net10.0-windows10.0.19041.0
```

原因：

如果 CI 只运行通用 TFM：

```text
WindowsAppNotificationBackend
```

可能数周都没有真正被编译。

---

# 58. Publish Gate

自动测试：

```text
win-x64
→ Windows TFM

linux-x64
→ net10.0

osx-arm64
→ net10.0
```

`ReleaseScriptTests` 应检查脚本显式包含：

```text
-f
```

以及 RID → TFM mapping。

---

# 59. Windows Integration Test

自动化能覆盖：

```text
parser
registration wrapper
unsupported fallback
payload
lifecycle
```

真正 notification shell UI 仍需 VM smoke test。

目标：

```text
Windows 10
Windows 11
```

场景：

```text
Inno fresh install
Inno upgrade
ZIP with Runtime
ZIP without Runtime
Launcher foreground
Launcher minimized
Launcher tray-hidden
warm activation
cold activation
system notifications disabled
```

---

# 60. Linux Automated Integration

可使用：

```text
dbus-run-session
```

配合 fake Portal / fake Notifications service。

测试：

```text
Portal preferred
Portal absent -> freedesktop fallback
Portal technical failure -> fallback
Portal policy rejection -> no legacy bypass
ActionInvoked parsed
activation token passed to router
DBus application action routed
unknown action ignored
```

---

# 61. Linux Manual Matrix

最低：

```text
GNOME Wayland
KDE Plasma Wayland
```

推荐再覆盖：

```text
X11 session
```

场景：

```text
Portal notification display
notification click while running
window minimized
window hidden to tray
activation token
Portal unavailable fallback
.deb cold activation
.deb warm activation
AppImage warm activation
```

---

# 62. Installer Contract Tests

扩展现有 `InstallerContractTests`。

验证：

```text
Runtime installer embedded/staged
Runtime hash contract exists
Runtime install command uses quiet mode
no force parameter
Launcher executable installation unchanged
AppMutex unchanged
upgrade identity unchanged
post-install launch still present
```

现有 Inno `AppId` 与 mutex 不应因通知功能改变。

---

# 63. Debian Package Contract Tests

新增静态 contract：

```text
/usr/bin/cafe-launcher exists
wrapper sets CAFE_LAUNCHER_DISTRIBUTION=deb
desktop DBusActivatable=true
desktop Exec=cafe-launcher
D-Bus service Name matches app ID
D-Bus service Exec=cafe-launcher
application bus name equals notification app action owner
icons unchanged
```

---

# 64. AppImage Contract

确保：

```text
AppImage desktop entry
```

不意外包含：

```text
DBusActivatable=true
```

除非未来 AppImage 自己实现相应 registration/install integration。

---

# 65. M0 — 技术验证与构建基础

预计：

```text
2–3 人日
```

## 工作

### Windows

- 选定 Windows App SDK 具体稳定版本。
- 固定 package version。
- 确认 Runtime redistributable。
- 记录 SHA-256。
- 验证 Win10 clean VM。
- 验证 Win11 clean VM。
- 验证 AppNotificationManager：
  - IsSupported；
  - Register；
  - show；
  - warm activation；
  - cold activation。
- 验证 elevated process 限制。

### Build

- 改双 TFM prototype。
- Windows backend dummy compile。
- Build-Distribution 增 `-f`。
- Windows/Linux/macOS publish 全通过。

### Linux

制作最小 prototype：

```text
Portal AddNotification
Portal ActionInvoked
activation-token
freedesktop Notify
D-Bus well-known name
org.freedesktop.Application
```

至少在：

```text
KDE Wayland
GNOME Wayland
```

各跑一次。

---

# 66. M0 Go / No-Go

## Windows GO

必须满足：

```text
Inno clean VM 能显示 notification
warm activation 能恢复窗口
cold activation 能启动 Launcher
ZIP 缺 Runtime 时安静降级
```

## Build GO

必须满足：

```text
net10.0 build
Windows TFM build
win publish
linux publish
macOS publish
```

全部明确选择正确 TFM。

## Linux M3a GO

必须满足：

```text
GNOME Portal show + activation
KDE Portal show + activation
technical fallback works
```

## Linux M3b GO

必须证明：

```text
普通 .deb Launcher 能持有 well-known bus name
```

并：

```text
Launcher 退出
→ D-Bus activation
→ 只启动一个 Launcher
→ ActivateAction 成功
```

任何一项失败：

```text
M3b 延后
```

但不阻塞 Windows/M3a。

---

# 67. M1 — 通用通知架构

预计：

```text
2 人日
```

实现：

```text
DesktopNotificationDelivery
DesktopActivationIntent
DesktopNotification
DesktopNotificationLifetime
ToastOptions extensions
DesktopNotificationCoordinator
NullDesktopNotificationBackend
DesktopActivationRouter
WindowActivationService
PendingActivationQueue
settings model
settings UI
localization
unit tests
```

同时：

```text
App explicit initialization
shutdown lifecycle
Channel consumer
```

---

# 68. M1 验收

必须：

```text
现有 Toast tests 全通过
现有 UI Toast 行为无改变
disabled => 无系统 backend 调用
foreground => 无系统 backend 调用
background => fake backend 收到 notification
backend exception 不影响业务
settings round-trip 正确
```

---

# 69. M2 — Windows Production Backend

预计：

```text
2–4 人日
```

实现：

```text
Microsoft.WindowsAppSDK reference
Windows TFM
WindowsAppNotificationBackend
Windows activation parser
cold activation queue
Runtime packaging
Inno integration
ZIP degradation
Windows CI compile gate
Windows contract tests
```

---

# 70. M2 验收

Win10 + Win11：

```text
安装完成通知
安装失败通知
更新完成通知
修复完成通知
Launcher update notification
```

必须：

```text
foreground 不重复弹系统通知
background 显示
click 显示 Launcher
cold click 启动 Launcher
system disabled 不崩溃
Runtime missing ZIP 不崩溃
uninstall/upgrade 不受影响
```

---

# 71. M3a — Linux Portal + Fallback

预计：

```text
2–4 人日
```

实现：

```text
Tmds.DBus.Protocol direct reference
Portal backend
Portal capability probe
Portal action routing
activation token propagation
Freedesktop fallback
Linux diagnostics
fake D-Bus tests
```

AppImage/tar.gz：

```text
只承诺 warm activation
```

---

# 72. M3a 验收

GNOME/KDE：

```text
Portal notification display
running notification click
tray-hidden restore
minimized restore
activation token best effort
Portal absent -> fallback
policy rejection -> no bypass
```

系统通知 backend 不可用：

```text
Launcher 继续正常工作
```

---

# 73. M3b — Debian D-Bus 冷启动

预计：

```text
2–3 人日
```

实现：

```text
LinuxDbusApplicationService
well-known name ownership
org.freedesktop.Application
startup activation queue
Debian-specific .desktop
D-Bus service file
wrapper environment
Build-Distribution packaging
Debian contract tests
```

---

# 74. M3b 验收

在 `.deb` 上：

### Launcher 正在运行

```text
notification click
→ existing process receives ActivateAction
→ no new visible instance
→ window restored
```

### Launcher 已退出

```text
notification click
→ D-Bus daemon activates service
→ Launcher starts
→ one process
→ activation consumed
→ window visible
```

### Race

连续快速：

```text
notification click
desktop icon launch
notification click
```

不得留下两个 Launcher 实例。

---

# 75. M4 — 收尾与发布保障

预计：

```text
1–2 人日
```

包括：

```text
完整 regression
release scripts
docs
manual test checklist
diagnostics review
localization review
lock file review
upgrade test
clean VM test
```

补充开发文档：

```text
docs/desktop-notifications.md
```

描述：

```text
capability matrix
Windows Runtime dependency
Linux portal/fallback
.deb D-Bus activation
portable limitations
troubleshooting
```

---

# 76. 平台能力矩阵

| 分发形式 | 显示系统通知 | Warm Activation | Cold Activation |
|---|---|---|---|
| Windows Inno | 是 | 是 | 是 |
| Windows ZIP + Runtime | 是 | 是 | 是 |
| Windows ZIP 无 Runtime | 降级 | 否 | 否 |
| Linux .deb + Portal | 是 | 是 | 是 |
| Linux .deb + freedesktop fallback | 是 | 是 | 不保证 |
| Linux AppImage + Portal | 是 | 是 | 第一版不保证 |
| Linux AppImage + freedesktop | 是 | 是 | 否 |
| Linux tar.gz | best effort | best effort | 否 |
| macOS | 否 | — | — |

---

# 77. 修改文件清单

预计涉及：

```text
Directory.Packages.props

src/Cafe.Launcher.Avalonia/
  Cafe.Launcher.Avalonia.csproj
  Program.cs
  App.axaml.cs

  Models/
    LauncherSettings.cs

  Services/
    ToastNotification.cs
    SettingsEditor.cs
    SystemTrayService.cs
    CrossProcessLaunchBridge.cs
    ...

    Notifications/
      IDesktopNotificationBackend.cs
      DesktopNotification.cs
      DesktopNotificationCoordinator.cs
      DesktopActivationRouter.cs
      NullDesktopNotificationBackend.cs

      Windows/
        WindowsAppNotificationBackend.cs
        WindowsNotificationBootstrap.cs
        WindowsNotificationActivationParser.cs

      Linux/
        LinuxPortalNotificationBackend.cs
        LinuxFreedesktopNotificationBackend.cs
        LinuxDbusApplicationService.cs

    WindowActivationService.cs

  Composition/
    ServiceConfiguration.cs

  Features/
    GameOperations/
      GameOperationJourney.cs

  Resources/
    LauncherStrings*.resx

installer/
  Cafe.Launcher.Avalonia.iss

  linux/
    cafe-launcher.desktop
    debian/
      cafe-launcher
      cafe-launcher.desktop
      control
      io.github....CafeLauncher.service

scripts/
  Build-Distribution.ps1
  New-WindowsInstaller.ps1

tests/
  Cafe.Launcher.Avalonia.Tests/
    DesktopNotificationCoordinatorTests.cs
    DesktopActivationRouterTests.cs
    WindowsNotificationActivationParserTests.cs
    LinuxPortalNotificationTests.cs
    LinuxDbusApplicationTests.cs
    LauncherSettingsServiceTests.cs
    SettingsEditorTests.cs
    GameOperationJourneyTests.cs
    InstallerContractTests.cs
    ReleaseScriptTests.cs
```

---

# 78. 实施顺序

推荐严格按照：

```text
M0
 ↓
TFM / package / runtime / Portal / D-Bus feasibility locked
 ↓
M1
 ↓
平台无关架构稳定
 ↓
M2 Windows
 ↓
M3a Linux Portal
 ↓
M3b Debian cold activation
 ↓
M4
```

不要：

```text
先写 Windows backend
→ 再改 multi-TFM
```

也不要：

```text
先写 Portal app.show-launcher
→ 最后才考虑谁持有 D-Bus name
```

这两个顺序都容易返工。

---

# 79. 可并行部分

M1 完成后：

```text
M2 Windows
```

和：

```text
M3a Linux
```

原则上可以并行。

M3b 依赖：

```text
M3a activation model
+
Linux WindowActivationService
+
M0 D-Bus feasibility
```

因此不要提前。

---

# 80. 工期

较稳妥估算：

| 阶段 | 预计 |
|---|---:|
| M0 | 2–3 人日 |
| M1 | 2 人日 |
| M2 | 2–4 人日 |
| M3a | 2–4 人日 |
| M3b | 2–3 人日 |
| M4 | 1–2 人日 |

完整顺序实施：

```text
约 11–18 人日
```

考虑部分 Windows/Linux 工作可并行：

```text
实际 calendar time 可低于总人日
```

若暂缓 M3b：

```text
约 9–15 人日
```

---

# 81. 风险排序

## 高风险

### Linux D-Bus cold activation

风险：

```text
bus ownership
startup race
single-instance race
desktop environment differences
Wayland activation
```

措施：

```text
M0 prototype
early bus claim
pending activation queue
.deb only
```

### Windows Runtime

风险：

```text
clean machine missing Runtime
ZIP capability mismatch
installer architecture/version
```

措施：

```text
pinned runtime
hash verification
offline installer
IsSupported degradation
```

---

## 中风险

### 多 TFM

风险：

```text
CI 只编译 net10.0
Windows-specific compile silently broken
publish selecting wrong TFM
```

措施：

```text
explicit compile gates
explicit -f
contract tests
```

### Async Toast subscription

风险：

```text
async void
unobserved exception
shutdown race
```

措施：

```text
Channel
single consumer
cancellation
central diagnostics
```

---

# 82. 回退方案

## Windows backend 出问题

立即：

```text
backend -> Null
```

保留：

```text
in-app Toast
所有业务功能
```

不需要回退业务层。

---

## Linux Portal 出问题

```text
Portal
→ Freedesktop
→ Null
```

逐级降级。

---

## M3b 不稳定

可以完全不发布：

```text
DBusActivatable=true
D-Bus .service
```

仍保留：

```text
M3a Portal notification
warm activation
```

因此 M3b 不应成为 Linux notification 第一版的 blocking requirement。

---

# 83. 发布前最终 Checklist

## 通用

- [ ] Toast UI 无行为回归
- [ ] DesktopNotificationsEnabled 正确持久化
- [ ] Disable 不影响 in-app Toast
- [ ] 所有系统通知文字已本地化
- [ ] Stopped 不发送系统通知
- [ ] Retry/View log 未暴露为系统 notification action
- [ ] Persistent notification 不在正常退出时删除
- [ ] backend exception 不向业务传播

## Build

- [ ] net10.0 build
- [ ] Windows TFM build
- [ ] win-x64 publish
- [ ] linux-x64 publish
- [ ] osx-arm64 publish
- [ ] package lock diff 人工检查

## Windows

- [ ] Win10 clean VM
- [ ] Win11 clean VM
- [ ] Inno fresh install
- [ ] Inno upgrade
- [ ] Runtime install
- [ ] ZIP no Runtime fallback
- [ ] foreground
- [ ] minimized
- [ ] tray hidden
- [ ] warm activation
- [ ] cold activation
- [ ] notifications disabled in Windows
- [ ] Launcher 非 elevated

## Linux

- [ ] GNOME Wayland
- [ ] KDE Wayland
- [ ] Portal display
- [ ] Portal warm activation
- [ ] activation token
- [ ] freedesktop fallback
- [ ] AppImage regression
- [ ] tar.gz regression
- [ ] .deb regression

## M3b

- [ ] well-known D-Bus name
- [ ] DBusActivatable
- [ ] service file
- [ ] warm ActivateAction
- [ ] cold ActivateAction
- [ ] no duplicate process
- [ ] shutdown then notification click
- [ ] rapid activation race

---

# 84. 最终交付标准

完成 M1 + M2 + M3a 后，即可认为：

> Cafe Launcher 已具备跨 Windows/Linux 的原生桌面通知基础能力，并能在平台不支持时安全降级。

完成 M3b 后：

> Debian 安装版进一步获得完整的 D-Bus application integration，使持久通知即使在 Launcher 已退出后，也能够通过标准桌面激活机制重新启动并恢复 Launcher。

整个方案保持：

```text
业务层
    ↓
ToastService
    ↓
窗口 Toast

以及：

ToastService
    ↓
DesktopNotificationCoordinator
    ↓
platform notification
```

两条输出路径彼此解耦。

因此即使未来某个平台 backend 被移除、重写或暂时禁用，也不会影响 Launcher 的核心业务和现有 Toast 系统。