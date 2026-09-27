# ADR-042：Avalonia 启动器按 Core 与 UI 程序集分层

## 背景

游戏协议、安装状态、下载和更新逻辑会持续演进，但它们不应因 Avalonia 控件、图片解码或
本地化资源而被绑定到桌面表现层。StellaSora 与 Blue Archive 官方启动器共享的
`@launcher/utils`、类型及 preload 层也表明：协议/文件处理与表现层之间有稳定接缝。

本项目仍是单进程 Avalonia 应用；不以 Electron 的 IPC 模型作为拆分前提。

## 决策

程序集依赖固定为：

```text
Cafe.Launcher.Avalonia (WinExe) → Cafe.Launcher.Avalonia.UI → Cafe.Launcher.Core → Cafe.Launcher.Updater.Core
Cafe.Launcher.Avalonia (WinExe) → Cafe.Launcher.Core
Cafe.Launcher.Updater → Cafe.Launcher.Updater.Core
```

- `Cafe.Launcher.Core` 不可引用 Avalonia、MarkView、Material Icons 或 UI 资源；它提供
  结构化 `LauncherMessage`、`LauncherBuildIdentity` 和后端服务注册入口。
- `Cafe.Launcher.Avalonia.UI` 承载 Views、Controls、Converters、ViewModel、主题、本地化和
  运行时资产。后续迁移以 `LauncherPresentationSession` 收口宿主到 UI 的生命周期调用。
- 原 `Cafe.Launcher.Avalonia` 保留项目路径、程序集名和可执行文件名，只负责进程生命周期、
  单实例信号和顶层 Avalonia 生命周期。
- `LauncherBuildIdentity` 必须由 WinExe 的程序集创建，避免类库把自己的版本误报为产品版本。
- 生产程序集不得彼此 `InternalsVisibleTo`；仅各测试程序集可作为 friend。

为维持每批迁移可构建，已经移入 Core 的既有公开协议工具暂保留
`Cafe.Launcher.Avalonia.*` 源命名空间。它们的物理程序集边界已生效；在调用方迁移到窄
Core API 时再做命名空间收口，避免一次迁移同时造成无意义的全仓库引用改写。

## 后果

- 宿主必须先调用 `AddLauncherCore`，再注册表现层；MS.DI 的反向释放因此先销毁 UI。
- 新增后端代码先放 Core，并把用户可见文本表达为 `LauncherMessageCode + arguments`；UI
  映射到 `LocalizationKeys`。
- 不能按 `Features/*` 建程序集：Shell 对表现族的下行聚合会变成循环引用，且会迫使出大量
  浅接口。
- 不引入独立 Contracts 或 Yostar.Protocol 项目；在确有第二个 .NET 消费方之前，这会只是
  一个单消费者的假接缝。
