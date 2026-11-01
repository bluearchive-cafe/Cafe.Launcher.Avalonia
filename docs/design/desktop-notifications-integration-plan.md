# 桌面系统通知与激活集成计划

> 状态：待实施方案，2026-11-01 按当前程序集与单实例实现整理。下表均为目标能力，不表示产品已经支持系统通知。实施前需重新验证平台 API、依赖版本和打包方案。

## 目标与范围

保留现有窗口内 Toast，仅为启动器在后台时的重要终态提供系统通知。第一版点击动作只有 `ShowLauncher`，用于恢复或启动启动器；重试、修复、卸载、查看日志和启动游戏仍在应用内执行。遵守系统通知权限，不新增常驻服务。

| 分发形式 | 目标后端 | 已运行时恢复窗口 | 退出后冷启动 |
| --- | --- | --- | --- |
| Windows Inno 安装版 | Windows App SDK | 支持 | 支持，须干净环境验证 |
| Windows ZIP | Windows App SDK，缺 Runtime 时降级 | 后端可用时支持 | 后端可用时验证 |
| Linux `.deb` + Portal | XDG Desktop Portal Notification | 支持 | 独立阶段验证 D-Bus 激活 |
| Linux freedesktop 回退 | `org.freedesktop.Notifications` | 支持 | 不保证 |
| Linux AppImage / tar.gz | Portal 优先，freedesktop 回退 | 尽力支持 | 第一版不承诺 |
| Linux RPM / Arch | 待单独验证包集成 | 待验证 | 不继承 `.deb` 的承诺 |
| macOS | 空后端 | — | — |

平台能力不足或初始化失败时保留窗口内 Toast 和业务功能。Windows ZIP 继续独立分发，通用构建不引入 Windows 平台类型。

## 当前基础与代码边界

