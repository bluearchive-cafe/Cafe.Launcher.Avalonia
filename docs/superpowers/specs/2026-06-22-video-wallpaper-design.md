# 视频壁纸功能设计

- 日期：2026-06-22
- 分支：`feature/video-wallpaper`
- 状态：已批准设计，待编写实现计划

## 目标

允许用户选择**本地视频文件**作为启动器背景（动态壁纸），与现有的 bundled / remote / custom 图片背景并列。视频逐帧渲染进现有的 `Image` 控件，确保设置、对话框、Toast 等浮层照常叠加在背景之上。

## 范围

### 本期实现

- 新增 `BackgroundSources.Video` 背景来源，仅支持**本地视频文件**。
- 基于 LibVLCSharp 的播放引擎，通过视频回调把帧渲染到 `WriteableBitmap`（不使用原生 `VideoView`/HWND）。
- 播放生命周期：**窗口不可见（最小化/隐藏到托盘）时自动暂停，恢复可见时续播**。
- 音频：可开关——静音开关 + 音量级别（0–100）。
- 设置 UI：新增"视频"来源选项、视频文件选择器、静音开关、音量滑块。
- 主题色 `wallpaper` 模式：从当前视频帧提取调色板。
- 失败回退到 bundled 图片，诊断日志记录。

### 明确不在本期范围（后续扩展）

- **游戏进程运行时也暂停**视频（收尾问题 #4，后续迭代再加，届时复用现有 `ProcessService`）。
- 远程视频壁纸（API 下发 URL）。
- 内置（bundled）视频。

## 关键约束与依据

- **Avalonia 12 无内置视频控件。**
- 本应用把浮层（设置 `100` / 对话框 `200` / Toast `1000`）叠加在背景之上（`Views/MainWindow.axaml` 的 Grid 分层，背景为 `Rectangle` + `Image`）。
- 使用 `NativeControlHost` 的视频方案（如 `LibVLCSharp.Avalonia` 的 `VideoView`）会创建独立原生 HWND，因 airspace 问题遮挡所有 Avalonia 浮层 —— **不可用**。
- 因此采用**解码成帧 → `WriteableBitmap` → 现有 `Image`** 的路径，保持 Avalonia 正常合成、复用现有拉伸/填充色/主题色/回退/诊断通路。
- 项目为 self-contained `win-x64`，**未启用 `PublishTrimmed`**（仅 feature switch），原生库无裁剪风险，但安装包体积会增加约 40MB。

## 设计

### 1. 数据模型与设置

新增常量：

```
BackgroundSources.Video = "video"
```

`LauncherSettings` 新增字段（沿用现有 `[JsonPropertyName]` 模式）：

| 字段 | JSON key | 类型 | 默认 |
|---|---|---|---|
| 视频路径 | `videoBackgroundPath` | string | `""` |
| 静音 | `videoBackgroundMuted` | bool | `true` |
| 音量 | `videoBackgroundVolume` | int | `50` |

`SettingsNormalizer` 增加：

- `backgroundSource` 枚举放行 `video`；
- `videoBackgroundVolume` 钳制到 0–100；
- `videoBackgroundPath` 路径裁剪；
- `videoBackgroundMuted` bool 解析。

`SettingsEditor` 走 JSON 深拷贝，新字段自动纳入快照/脏检查/撤销，无需额外改动。`BackgroundFit`（fill/uniform/uniformToFill）与填充色对视频同样适用，复用现有 `BackgroundStretch` / `BackgroundFillBrush`。

### 2. 播放引擎（隔离原生互操作）

新增 `Services/VideoWallpaperEngine.cs`，并定义内部测试 seam 接口 `IVideoWallpaperEngine`：

```csharp
internal interface IVideoWallpaperEngine : IDisposable
{
    WriteableBitmap? CurrentFrame { get; }      // 双缓冲交换后的当前帧
    event Action? FrameReady;                    // 通知 VM 刷新 BackgroundImageSource
    Task<bool> LoadAsync(string path, CancellationToken ct);  // 失败返回 false
    void Play();
    void Pause();
    void Stop();
    void SetVolume(int volume);
    void SetMuted(bool muted);
    Bitmap? CaptureFrame();                       // 供主题色提取
}
```

实现要点（LibVLCSharp，**不使用** `VideoView`）：

- 进程内单例初始化 `LibVLC` 核心；`MediaPlayer` 配置 `SetVideoFormat`(RV32/BGRA) + `SetVideoCallbacks`（Lock/Unlock/Display）。
- **双缓冲 `WriteableBitmap`**：libvlc 写入后台非托管缓冲；`Display` 回调里 `Dispatcher.UIThread.Post`（Render 优先级，前一帧未消费则丢帧而非堆积），把数据拷入闲置的 `WriteableBitmap` 并交换引用 → 触发现有 `Image` 重绘。**不创建原生 HWND，浮层照常合成。**
- 循环播放；音量/静音独立于视频回调。
- `LoadAsync` 失败（libvlc 初始化失败 / 文件无法打开 / 解码失败）返回 `false`，由调用方回退。
- 实现 `IDisposable`，停止解码线程、释放 `MediaPlayer`/缓冲。

