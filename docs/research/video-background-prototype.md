# libmpv 视频背景原型

日期：2026-10-31。本文记录 Windows x64 / Avalonia 12.1.4 / Debug 原型的历史实验。后续已将生产实现接入普通设置与 Release；当前产品行为见 [领域契约](../../CONTEXT.md#设置与操作)，运行库恢复和分发见 [开发指南](../DEVELOPMENT.md)。下面的「不分发运行库」仅指原型阶段；生产控件位于 `Controls/VideoBackground`，不使用原型 CSV 采样器。

## 验证问题

后续普通设置接入的桌面冒烟记录位于 `.artifacts/video-background/settings/desktop-smoke/result.json`：随包 LGPL DLL 实际到达首帧、播放位置前进，设置／确认框覆盖期间保留同一控件，合成 125%／150%／200% DPI 不重建；隐藏／恢复、最小化和减少动效的暂停状态正确；视频切换保留同一原生 player/render context，切回静态后库拥有计数归零，损坏文件回退静态背景。此次检查不等同于真实跨显示器 DPI 或截图验收。修复了命名颜色 `Black` 不符合 mpv 数值颜色语法的初始化失败，并避免以零透明度等待首帧。

在该生产控件路径上，1080p30 测试片热身 5 秒、采样 15 秒：整机口径 CPU 约 1.95%，进程私有内存约 548→536 MB，托管内存约 28.9 MB，硬解为 `d3d11va-copy`。这是短时 Debug 冒烟数据，不是长时泄漏证明；GPU 保留仍见 `AUD-PERF-009`。

普通设置接入后的 `pwsh ./verify.ps1` 已通过（`.artifacts/video-background/settings/verify.log`）：Unit 2536 通过／16 跳过，Headless 339 通过，Release 资源契约 23 通过；Debug／Release 零警告、零错误。手写 C# 行覆盖率 86.37%，分支覆盖率 91.84%，未降低门槛或排除生产视频代码。Windows x64 `dotnet publish` 输出已核对 DLL 的 SHA-256、RID 目录、许可证与来源说明；未执行发布、打标签或真实安装升级。

验证 libmpv Render API 绘制到 `OpenGlControlBase` 所提供的 framebuffer 后，是否能复用 Cafe 的真实设置页、确认框和托盘生命周期，并测量尺寸、缩放及资源开销。现有图片背景、设置持久化和 Core 的程序集边界保持原样。

## 启动

准备脚本需要已安装的 FFmpeg、7-Zip 和 curl。下载的是 mpv 安装页所列 shinchiro 构建，固定版本及 SHA-256；仅解压到 Git 忽略的 `.artifacts`，不会安装系统组件或复制到发行资产。

```powershell
pwsh ./scripts/Initialize-VideoBackgroundPrototype.ps1
pwsh ./scripts/Start-VideoBackgroundPrototype.ps1 -GpuSampleSeconds 180
```

已有媒体和运行库可直接运行：

```powershell
pwsh ./scripts/Start-VideoBackgroundPrototype.ps1 `
    -VideoPath 'D:/Media/background.mp4' `
    -LibMpvPath 'D:/Tools/mpv/libmpv-2.dll' `
    -GpuSampleSeconds 180
```

准备脚本可选 `-Proxy 'http://127.0.0.1:端口'`；不改变系统代理。启动脚本复制 Debug 产物到原型输出目录的 `app`，使用独立 apphost 名、单实例信号和数据根，与安装版并行，避免锁住后续构建。输出目录默认 `.artifacts/video-background`；用 `-OutputDirectory` 为每次测量保留独立记录。

## 操作

快捷键仅在显式启用的 Debug 原型中注册，前缀均为 `Ctrl+Shift`：

| 按键 | 操作 |
| --- | --- |
| B | 暂停并隐藏视频，显示原有静态背景；再次按下恢复 |
| S | 打开真实设置页 |
| C | 打开真实重置设置确认框；用 Escape 取消，**无需执行重置** |
| D | 循环注入 125%、150%、200%、100% 的缩放通知 |
| R | 请求切换 960×640 与 1300×800 DIP，实际尺寸仍受窗口最小尺寸约束 |
| T | 调用真实托盘服务隐藏窗口，10 秒后调用同一服务恢复 |

D 使用 Avalonia 12.1.4 的私有缩放通知处理方法，专门用于实验；不更改 Windows 显示设置，不等价于真实多显示器跨屏测试。升级 Avalonia 后需要重新检查此临时接缝。

## 实现与生命周期

- `Controls/VideoBackgroundPrototype/VideoBackgroundPrototypeControl.cs`：在 GL 初始化阶段创建播放器和 render context；视频更新回调只合并重绘请求；GL 渲染阶段按窗口物理尺寸更新 FBO 参数。无逐帧位图解码、编码或 `WriteableBitmap` 复制。
- `MpvPrototypeNative.cs`：最小 C ABI 绑定，显式绝对路径加载。原生回调委托在 renderer/player 销毁完成前始终保活。
- `MainWindow.VideoBackgroundPrototype.cs`：视频插在两张背景图之后、真实 Shell 与全部覆盖层之前；不接受鼠标命中或键盘焦点。隐藏/最小化时暂停，恢复时继续。
- GL 清理顺序为撤销回调 → 释放 render context → 销毁播放器 → 释放参数内存与动态库；窗口关闭时停止采样。
- 入口、原生适配器及控件均由 `#if DEBUG` 排除出 Release。只有设置 `CAFE_VIDEO_PROTOTYPE=1` 才创建控件；由启动脚本同时提供视频、运行库和 CSV 路径。
- 两个实验类与主窗口的实验方法逐项标记 `ExcludeFromCodeCoverage`：它们不属于发行路径，依赖真实 GPU/窗口的验证记录；没有排除整个 `MainWindow`，也没有下调正式覆盖率门槛。原生 ABI 布局及显式选取运行库的暂停/恢复另有自动化测试。

## 测量口径

测试视频为 FFmpeg `testsrc2`，H.264 / yuv420p，1920×1080、30 fps、20 秒循环，无音频。测试机 CPU 为 i5-10400F（12 逻辑处理器），GPU 为 GTX 1050 Ti（驱动 32.0.15.8157），并存在 GameViewer 虚拟显示适配器。

- `samples.csv`：每秒记录播放阶段、实际绘制频率、渲染调用耗时、FBO 物理尺寸、缩放、暂停状态、硬解后端、CPU、私有提交内存、播放位置和解码丢帧。
- CPU 为当前进程 CPU 时间差 / 墙钟时间 / 逻辑处理器数，100% 表示整机全部逻辑处理器。不是单核百分比。
- `gpu.csv`：Windows GPU 性能计数器按该 PID 过滤，分别记录 3D、VideoDecode、Copy，以及专用/共享 GPU 内存。每一列按同类型引擎求和，不能把三列相加称作“总 GPU 利用率”。
- `render_ms` 仅是 CPU 侧 `mpv_render_context_render` 调用耗时，不是完整 GPU 帧时间，也不是端到端延迟。
- 静态对照为同一进程中已初始化但暂停/隐藏的播放器，不代表完全未加载 libmpv 的进程内存。

## 验证记录

运行库压缩包：`mpv-dev-x86_64-20261021-git-e471496c56.7z`。

SHA-256：`efc719eff3dc5954b4a5efc800632808bcec01cc9034940dd14e5eb7fe9ea633`，与发布元数据一致；7-Zip 完整性检查通过。

本次运行约 492 秒。原始数据保存在 `.artifacts/video-background/validation/`：`samples.csv`、`gpu.csv`、`gpu-compare.csv`、`hardware.json` 和 `summary.json`。统计时各阶段丢弃前三个 CPU/FPS 样本与前两个 GPU 样本，避开切换瞬间；不是 Release 性能基准，期间还运行了仓库验证工具。

### 行为结果

| 验证项 | 结果与证据 |
| --- | --- |
| 视频播放 | H.264 1080p30 循环播放，各稳定阶段约 30 fps；所有样本的解码丢帧计数为 0，错误列为空 |
| 设置页交互 | 使用真实设置命令打开；可访问性树显示设置导航和操作控件可用，主界面控件禁用 |
| 确认框交互 | 在设置页上打开真实重置确认框；设置页禁用，焦点落到安全的「取消」按钮；Escape 可取消，未执行重置 |
| 缩放通知 | 125% → 1625×943，150% → 1950×1131，200% → 2600×1508；返回 100% 后正常继续播放 |
| 窗口缩放 | 请求小窗口后受最小尺寸约束；200% 时实际目标 2048×1294，返回 100% 为 1024×647；继续约 30 fps |
| 托盘 | 调用真实 HideWindow 后播放位置不变、稳定帧率 0；10 秒后调用 ShowWindow，恢复约 30 fps，无新增解码丢帧 |
| 关闭 | 正常关闭原型，进程退出，日志包含 Session ended，未生成崩溃报告 |

### 资源开销

下表静态、视频及托盘阶段使用同一进程、1024×647 的 100% 窗口。CPU 与 GPU 采样频率不同，表中为各自阶段均值，不能视为逐帧配对基准。

| 阶段 | CPU（整机百分比） | 视频绘制 fps | GPU 3D | GPU VideoDecode |
| --- | ---: | ---: | ---: | ---: |
| 静态背景，播放器暂停且隐藏 | 6.97% | 0 | 0.03% | 0% |
| 视频背景 | 9.94% | 30.01 | 13.35% | 5.83% |
| 托盘隐藏 | 0.018% | 0 | 0% | 0% |
| 托盘恢复 | 10.76% | 30.02 | 14.41% | 7.03% |

视频阶段的 CPU 侧绘制调用均值约 0.88 ms。`hwdec-current` 为 `d3d11va-copy`：启用了硬件解码，但带有复制，**没有验证到零复制**；Copy 引擎计数器显示 0 也不能推翻这一点。

进程私有提交内存峰值约 1133 MB；多次高 DPI 与尺寸切换后，静态阶段保留约 1 GB，托盘隐藏后回落到约 908 MB。GPU 专用内存也在高缩放后保留较高缓存，隐藏/恢复后降低。当前样本不足以区分缓存高水位、Debug/现有 Shell 开销与资源生命周期问题；既不能宣称内存泄漏，也不能宣称内存占用已优化。后续应在固定尺寸、关闭其他验证工具的独立进程中，做长时间播放与重复创建/销毁对照。

### 自动化验证

- 两项新增原型测试通过：原生 ABI 布局，以及显式加载本次 libmpv 的初始化/暂停/恢复/销毁。
- 最终 `pwsh ./verify.ps1` 退出码 0：单元测试 2514 通过、14 个平台跳过；无头测试 337 通过；Release 构建零警告；Release 本地化测试 23 通过。
- 手写行覆盖率 86.80%，分支覆盖率 92.87%，原有覆盖率门槛未改。
- 准备脚本实际运行通过；三个 PowerShell 脚本语法检查通过。
- 首轮无头测试中设置保存用例曾因 Toast 集合为空失败；该用例单独重跑和最终完整验证均通过，未修改该用例或相关生产逻辑。

验证日志位于 `.artifacts/video-background/verify-final.log`。Release 不包含这两个实验类，发行包没有引入 libmpv 原生资产。

### 未完成的验收与结论

Windows 窗口截图通道在多次恢复绑定、独立 apphost 与托盘恢复后仍返回 `window capture timed out` / `FrameArrived timed out`。因此，本次只能确认实际控件树的叠层结构、焦点与模态交互，**不能把视觉遮挡、视频方向、裁切和最终像素合成判为通过**。

当前桌面实际缩放为 100%；125%/150%/200% 使用模拟平台通知，尚未进行真实多显示器 DPI 切换、不同显卡与 macOS/Linux 验证。

结论：Render API 原型可运行，图片背景与真实 Shell 覆盖层可以保留，暂停/恢复和物理目标尺寸路径得到运行证据。正式接入前仍需补齐视觉截图/人工验收、真实跨屏 DPI、固定尺寸性能与内存生命周期验证，以及原生运行库的发行策略。

## 来源

- [mpv 嵌入与 Render API](https://github.com/mpv-player/mpv-examples/blob/master/libmpv/README.md)
- [mpv render.h：线程与资源约束](https://github.com/mpv-player/mpv/blob/master/include/mpv/render.h)
- [mpv render_gl.h：FBO 与 GL 初始化参数](https://github.com/mpv-player/mpv/blob/master/include/mpv/render_gl.h)
- [Avalonia OpenGlControlBase](https://docs.avaloniaui.net/api/avalonia/opengl/controls/openglcontrolbase)