- `Cafe.Launcher.UI/Services/ToastService.cs` 同步发布 `ToastRaised`；窗口内 `ToastAction` 持有委托，只能在当前进程使用。
- 宿主的 `CrossProcessLaunchBridge` 已统一承载 Windows/Unix 的启动游戏与显示窗口信号；普通二次启动唤起已有窗口，无需另建 ShowLauncher socket。
- 组合根和跨进程接线在 `Cafe.Launcher`，表现层服务在 `Cafe.Launcher.UI`，平台后端按[程序集边界](../../PROJECT_CONVENTIONS.md#2-程序集与命名边界)放置。通过 `LauncherPresentationSession` 对接窗口生命周期，不在 `App` 内另建容器或使用 Service Locator。
- Linux desktop 与 wrapper 使用共享模板，新增包型差异在生成步骤处理，见 [ADR-039](adr/ADR-039-Linux打包启动资产共用模板去重.md)。

## 通知策略

| 事件 | 系统通知策略 |
| --- | --- |
| 游戏安装、更新、修复成功或终态失败 | `WhenBackground`，持久终态通知 |
| 启动器有可用更新 | 由消费更新结果的上层发出，同版本去重 |
| 用户主动停止、暂停/恢复、成功启动游戏、创建快捷方式、普通状态与初始化失败 | `InAppOnly` |

前台定义为窗口可见、非最小化且处于活动状态。业务必须逐结果决定策略，尤其不能把 `Stopped` 当成终态失败。`LauncherUpdateService` 保持返回结果，不依赖通知层。

建议给 `ToastOptions` 增加投递策略、可选稳定通知 ID 与激活意图，默认 `InAppOnly`；第一版不提供 `Always`。标题和正文使用四语言资源，窗口内动作不序列化进系统通知。

系统通知数据包含 ID、标题、正文、激活意图和生命周期，不持有 ViewModel、委托或 DI 对象。ID 可采用 `game-operation:<operation-id>` 与 `launcher-update:<version>`；一次性终态可生成 ID，需要替换或撤回的通知必须使用稳定 ID。

生命周期区分 `ProcessBound` 与 `Persistent`：退出或关闭通知设置时只清理前者；终态通知保留，才能支持退出后点击。重新开启设置只影响未来事件，不重放历史通知。

## 协调与生命周期

`DesktopNotificationCoordinator` 订阅 Toast 事件，读取设置与窗口状态，再将纯数据交给平台后端。同步事件只入队，由单消费者异步发送，集中捕获平台错误并记录本地诊断，避免阻塞业务或产生未观察异常。

通过现有表现层会话显式启动协调器，不能依赖单例登记自动实例化。初始化在业务事件产生前完成；退出时取消订阅、结束并等待队列、清理进程绑定通知、注销后端并释放资源。启动早期到达的激活先排队，窗口就绪后派发到 UI 线程。

所有入口使用同一窗口恢复服务：显示窗口、恢复最小化状态并尝试激活。Wayland token 作为短期一次性数据传入，不落盘、不长期缓存、不记录完整值；无 token 时尽力恢复窗口。

所有外部载荷仅映射到白名单 `ShowLauncher`。未知动作和参数忽略并记录简要诊断；载荷不能指定命令、路径、URL 或任意业务调用。

## 设置

建议新增 `desktopNotificationsEnabled`，默认 true，仅在后台为明确事件通知；系统权限仍是最终开关。后端不可用时保留用户选择并显示原因，不自动改写为 false。

字段复制、状态比较、归一化、唯一写入方和本地化按[规范 §9](../../PROJECT_CONVENTIONS.md#9-settings-与持久化兼容性)、[§6](../../PROJECT_CONVENTIONS.md#6-本地化)实施；使用当前 `DeepClone` / `ComparedProperties` / `HasSameSettingsState` 契约，不另建逐字段比较表。

## Windows 方案与待验证项

候选方案为 Windows App SDK `AppNotificationManager`，保留 unpackaged desktop 模式。先验证平台后端隔离与 TFM 选择；若采用 `net10.0` 加 Windows 专用 TFM，多目标的构建、测试和所有 RID 发布入口必须显式选目标，Windows 包只进入 Windows 编译图。

.NET self-contained 不代表包含 Windows App Runtime。M0 需固定 SDK 与匹配 Runtime，核对官方来源、架构、SHA-256 和静默参数；安装版内嵌匹配运行库并校验，ZIP 缺运行库时安全降级。普通用户上下文是支持边界；验证管理员安装后启动器以普通用户身份运行。

后端初始化检查支持性、订阅激活并注册，退出时取消订阅并注销，异常不向业务传播。冷启动激活与已有实例转发必须统一经过激活路由，验证重复点击、启动竞态与单实例约束。

## Linux 方案与待验证项

先实现 Portal 显示与进程内点击，再增加 freedesktop 回退。仅 Portal 服务或接口缺失、初始化失败、协议不支持、技术性错误或超时可以回退；明确权限或策略拒绝时停止，不能换后端绕过。

`.deb` 冷启动作为独立阶段：

1. 首实例取得现有单实例所有权后尽早申请 D-Bus well-known name，并导出 `org.freedesktop.Application` 的 `Activate` / `ActivateAction`。
2. 建议应用 ID `io.github.bluearchive_cafe.CafeLauncher`，对象路径与 service、desktop 条目一致；正式发布前锁定身份与协议。
3. Portal 动作为 `app.show-launcher`；窗口未就绪时排队。普通二次 executable 启动仍走现有单实例桥，两条入口汇入同一激活路由。
4. `.deb` wrapper 显式提供包能力标记，D-Bus service 与 desktop 调用同一个 wrapper；仅实际安装 service 的包声明 `DBusActivatable=true`，不按 `/opt` 等路径猜包型。
5. 验证 mutex 与 bus name 申请间的竞态、已运行/已退出状态、连续点击和 Wayland 激活。不能证明可靠性时不发布 service 或激活声明，保留显示及进程内恢复能力。

D-Bus 客户端库（原方案候选 `Tmds.DBus`）须先验证维护状态、协议接口和 AOT/打包适配，版本集中管理并更新锁文件与第三方声明。

## 实施阶段与验收

| 阶段 | 交付与通过条件 |
| --- | --- |
| M0 技术验证 | Windows 10/11 干净环境验证注册、显示、前后台点击、缺 Runtime 降级及普通用户运行；GNOME/KDE Wayland 验证 Portal、token 与技术性回退；验证通用及 Windows 目标和三平台发布 |
| M1 通用协调 | 通知模型、队列、窗口恢复、空后端、设置与业务策略；覆盖后台判断、停止不通知、稳定 ID、关闭设置、异常隔离和退出等待 |
| M2 Windows | 注册/注销、冷启动与已有实例路由、Runtime 打包；验证安装/升级/卸载、ZIP 无 Runtime、损坏 payload 与重复激活 |
| M3a Linux | Portal 和 freedesktop；在 GNOME/KDE、Wayland/X11 验证显示、点击、系统禁用与降级；AppImage/tar.gz 不承诺冷启动 |
| M3b Debian 冷启动 | service、desktop、wrapper 与首实例 D-Bus owner；验证退出后点击只启动一个实例、早期事件排队和连续点击。失败可独立延期 |
| M4 交付 | 四语言资源、设置事务、依赖/锁文件/notices、平台打包契约与文档；运行受影响测试及 `pwsh ./verify.ps1` |

自动化覆盖策略、生命周期、激活解析、设置保存/放弃/旧配置以及包生成结果；干净系统、通知中心持久性和真实桌面焦点由集成实测确认，不能由 Headless 替代。平台能力矩阵只在对应验收通过后更新为产品承诺。