生产环境注入真实引擎；测试注入 fake，**无需原生 libvlc**。

### 3. BackgroundViewModel 集成

`UpdateBackgroundImageAsync` 增加 `case BackgroundSources.Video`：

- 路径有效 → `engine.LoadAsync`；成功则订阅 `FrameReady`，每帧把 `engine.CurrentFrame` 赋给 `BackgroundImageSource`，应用音量/静音；失败 → 回退 bundled 图片（沿用现有 fallback + 诊断模式）。
- 切换到非 Video 来源时 `Stop()` 并释放引擎、退订事件。
- **生命周期**：新增 `SetPlaybackActive(bool)`。`MainWindowViewModel` 监听窗口可见性（最小化/隐藏到托盘 → `false`，恢复 → `true`），转调 `Background.SetPlaybackActive` → `Pause()/Play()`，复用现有托盘/窗口状态通路（`WindowChromeViewModel` / 托盘恢复信号）。
- **主题色**：`ThemeColorMode = Wallpaper` 且来源为 Video 时，用 `engine.CaptureFrame()` 取一帧交给现有 `ThemeColorExtractionService`，复用现有 `wallpaperChanged` 通路。

### 4. 设置 UI（Views + 本地化）

- 外观设置的"背景来源"选择器新增 **视频** 选项。
- 选中 Video 时显示：视频文件选择器（新增 `PickBackgroundVideoAsync` 委托，与现有 `PickBackgroundImageAsync` 同构，code-behind 用 `StorageProvider` 过滤 `mp4/webm/mkv/mov`）、静音开关、音量滑块。
- 三个 locale 文件（en / zh-Hans / ja）加键 + `LocalizedStrings` 的 `[ObservableProperty]` + `Apply()` 接线。
- 严格走设计令牌（`LauncherSpacing*` / `LauncherRadius*` / 控件高度），满足 `UiStyleContractTests`。

### 5. 打包

- 新增 NuGet：`LibVLCSharp` + `VideoLAN.LibVLC.Windows`（原生 libvlc 随 win-x64 输出，self-contained publish 一并打包）。
- 启动时 `Core.Initialize()` 指向打包的 libvlc 目录。
- 安装包体积 **+~40MB**，在 `CLAUDE.md` 中记录。

### 6. 错误处理

- libvlc 不可用 / 文件损坏 / 解码失败 / 路径不存在 → 回退 bundled 图片 + `LocalDiagnostics` 记录（与远程/自定义图片失败一致）。
- 引擎 `IDisposable`，按现有反向注册顺序释放；切换来源或窗口关闭时确保停止解码线程。

### 7. 测试策略

| 层 | 测试 |
|---|---|
| `SettingsNormalizer` | video 枚举放行、音量钳制、路径裁剪、静音解析 |
| `SettingsEditor` | 三个视频字段快照/脏/撤销 |
| `SettingsViewModel` / `SettingsAppearanceViewModel` | 选中 Video、picker 委托、音量/静音绑定与持久化 |
| `BackgroundViewModel`（注入 fake 引擎） | Video 分支启动/停止、加载失败回退、暂停/恢复、音量静音应用、切换来源释放引擎、主题色取帧 |
| `UiStyleContractTests` | 新设置 UI 令牌合规 |
| 真实 `VideoWallpaperEngine` | 薄封装，按 libvlc 可用性**能力检测跳过**（仿照符号链接用例），不纳入 80% 指标（同 XAML 处理） |

关键模块（`BackgroundViewModel` 的 Video 分支、`SettingsNormalizer`、设置 VM）维持 ≥80% 行覆盖；引擎原生封装因依赖原生库豁免，与 XAML 同等对待。

## 验收标准

- Debug 构建 0 警告、0 错误。
- 全部测试通过；缺少 libvlc 能力的用例显示为 Skip。
- 用户可在设置中选择视频文件并实时预览；浮层（设置/对话框/Toast）不被遮挡。
- 视频遵循壁纸契合度（fill / uniform / uniformToFill）与填充色设置，与图片背景行为一致。
- 窗口最小化/隐藏到托盘时视频暂停，恢复时续播。
- 静音/音量开关生效并持久化。
- 加载失败时回退 bundled 图片且不崩溃。

## 假设

- 不改变现有 JSON 字段、网络协议或既有用户行为。
- 测试 seam 保持 `internal`，不扩大公开接口。
- XAML 与原生引擎封装不纳入 80% 覆盖率指标。
