# 视频壁纸功能实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让用户选择本地视频文件作为启动器动态背景，逐帧渲染进现有 `Image` 控件，支持壁纸契合度、可开关音量、窗口不可见时自动暂停。

**架构：** 新增 `BackgroundSources.Video` 来源；原生播放封装在 `IVideoWallpaperEngine` 接口后（生产实现用 LibVLCSharp 视频回调写入双缓冲 `WriteableBitmap`，不创建原生 HWND，浮层照常合成）；`BackgroundViewModel` 增加 Video 分支并注入引擎（测试用 fake）；设置新增 3 个字段、UI、三语本地化。

**技术栈：** .NET 10、Avalonia 12.0.4、CommunityToolkit.Mvvm 8.4.2、LibVLCSharp + VideoLAN.LibVLC.Windows、xUnit 2.9.3。

**规格：** `docs/superpowers/specs/2026-06-22-video-wallpaper-design.md`

**约定：**
- 每步 commit 前先 `.\build.ps1`（期望 `0 个警告，0 个错误`）。
- 测试运行 `dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug`。
- 测试 seam 保持 `internal`（项目已 `[assembly: InternalsVisibleTo("Cafe.Launcher.Avalonia.Tests")]`）。
- 本期**不含**游戏运行时暂停、远程视频、内置视频。

---

## 文件结构

**创建：**
- `Services/VideoWallpaper/IVideoWallpaperEngine.cs` — 播放引擎抽象（测试 seam）
- `Services/VideoWallpaper/VideoWallpaperEngine.cs` — LibVLCSharp 实现
- `Services/VideoWallpaper/NullVideoWallpaperEngine.cs` — 空实现（libvlc 不可用时的安全降级）
- `tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperSettingsTests.cs` — 设置字段规范化/编辑测试
- `tests/Cafe.Launcher.Avalonia.Tests/FakeVideoWallpaperEngine.cs` — 测试用 fake 引擎
- 在 `BackgroundViewModelTests.cs` 内新增 Video 分支测试（已存在文件）

**修改：**
- `Models/LauncherStateModels.cs` — `BackgroundSources.Video` 常量 + 3 个 `LauncherSettings` 字段
- `Services/LauncherSettingsService.cs:225-258` — 规范化新字段
- `ViewModels/BackgroundViewModel.cs` — Video 分支、引擎注入、`SetPlaybackActive`
- `ViewModels/SettingsOptionsViewModel.cs:23-28,178-185` — Video 选项 + 本地化 DisplayName
- `ViewModels/SettingsAppearanceViewModel.cs` — `IsVideoBackgroundSelected`、`VideoVolume`、`IsVideoMuted` 投影
- `ViewModels/SettingsViewModel.cs` — `PickBackgroundVideoAsync` 委托 + `ChooseBackgroundVideoAsync` 命令
- `ViewModels/MainWindowViewModel.cs` — 引擎接线、`GetBackgroundBitmap` 已有
- `Views/MainWindow.axaml.cs` — 视频文件选择器、`ConfigureViewModel` 接线、窗口可见性 → `SetPlaybackActive`
- `Views/MainWindowSettingsOverlay.axaml:439` 附近 — 视频设置区块 UI
- `Services/LocalizationService.cs` — 新 `[ObservableProperty]` 键 + `Apply()` 接线
- `Assets/Locales/en.json`、`zh-Hans.json`、`ja.json` — 新键
- `Services/ServiceConfiguration.cs` — 注册 `IVideoWallpaperEngine`
- `Cafe.Launcher.Avalonia.csproj` — LibVLCSharp 包引用
- `App.axaml.cs` — `LibVLCSharp.Shared.Core.Initialize()`
- `CLAUDE.md` / `AGENTS.md` — 文档更新（体积说明、新来源）

---

## 任务 1：数据模型 — Video 来源常量与设置字段

**文件：**
- 修改：`Models/LauncherStateModels.cs:80-85`（`BackgroundSources`）和 `:184-190` 附近（`LauncherSettings` 字段）
- 测试：`tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperSettingsTests.cs`（创建）

- [ ] **步骤 1：编写失败的测试**

创建 `tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperSettingsTests.cs`：

```csharp
using Cafe.Launcher.Avalonia.Models;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

public class VideoWallpaperSettingsTests
{
    [Fact]
    public void BackgroundSources_Video_HasExpectedCode()
    {
        Assert.Equal("video", BackgroundSources.Video);
    }

    [Fact]
    public void LauncherSettings_VideoDefaults_AreMutedHalfVolumeEmptyPath()
    {
        var settings = new LauncherSettings();

        Assert.Equal("", settings.VideoBackgroundPath);
        Assert.True(settings.VideoBackgroundMuted);
        Assert.Equal(50, settings.VideoBackgroundVolume);
    }

    [Fact]
    public void LauncherSettings_DeepClone_PreservesVideoFields()
    {
        var settings = new LauncherSettings
        {
            VideoBackgroundPath = @"C:\videos\bg.mp4",
            VideoBackgroundMuted = false,
            VideoBackgroundVolume = 80,
        };

        var clone = settings.DeepClone();

        Assert.Equal(@"C:\videos\bg.mp4", clone.VideoBackgroundPath);
        Assert.False(clone.VideoBackgroundMuted);
        Assert.Equal(80, clone.VideoBackgroundVolume);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --filter "FullyQualifiedName~VideoWallpaperSettingsTests"`
预期：编译失败 — `BackgroundSources.Video`、`VideoBackgroundPath` 等未定义。

- [ ] **步骤 3：实现常量与字段**

在 `Models/LauncherStateModels.cs` 的 `BackgroundSources` 类（`:80-85`）添加：

```csharp
public static class BackgroundSources
{
    public const string Bundled = "bundled";
    public const string Remote = "remote";
    public const string Custom = "custom";
    public const string Video = "video";
}
```

在 `LauncherSettings` 中，紧接 `backgroundFillColor`（`:188-190`）之后添加：

```csharp
    [ObservableProperty]
    [property: JsonPropertyName("videoBackgroundPath")]
    private string videoBackgroundPath = "";

    [ObservableProperty]
    [property: JsonPropertyName("videoBackgroundMuted")]
    private bool videoBackgroundMuted = true;

    [ObservableProperty]
    [property: JsonPropertyName("videoBackgroundVolume")]
    private int videoBackgroundVolume = 50;
```

- [ ] **步骤 4：运行测试验证通过**

运行：同步骤 2。预期：3 个测试 PASS。

- [ ] **步骤 5：构建并 commit**

```bash
.\build.ps1
git add Models/LauncherStateModels.cs tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperSettingsTests.cs
git commit -m "feat(settings): 新增视频壁纸来源与设置字段"
```

---

## 任务 2：设置规范化 — 校验视频字段

**文件：**
- 修改：`Services/LauncherSettingsService.cs:225-258`（`NormalizeSettings`）
- 修改：`Services/LauncherSettingsService.cs` 末尾（新增 `internal static` 测试入口）
- 测试：`tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperSettingsTests.cs`

- [ ] **步骤 1：暴露规范化测试入口**

`NormalizeSettings` 当前是 `private static`。在 `LauncherSettingsService` 中新增一个 `internal static` 包装（供测试调用，不改变现有逻辑）。在 `private static LauncherSettings NormalizeSettings` 定义上方添加：

```csharp
    internal static LauncherSettings NormalizeForTesting(LauncherSettings settings) =>
        NormalizeSettings(settings);
```

- [ ] **步骤 2：编写失败的测试**

在 `VideoWallpaperSettingsTests.cs` 追加：

```csharp
    [Fact]
    public void Normalize_KeepsVideoBackgroundSource()
    {
        var result = Cafe.Launcher.Avalonia.Services.LauncherSettingsService
            .NormalizeForTesting(new LauncherSettings { BackgroundSource = BackgroundSources.Video });

        Assert.Equal(BackgroundSources.Video, result.BackgroundSource);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(150, 100)]
    [InlineData(60, 60)]
    public void Normalize_ClampsVideoVolume(int input, int expected)
    {
        var result = Cafe.Launcher.Avalonia.Services.LauncherSettingsService
            .NormalizeForTesting(new LauncherSettings { VideoBackgroundVolume = input });

        Assert.Equal(expected, result.VideoBackgroundVolume);
    }

    [Fact]
    public void Normalize_TrimsVideoPath()
    {
        var result = Cafe.Launcher.Avalonia.Services.LauncherSettingsService
            .NormalizeForTesting(new LauncherSettings { VideoBackgroundPath = "  C:\\v.mp4  " });

        Assert.Equal("C:\\v.mp4", result.VideoBackgroundPath);
    }
```

- [ ] **步骤 3：运行测试验证失败**

运行：`dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --filter "FullyQualifiedName~VideoWallpaperSettingsTests"`
预期：`Normalize_KeepsVideoBackgroundSource` 失败（video 被重置为 bundled）；`Normalize_ClampsVideoVolume`/`Normalize_TrimsVideoPath` 失败（未钳制/未裁剪）。

- [ ] **步骤 4：实现规范化**

在 `NormalizeSettings` 中，将 `BackgroundSource` 校验（`:225-230`）改为接受 video：

```csharp
        if (settings.BackgroundSource is not BackgroundSources.Bundled
            and not BackgroundSources.Remote
            and not BackgroundSources.Custom
            and not BackgroundSources.Video)
        {
            settings.BackgroundSource = BackgroundSources.Bundled;
        }
```

在 `settings.BackgroundFillColor = NormalizeColor(...)`（`:239`）之后添加：

```csharp
        settings.VideoBackgroundVolume = Math.Clamp(settings.VideoBackgroundVolume, 0, 100);
        settings.VideoBackgroundPath = settings.VideoBackgroundPath?.Trim() ?? "";
```

（`Math` 已通过 `using System;` 可用——文件首行已 `using System;`。）

- [ ] **步骤 5：运行测试验证通过**

运行：同步骤 3。预期：全部 PASS。

- [ ] **步骤 6：构建并 commit**

```bash
.\build.ps1
git add Services/LauncherSettingsService.cs tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperSettingsTests.cs
git commit -m "feat(settings): 规范化视频壁纸字段（枚举/音量/路径）"
```

---

## 任务 3：播放引擎接口与 Null 实现

**文件：**
- 创建：`Services/VideoWallpaper/IVideoWallpaperEngine.cs`
- 创建：`Services/VideoWallpaper/NullVideoWallpaperEngine.cs`
- 测试：暂无（接口与空实现由后续任务的 BackgroundViewModel 测试间接覆盖）

- [ ] **步骤 1：定义接口**

创建 `Services/VideoWallpaper/IVideoWallpaperEngine.cs`：

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Cafe.Launcher.Avalonia.Services.VideoWallpaper;

/// <summary>
/// Abstraction over native video playback. Decodes frames into a <see cref="WriteableBitmap"/>
/// so the existing background Image control renders them — no native HWND, overlays compose normally.
/// </summary>
internal interface IVideoWallpaperEngine : IDisposable
{
    /// <summary>The most recently decoded frame, or null before the first frame.</summary>
    WriteableBitmap? CurrentFrame { get; }

    /// <summary>Raised on the UI thread after <see cref="CurrentFrame"/> swaps to a new frame.</summary>
    event Action? FrameReady;

    /// <summary>Loads and starts playback. Returns false if the engine or file is unusable.</summary>
    Task<bool> LoadAsync(string path, CancellationToken cancellationToken);

    void Play();
    void Pause();
    void Stop();
    void SetVolume(int volume);
    void SetMuted(bool muted);

    /// <summary>Snapshots the current frame as a Bitmap for theme-color extraction, or null.</summary>
    Bitmap? CaptureFrame();
}
```

- [ ] **步骤 2：实现 Null 引擎**

创建 `Services/VideoWallpaper/NullVideoWallpaperEngine.cs`：

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Cafe.Launcher.Avalonia.Services.VideoWallpaper;

/// <summary>
/// No-op engine used when native libvlc is unavailable. Always fails to load so callers fall back
/// to the bundled image.
/// </summary>
internal sealed class NullVideoWallpaperEngine : IVideoWallpaperEngine
{
    public WriteableBitmap? CurrentFrame => null;

    public event Action? FrameReady { add { } remove { } }

    public Task<bool> LoadAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public void Play() { }
    public void Pause() { }
    public void Stop() { }
    public void SetVolume(int volume) { }
    public void SetMuted(bool muted) { }
    public Bitmap? CaptureFrame() => null;
    public void Dispose() { }
}
```

- [ ] **步骤 3：构建验证**

运行：`.\build.ps1`。预期：`0 个警告，0 个错误`（`FrameReady` 的空 add/remove 避免“未使用事件”警告）。

- [ ] **步骤 4：Commit**

```bash
git add Services/VideoWallpaper/IVideoWallpaperEngine.cs Services/VideoWallpaper/NullVideoWallpaperEngine.cs
git commit -m "feat(video): 新增视频壁纸引擎接口与空实现"
```

---

## 任务 4：测试用 Fake 引擎

**文件：**
- 创建：`tests/Cafe.Launcher.Avalonia.Tests/FakeVideoWallpaperEngine.cs`

- [ ] **步骤 1：实现 fake**

创建 `tests/Cafe.Launcher.Avalonia.Tests/FakeVideoWallpaperEngine.cs`。它记录调用、可配置加载成功/失败、可手动触发帧：

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cafe.Launcher.Avalonia.Services.VideoWallpaper;

namespace Cafe.Launcher.Avalonia.Tests;

internal sealed class FakeVideoWallpaperEngine : IVideoWallpaperEngine
{
    public bool LoadResult { get; set; } = true;
    public string? LoadedPath { get; private set; }
    public int PlayCount { get; private set; }
    public int PauseCount { get; private set; }
    public int StopCount { get; private set; }
    public int? LastVolume { get; private set; }
    public bool? LastMuted { get; private set; }
    public bool Disposed { get; private set; }

    public WriteableBitmap? CurrentFrame { get; private set; }

    public event Action? FrameReady;

    public Task<bool> LoadAsync(string path, CancellationToken cancellationToken)
    {
        LoadedPath = path;
        if (LoadResult)
        {
            CurrentFrame = new WriteableBitmap(
                new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }
        return Task.FromResult(LoadResult);
    }

    public void RaiseFrameReady() => FrameReady?.Invoke();

    public void Play() => PlayCount++;
    public void Pause() => PauseCount++;
    public void Stop() => StopCount++;
    public void SetVolume(int volume) => LastVolume = volume;
    public void SetMuted(bool muted) => LastMuted = muted;

    public Bitmap? CaptureFrame() => CurrentFrame;

    public void Dispose() => Disposed = true;
}
```

- [ ] **步骤 2：构建验证**

运行：`.\build.ps1`。预期：`0 个警告，0 个错误`。

- [ ] **步骤 3：Commit**

```bash
git add tests/Cafe.Launcher.Avalonia.Tests/FakeVideoWallpaperEngine.cs
git commit -m "test(video): 新增 fake 视频壁纸引擎"
```

---

## 任务 5：BackgroundViewModel Video 分支 + 引擎注入

**文件：**
- 修改：`ViewModels/BackgroundViewModel.cs`（构造函数注入引擎工厂、Video 分支、`SetPlaybackActive`）
- 测试：`tests/Cafe.Launcher.Avalonia.Tests/BackgroundViewModelTests.cs`

引擎按需创建（每次切到 Video 来源新建一个，切走时释放）。通过 `Func<IVideoWallpaperEngine>` 工厂注入，便于测试注入 fake。

- [ ] **步骤 1：编写失败的测试**

在 `BackgroundViewModelTests.cs` 顶部确保有：
`using Cafe.Launcher.Avalonia.Services.VideoWallpaper;`

追加测试（使用现有 internal 构造 + 新的引擎工厂重载，见步骤 3）：

```csharp
    [Fact]
    public async Task UpdateBackground_VideoSource_LoadsAndPlaysFrame()
    {
        var fake = new FakeVideoWallpaperEngine();
        var vm = CreateViewModelWithEngine(() => fake);
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Video,
            VideoBackgroundPath = @"C:\v.mp4",
            VideoBackgroundVolume = 70,
            VideoBackgroundMuted = false,
        };

        await vm.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        fake.RaiseFrameReady();

        Assert.Equal(@"C:\v.mp4", fake.LoadedPath);
        Assert.Equal(1, fake.PlayCount);
        Assert.Equal(70, fake.LastVolume);
        Assert.Equal(false, fake.LastMuted);
        Assert.Same(fake.CurrentFrame, vm.BackgroundImageSource);
    }

    [Fact]
    public async Task UpdateBackground_VideoLoadFails_FallsBackToBundled()
    {
        var fake = new FakeVideoWallpaperEngine { LoadResult = false };
        var vm = CreateViewModelWithEngine(() => fake);
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Video,
            VideoBackgroundPath = @"C:\missing.mp4",
        };

        await vm.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);

        Assert.NotNull(vm.BackgroundImageSource); // bundled fallback bitmap
        Assert.NotSame(fake.CurrentFrame, vm.BackgroundImageSource);
    }

    [Fact]
    public async Task SwitchingAwayFromVideo_DisposesEngine()
    {
        var fake = new FakeVideoWallpaperEngine();
        var vm = CreateViewModelWithEngine(() => fake);
        await vm.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Video, VideoBackgroundPath = @"C:\v.mp4" },
            null, CancellationToken.None);

        await vm.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Bundled },
            null, CancellationToken.None);

        Assert.True(fake.Disposed);
        Assert.Equal(1, fake.StopCount);
    }

    [Fact]
    public async Task SetPlaybackActive_PausesAndResumesVideo()
    {
        var fake = new FakeVideoWallpaperEngine();
        var vm = CreateViewModelWithEngine(() => fake);
        await vm.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Video, VideoBackgroundPath = @"C:\v.mp4" },
            null, CancellationToken.None);

        vm.SetPlaybackActive(false);
        vm.SetPlaybackActive(true);

        Assert.Equal(1, fake.PauseCount);
        Assert.Equal(2, fake.PlayCount); // initial Play + resume Play
    }
```

在该测试类中新增辅助方法（紧邻现有创建辅助方法），用 BackgroundViewModel 的新引擎工厂重载：

```csharp
    private static BackgroundViewModel CreateViewModelWithEngine(Func<IVideoWallpaperEngine> engineFactory)
    {
        return new BackgroundViewModel(
            CreateImageCacheService(),         // 复用本测试类已有的辅助；若无则用现有构造路径
            CreateDiagnostics(),
            _ => { },
            static path => new Bitmap(path),
            LoadStubBitmap,
            engineFactory);
    }
```

> 注：`CreateImageCacheService`/`CreateDiagnostics`/`LoadStubBitmap` 请复用 `BackgroundViewModelTests.cs` 中**已存在**的等价辅助（打开文件确认现有命名后对齐）。本任务只新增 `engineFactory` 这一参数链路。

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --filter "FullyQualifiedName~BackgroundViewModelTests"`
预期：编译失败 — `UpdateBackgroundImageAsync` 无 Video 分支、无 `SetPlaybackActive`、无引擎工厂构造。

- [ ] **步骤 3：实现 Video 分支与引擎生命周期**

在 `ViewModels/BackgroundViewModel.cs`：

顶部增加 `using Cafe.Launcher.Avalonia.Services.VideoWallpaper;`。

新增字段（`:24` 附近，`disposed` 旁）：

```csharp
    private readonly Func<IVideoWallpaperEngine> engineFactory;
    private IVideoWallpaperEngine? videoEngine;
    private LauncherSettings? activeVideoSettings;
    private bool playbackActive = true;
```

构造函数链：现有最内层 internal 构造（`:73-86`）新增 `engineFactory` 参数；现有 `:60-71` 与公共构造（`:43-58`）传入默认工厂 `static () => VideoWallpaperEngineFactory.Create()`（工厂在任务 7 提供；本任务先用 `static () => new NullVideoWallpaperEngine()` 占位，任务 7 替换为真实工厂）。

最内层构造体内保存：

```csharp
        this.engineFactory = engineFactory;
```

在 `UpdateBackgroundImageAsync` 的 `switch` 中，`case BackgroundSources.Custom` 之前插入：

```csharp
            case BackgroundSources.Video:
                if (!string.IsNullOrWhiteSpace(settings.VideoBackgroundPath))
                {
                    if (await TryStartVideoAsync(settings, cancellationToken))
                    {
                        return;
                    }
                }
                StopVideo();
                break;
```

> 重要：在方法开头、`ApplyBackgroundPresentation(settings)` 之后，若**来源不是 Video** 则先 `StopVideo();`，确保切走时停止并释放引擎。即在 `switch` 之前加：
> ```csharp
>         if (settings.BackgroundSource != BackgroundSources.Video)
>         {
>             StopVideo();
>         }
> ```

新增方法（放在 `SetBackgroundImage` 附近）：

```csharp
    private async Task<bool> TryStartVideoAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            StopVideo();
            var engine = engineFactory();
            var ok = await engine.LoadAsync(settings.VideoBackgroundPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ok)
            {
                engine.Dispose();
                return false;
            }

            videoEngine = engine;
            activeVideoSettings = settings;
            engine.SetVolume(settings.VideoBackgroundVolume);
            engine.SetMuted(settings.VideoBackgroundMuted);
            engine.FrameReady += OnVideoFrameReady;
            if (playbackActive)
            {
                engine.Play();
            }

            OnVideoFrameReady();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _ = diagnostics.MessageAsync(
                "Video wallpaper load failed",
                $"path: {settings.VideoBackgroundPath}\nexception: {ex.Message}",
                CancellationToken.None);
            return false;
        }
    }

    private void OnVideoFrameReady()
    {
        var frame = videoEngine?.CurrentFrame;
        if (frame is not null && activeVideoSettings is not null)
        {
            // 不经 SetBackgroundImage（那会 Dispose 旧帧）；视频帧由引擎双缓冲拥有。
            BackgroundImageSource = frame;
            if (activeVideoSettings.ThemeColorMode == ThemeColorModes.Wallpaper)
            {
                wallpaperChanged(activeVideoSettings);
            }
        }
    }

    private void StopVideo()
    {
        if (videoEngine is null)
        {
            return;
        }

        videoEngine.FrameReady -= OnVideoFrameReady;
        videoEngine.Stop();
        videoEngine.Dispose();
        videoEngine = null;
        activeVideoSettings = null;
    }

    public void SetPlaybackActive(bool active)
    {
        playbackActive = active;
        if (videoEngine is null)
        {
            return;
        }

        if (active)
        {
            videoEngine.Play();
        }
        else
        {
            videoEngine.Pause();
        }
    }
```

`GetBackgroundBitmap()` 改为：视频激活时返回引擎快照，否则返回当前位图：

```csharp
    public Bitmap? GetBackgroundBitmap()
    {
        return videoEngine?.CaptureFrame() ?? BackgroundImageSource as Bitmap;
    }
```

`Dispose()` 中（`:325-334`）在 disposed 守卫后增加 `StopVideo();`。

> 注意 `BackgroundImageSource` 在 Video 分支被设为引擎拥有的帧。`SetBackgroundImage`（图片路径用）会 `Dispose` 旧 `BackgroundImageSource`——因此切到非视频来源时务必先 `StopVideo()`（已在 switch 前处理），且 `StopVideo` 后下一次 `SetBackgroundImage` 会把已停止引擎的帧替换掉。引擎的 `Dispose` 负责释放其双缓冲，不要让 `SetBackgroundImage` 去 Dispose 视频帧——切换顺序保证：StopVideo→Dispose 引擎（释放帧）→SetBackgroundImage 设新图。为安全起见，`StopVideo` 末尾将 `BackgroundImageSource` 置回 `bundledImageLoader()` 之前不要 Dispose 当前帧。实现时让 `StopVideo` **不**触碰 `BackgroundImageSource`，由随后的 fallback `SetBackgroundImage(bundledImageLoader(), settings)` 负责赋新值；此时旧值是引擎帧，`SetBackgroundImage` 会尝试 Dispose 它——为避免二次释放，引擎 `Dispose` 后其 `WriteableBitmap` 仍是有效托管对象，`Dispose` 它是安全幂等的（Avalonia `Bitmap.Dispose` 可重入）。

- [ ] **步骤 4：运行测试验证通过**

运行：同步骤 2。预期：4 个新测试 PASS，且现有 BackgroundViewModel 测试不回归。

- [ ] **步骤 5：构建并 commit**

```bash
.\build.ps1
git add ViewModels/BackgroundViewModel.cs tests/Cafe.Launcher.Avalonia.Tests/BackgroundViewModelTests.cs
git commit -m "feat(video): BackgroundViewModel 视频分支与播放生命周期"
```

---

## 任务 6：LibVLC NuGet 包与运行时初始化

**文件：**
- 修改：`Cafe.Launcher.Avalonia.csproj:46-60`（PackageReference 区）
- 修改：`App.axaml.cs`（框架初始化处调用 `Core.Initialize()`）

- [ ] **步骤 1：添加包引用**

在 `Cafe.Launcher.Avalonia.csproj` 的 `<ItemGroup>` 包区（`:46` 起）添加：

```xml
    <PackageReference Include="LibVLCSharp" Version="3.9.4" />
    <PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.0.21" />
```

- [ ] **步骤 2：在应用启动时初始化 libvlc 核心**

在 `App.axaml.cs` 的框架初始化方法（`OnFrameworkInitializationCompleted`）最前面，DI 构建之前，添加：

```csharp
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
        }
        catch (Exception ex)
        {
            Services.Diagnostics.LocalDiagnostics.LogSync(
                "LibVLC", $"Core.Initialize failed; video wallpaper disabled: {ex.Message}");
        }
```

（确保 `using System;` 存在。失败不致命——后续引擎工厂会回退 Null 引擎。）

- [ ] **步骤 3：构建验证**

运行：`.\build.ps1`。预期：`0 个警告，0 个错误`，还原成功。

- [ ] **步骤 4：发布烟测（确认原生库随包）**

运行：`dotnet publish .\Cafe.Launcher.Avalonia.csproj -c Release -o publish`
预期：`publish/libvlc/win-x64/` 下存在 `libvlc.dll`、`libvlccore.dll` 与 `plugins/`。

- [ ] **步骤 5：Commit**

```bash
git add Cafe.Launcher.Avalonia.csproj App.axaml.cs
git commit -m "build(video): 引入 LibVLCSharp 并初始化原生核心"
```

---

## 任务 7：LibVLCSharp 引擎实现与工厂

**文件：**
- 创建：`Services/VideoWallpaper/VideoWallpaperEngine.cs`
- 创建：`Services/VideoWallpaper/VideoWallpaperEngineFactory.cs`
- 修改：`ViewModels/BackgroundViewModel.cs`（把任务 5 的占位工厂替换为真实工厂）

> 本任务依赖原生 libvlc，**不写单元测试**（与 XAML 同等豁免），由任务 11 的能力检测烟测覆盖。

- [ ] **步骤 1：实现引擎**

创建 `Services/VideoWallpaper/VideoWallpaperEngine.cs`。要点：`MediaPlayer` 用 `SetVideoFormatCallbacks` + `SetVideoCallbacks` 走内存渲染，双缓冲 `WriteableBitmap`，`Display` 回调里在 UI 线程拷贝并交换：

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace Cafe.Launcher.Avalonia.Services.VideoWallpaper;

internal sealed class VideoWallpaperEngine : IVideoWallpaperEngine
{
    private readonly LibVLC libVlc;
    private readonly MediaPlayer mediaPlayer;
    private readonly object frameLock = new();

    private IntPtr nativeBuffer;
    private int bufferSize;
    private uint videoWidth;
    private uint videoHeight;
    private uint stride;
    private WriteableBitmap? frontBuffer;
    private WriteableBitmap? backBuffer;
    private volatile bool frameDirty;
    private bool disposed;

    public VideoWallpaperEngine()
    {
        libVlc = new LibVLC("--no-osd", "--no-stats", "--no-video-title-show", "--input-repeat=65535");
        mediaPlayer = new MediaPlayer(libVlc) { EnableHardwareDecoding = true };
    }

    public WriteableBitmap? CurrentFrame => frontBuffer;

    public event Action? FrameReady;

    public Task<bool> LoadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Task.FromResult(false);
            }

            using var media = new Media(libVlc, new Uri(path));
            media.AddOption(":input-repeat=65535"); // loop
            mediaPlayer.SetVideoFormatCallbacks(OnVideoFormat, OnVideoCleanup);
            mediaPlayer.SetVideoCallbacks(OnLock, null, OnDisplay);
            return Task.FromResult(mediaPlayer.Play(media));
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    private uint OnVideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
        ref uint pitches, ref uint lines)
    {
        // 强制 RV32 (BGRA)
        WriteChroma(chroma, "RV32");
        videoWidth = width;
        videoHeight = height;
        stride = width * 4;
        pitches = stride;
        lines = height;
        bufferSize = (int)(stride * height);
        nativeBuffer = Marshal.AllocHGlobal(bufferSize);

        Dispatcher.UIThread.Post(() =>
        {
            var size = new PixelSize((int)videoWidth, (int)videoHeight);
            var dpi = new Vector(96, 96);
            frontBuffer = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            backBuffer = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        });
        return 1;
    }

    private static void WriteChroma(IntPtr chroma, string fourcc)
    {
        for (var i = 0; i < 4; i++)
        {
            Marshal.WriteByte(chroma, i, (byte)fourcc[i]);
        }
    }

    private void OnVideoCleanup(ref IntPtr opaque)
    {
        if (nativeBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(nativeBuffer);
            nativeBuffer = IntPtr.Zero;
        }
    }

    private IntPtr OnLock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, nativeBuffer);
        return IntPtr.Zero;
    }

    private void OnDisplay(IntPtr opaque, IntPtr picture)
    {
        if (frameDirty)
        {
            return; // 丢帧：上一帧尚未被 UI 消费
        }

        frameDirty = true;
        Dispatcher.UIThread.Post(() =>
        {
            CopyAndSwap();
            frameDirty = false;
            FrameReady?.Invoke();
        }, DispatcherPriority.Render);
    }

    private void CopyAndSwap()
    {
        if (backBuffer is null || nativeBuffer == IntPtr.Zero)
        {
            return;
        }

        using (var fb = backBuffer.Lock())
        {
            unsafe
            {
                Buffer.MemoryCopy(
                    nativeBuffer.ToPointer(), fb.Address.ToPointer(), bufferSize, bufferSize);
            }
        }

        (frontBuffer, backBuffer) = (backBuffer, frontBuffer);
    }

    public void Play() => mediaPlayer.Play();
    public void Pause() => mediaPlayer.SetPause(true);
    public void Stop() => mediaPlayer.Stop();
    public void SetVolume(int volume) => mediaPlayer.Volume = Math.Clamp(volume, 0, 100);
    public void SetMuted(bool muted) => mediaPlayer.Mute = muted;

    public Bitmap? CaptureFrame() => frontBuffer;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try { mediaPlayer.Stop(); } catch (Exception) { /* ignore */ }
        mediaPlayer.Dispose();
        libVlc.Dispose();
        OnVideoCleanup(ref Unsafe.AsRef<IntPtr>(null)); // free buffer if still allocated
        frontBuffer?.Dispose();
        backBuffer?.Dispose();
    }
}
```

> 实现注意：
> - 文件需要 `AllowUnsafeBlocks`。在 `.csproj` 的 `<PropertyGroup>` 增加 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`（任务 6 也可，但放此任务的步骤 2）。
> - `System.Runtime.CompilerServices.Unsafe` 用于 Dispose 释放缓冲；若引入麻烦，改为直接 `if (nativeBuffer != IntPtr.Zero) Marshal.FreeHGlobal(...)`，不要走 `OnVideoCleanup(ref ...)`。**采用直接释放写法**：
>   ```csharp
>   if (nativeBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(nativeBuffer); nativeBuffer = IntPtr.Zero; }
>   ```
> - LibVLCSharp 3.9.x 的 `SetVideoFormatCallbacks`/`SetVideoCallbacks` 委托签名以实际包为准；构建报错时按 IDE 提示对齐委托类型（这是预期的 API 对接微调，不是设计变更）。

- [ ] **步骤 2：开启 unsafe 并修正 Dispose**

在 `Cafe.Launcher.Avalonia.csproj` 的首个 `<PropertyGroup>` 添加：

```xml
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

将引擎 `Dispose` 中缓冲释放改为上面注释的直接写法，移除 `Unsafe` 用法与对应 `using`。

- [ ] **步骤 3：实现工厂**

创建 `Services/VideoWallpaper/VideoWallpaperEngineFactory.cs`：

```csharp
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services.VideoWallpaper;

internal static class VideoWallpaperEngineFactory
{
    /// <summary>Creates the native engine, or a Null engine if libvlc is unavailable.</summary>
    public static IVideoWallpaperEngine Create()
    {
        try
        {
            return new VideoWallpaperEngine();
        }
        catch (System.Exception ex)
        {
            LocalDiagnostics.LogSync("VideoWallpaper", $"Engine create failed; using null engine: {ex.Message}");
            return new NullVideoWallpaperEngine();
        }
    }
}
```

- [ ] **步骤 4：把 BackgroundViewModel 占位工厂换成真实工厂**

在 `ViewModels/BackgroundViewModel.cs` 中，把任务 5 引入的占位默认工厂 `static () => new NullVideoWallpaperEngine()` 改为 `static () => VideoWallpaperEngineFactory.Create()`（位于 `:60-71` 与公共构造对最内层构造的调用处）。

- [ ] **步骤 5：构建验证**

运行：`.\build.ps1`。预期：`0 个警告，0 个错误`。若委托签名报错，按步骤 1 注释对齐后再次构建。

- [ ] **步骤 6：Commit**

```bash
git add Services/VideoWallpaper/VideoWallpaperEngine.cs Services/VideoWallpaper/VideoWallpaperEngineFactory.cs ViewModels/BackgroundViewModel.cs Cafe.Launcher.Avalonia.csproj
git commit -m "feat(video): LibVLCSharp 引擎与工厂实现"
```

---

## 任务 8：设置选项与外观投影（Video 选项、音量、静音）

**文件：**
- 修改：`ViewModels/SettingsOptionsViewModel.cs:23-28,178-185`
- 修改：`ViewModels/SettingsAppearanceViewModel.cs`
- 测试：`tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperSettingsTests.cs`

- [ ] **步骤 1：编写失败的测试**

在 `VideoWallpaperSettingsTests.cs` 追加（验证选项含 video、外观投影随来源切换）：

```csharp
    [Fact]
    public void OptionsViewModel_BackgroundSource_IncludesVideo()
    {
        var options = new Cafe.Launcher.Avalonia.ViewModels.SettingsOptionsViewModel(
            TestLocalizationHelper.CreateLocalizer());

        Assert.Contains(options.BackgroundSource, o => o.Code == BackgroundSources.Video);
    }
```

> 注：`TestLocalizationHelper.CreateLocalizer()` 复用现有测试辅助（见 `tests/.../TestLocalizationHelper.cs`；若签名不同，对齐其现有用法）。

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test ... --filter "FullyQualifiedName~VideoWallpaperSettingsTests"`
预期：`OptionsViewModel_BackgroundSource_IncludesVideo` 失败（无 video 选项）。

- [ ] **步骤 3：添加 video 选项与本地化**

`SettingsOptionsViewModel.cs:23-28`：

```csharp
    public ObservableCollection<SettingOption> BackgroundSource { get; } =
    [
        new() { Code = BackgroundSources.Bundled },
        new() { Code = BackgroundSources.Remote },
        new() { Code = BackgroundSources.Custom },
        new() { Code = BackgroundSources.Video }
    ];
```

`:178-185` 的 `foreach (var option in BackgroundSource)` switch 增加：

```csharp
                BackgroundSources.Video => localizer.T("backgroundSourceVideo"),
```

- [ ] **步骤 4：外观投影属性**

在 `SettingsAppearanceViewModel.cs` 增加可观察属性（`:43-46` 附近）：

```csharp
    [ObservableProperty]
    private bool isVideoBackgroundSelected;

    [ObservableProperty]
    private int videoVolume = 50;

    [ObservableProperty]
    private bool isVideoMuted = true;
```

`Load`（`:66-87`）内补充：

```csharp
            IsVideoBackgroundSelected = settings.BackgroundSource == BackgroundSources.Video;
            VideoVolume = settings.VideoBackgroundVolume;
            IsVideoMuted = settings.VideoBackgroundMuted;
```

`OnCurrentSettingChanged`（`:170-177` 的 BackgroundSource 分支）补充：

```csharp
            IsVideoBackgroundSelected =
                editor.Current.BackgroundSource == BackgroundSources.Video;
```

新增 partial 变更处理（`:209` 的 `OnSelectedBackgroundFillColorChanged` 旁）：

```csharp
    partial void OnVideoVolumeChanged(int value) =>
        PushToEditor(settings => settings.VideoBackgroundVolume = value);

    partial void OnIsVideoMutedChanged(bool value) =>
        PushToEditor(settings => settings.VideoBackgroundMuted = value);
```

- [ ] **步骤 5：运行测试验证通过**

运行：同步骤 2，外加 `--filter "FullyQualifiedName~SettingsEditorTests|FullyQualifiedName~VideoWallpaperSettingsTests"`。预期：PASS，无回归。

- [ ] **步骤 6：构建并 commit**

```bash
.\build.ps1
git add ViewModels/SettingsOptionsViewModel.cs ViewModels/SettingsAppearanceViewModel.cs tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperSettingsTests.cs
git commit -m "feat(settings): 视频来源选项与音量/静音投影"
```

---

## 任务 9：视频文件选择命令与委托接线

**文件：**
- 修改：`ViewModels/SettingsViewModel.cs:31-33,286-298`
- 修改：`Views/MainWindow.axaml.cs:29-44`（ConfigureViewModel）+ 新增 `PickBackgroundVideoAsync`
- 测试：`tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperSettingsTests.cs`（命令逻辑，注入桩委托）

- [ ] **步骤 1：编写失败的测试**

在 `VideoWallpaperSettingsTests.cs` 追加（构造 SettingsViewModel 方式复用 `SettingsEditorTests`/现有 SettingsViewModel 测试的构造辅助；若无现成辅助，本测试可改为直接验证命令对 editor 的副作用）：

```csharp
    [Fact]
    public async Task ChooseBackgroundVideo_SetsPathAndVideoSource()
    {
        var (vm, editor) = TestSettingsViewModelFactory.Create();   // 复用现有测试工厂；无则内联构造
        vm.PickBackgroundVideoAsync = () => Task.FromResult<string?>(@"C:\v.mp4");

        await vm.ChooseBackgroundVideoCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\v.mp4", editor.Current.VideoBackgroundPath);
        Assert.Equal(BackgroundSources.Video, editor.Current.BackgroundSource);
    }
```

> 若仓库无 `TestSettingsViewModelFactory`，按 `SettingsEditorTests.cs` 中构造 `SettingsViewModel` 的现有写法内联创建（确认现有测试如何 new 出 `SettingsViewModel` 后对齐）。

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test ... --filter "FullyQualifiedName~VideoWallpaperSettingsTests"`
预期：编译失败 — 无 `PickBackgroundVideoAsync`、无 `ChooseBackgroundVideoCommand`。

- [ ] **步骤 3：实现委托与命令**

`SettingsViewModel.cs:31-33` 委托区追加：

```csharp
    public Func<Task<string?>>? PickBackgroundVideoAsync { get; set; }
```

`:286-298` 的 `ChooseBackgroundImageAsync` 之后追加：

```csharp
    [RelayCommand]
    private async Task ChooseBackgroundVideoAsync()
    {
        if (PickBackgroundVideoAsync is null)
            return;

        var pickedPath = await PickBackgroundVideoAsync();
        if (string.IsNullOrWhiteSpace(pickedPath))
            return;

        editor.Current.VideoBackgroundPath = pickedPath;
        editor.Current.BackgroundSource = BackgroundSources.Video;
    }
```

- [ ] **步骤 4：实现视频文件选择器并接线**

`Views/MainWindow.axaml.cs`：参照现有 `PickBackgroundImageAsync` 实现，新增：

```csharp
    private async Task<string?> PickBackgroundVideoAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = ViewModelTitleOrDefault(),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Video")
                {
                    Patterns = ["*.mp4", "*.webm", "*.mkv", "*.mov"]
                }
            ]
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
```

> 以现有 `PickBackgroundImageAsync` 的真实写法为准对齐命名（标题来源、返回方式）。`using Avalonia.Platform.Storage;` 应已存在（图片选择器已用）。

在 `ConfigureViewModel`（`:29-44`）追加：

```csharp
        viewModel.Settings.PickBackgroundVideoAsync = PickBackgroundVideoAsync;
```

- [ ] **步骤 5：运行测试验证通过**

运行：同步骤 2。预期：PASS。

- [ ] **步骤 6：构建并 commit**

```bash
.\build.ps1
git add ViewModels/SettingsViewModel.cs Views/MainWindow.axaml.cs tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperSettingsTests.cs
git commit -m "feat(settings): 视频文件选择命令与选择器接线"
```

---

## 任务 10：设置 UI 区块与三语本地化

**文件：**
- 修改：`Services/LocalizationService.cs:106-117,229+`（键 + Apply）
- 修改：`Assets/Locales/en.json`、`zh-Hans.json`、`ja.json`
- 修改：`Views/MainWindowSettingsOverlay.axaml:439` 附近（自定义背景区块之后新增视频区块）
- 测试：`UiStyleContractTests`（已有，构建后运行）

- [ ] **步骤 1：新增本地化键**

`Services/LocalizationService.cs:113-117` 区域追加 `[ObservableProperty]` 字段：

```csharp
    [ObservableProperty] private string backgroundSourceVideo = "";
    [ObservableProperty] private string videoBackground = "";
    [ObservableProperty] private string videoBackgroundDescription = "";
    [ObservableProperty] private string chooseVideo = "";
    [ObservableProperty] private string videoVolume = "";
    [ObservableProperty] private string videoMute = "";
    [ObservableProperty] private string chooseBackgroundVideoTitle = "";
```

`Apply()`（`:229+`）对应追加：

```csharp
        BackgroundSourceVideo = localizer.T("backgroundSourceVideo");
        VideoBackground = localizer.T("videoBackground");
        VideoBackgroundDescription = localizer.T("videoBackgroundDescription");
        ChooseVideo = localizer.T("chooseVideo");
        VideoVolume = localizer.T("videoVolume");
        VideoMute = localizer.T("videoMute");
        ChooseBackgroundVideoTitle = localizer.T("chooseBackgroundVideoTitle");
```

- [ ] **步骤 2：三个 locale 文件加键**

`Assets/Locales/en.json` 添加：

```json
  "backgroundSourceVideo": "Video",
  "videoBackground": "Video wallpaper",
  "videoBackgroundDescription": "Use a local video file as an animated background.",
  "chooseVideo": "Choose video…",
  "videoVolume": "Volume",
  "videoMute": "Mute",
  "chooseBackgroundVideoTitle": "Choose background video"
```

`Assets/Locales/zh-Hans.json` 添加：

```json
  "backgroundSourceVideo": "视频",
  "videoBackground": "视频壁纸",
  "videoBackgroundDescription": "使用本地视频文件作为动态背景。",
  "chooseVideo": "选择视频…",
  "videoVolume": "音量",
  "videoMute": "静音",
  "chooseBackgroundVideoTitle": "选择背景视频"
```

`Assets/Locales/ja.json` 添加：

```json
  "backgroundSourceVideo": "動画",
  "videoBackground": "動画の壁紙",
  "videoBackgroundDescription": "ローカルの動画ファイルを背景として使用します。",
  "chooseVideo": "動画を選択…",
  "videoVolume": "音量",
  "videoMute": "ミュート",
  "chooseBackgroundVideoTitle": "背景動画を選択"
```

> 注意：每个 JSON 文件最后一项不要漏/多逗号；插入位置紧邻已有 `backgroundSourceCustom` 等键之后。

- [ ] **步骤 3：新增设置 UI 区块**

在 `Views/MainWindowSettingsOverlay.axaml` 自定义背景区块（`IsVisible="{Binding Settings.Appearance.IsCustomBackgroundSelected}"`，`:439`）的**同级之后**，新增视频区块。严格使用设计令牌（间距/圆角/控件高度），不得出现裸色值/裸数值：

```xml
                                    <StackPanel Spacing="{StaticResource LauncherSpacingMd}"
                                                IsVisible="{Binding Settings.Appearance.IsVideoBackgroundSelected}">
                                        <TextBlock Text="{Binding Shell.I18n.VideoBackground}" Classes="section-title"/>
                                        <TextBlock Text="{Binding Shell.I18n.VideoBackgroundDescription}" Classes="caption" TextWrapping="Wrap"/>
                                        <Button Classes="flat-action" Command="{Binding Settings.ChooseBackgroundVideoCommand}">
                                            <TextBlock Text="{Binding Shell.I18n.ChooseVideo}" VerticalAlignment="Center"/>
                                        </Button>
                                        <Grid ColumnDefinitions="*,Auto">
                                            <TextBlock Grid.Column="0" Text="{Binding Shell.I18n.VideoMute}" VerticalAlignment="Center"/>
                                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding Settings.Appearance.IsVideoMuted, Mode=TwoWay}"/>
                                        </Grid>
                                        <Grid ColumnDefinitions="Auto,*" ColumnSpacing="{StaticResource LauncherSpacingSm}">
                                            <TextBlock Grid.Column="0" Text="{Binding Shell.I18n.VideoVolume}" VerticalAlignment="Center"/>
                                            <Slider Grid.Column="1" Minimum="0" Maximum="100"
                                                    Value="{Binding Settings.Appearance.VideoVolume, Mode=TwoWay}"
                                                    IsEnabled="{Binding !Settings.Appearance.IsVideoMuted}"/>
                                        </Grid>
                                    </StackPanel>
```

> 以现有 `flat-action` 按钮、`section-title`/`caption` 样式类和 `ToggleSwitch` 用法（`:497`、`:512`）为模板对齐。

- [ ] **步骤 4：构建并运行契约测试**

运行：
```
.\build.ps1
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --filter "FullyQualifiedName~UiStyleContractTests|FullyQualifiedName~LocalizationServiceTests"
```
预期：`0 个警告，0 个错误`；契约测试 PASS（无裸色值/裸数值）；本地化测试 PASS（三语键齐全）。

- [ ] **步骤 5：Commit**

```bash
git add Services/LocalizationService.cs Assets/Locales/en.json Assets/Locales/zh-Hans.json Assets/Locales/ja.json Views/MainWindowSettingsOverlay.axaml
git commit -m "feat(ui): 视频壁纸设置区块与三语本地化"
```

---

## 任务 11：DI 注册（可选）与真实引擎能力检测烟测

**文件：**
- 修改：`Services/ServiceConfiguration.cs`（如需将工厂登记为可注入；当前 BackgroundViewModel 自带默认工厂，可不改——见步骤 1 判断）
- 创建：`tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperEngineSmokeTests.cs`

- [ ] **步骤 1：确认 DI 现状**

`BackgroundViewModel` 通过默认参数使用 `VideoWallpaperEngineFactory.Create()`，无需额外 DI 注册。**本步骤无需改 ServiceConfiguration**——仅确认 `BackgroundViewModel` 在 DI 中仍可解析（它是 `AddTransient`）。运行：
```
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --filter "FullyQualifiedName~ServiceConfigurationTests"
```
预期：PASS（无回归）。

- [ ] **步骤 2：编写能力检测烟测**

创建 `tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperEngineSmokeTests.cs`。仿照 `GamePathValidatorTests` 的符号链接能力检测：libvlc 不可用时 `Skip`。

```csharp
using System;
using Cafe.Launcher.Avalonia.Services.VideoWallpaper;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

public class VideoWallpaperEngineSmokeTests
{
    private static bool LibVlcAvailable()
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [SkippableFact]
    public void Factory_Create_ReturnsUsableEngine_WhenLibVlcAvailable()
    {
        Skip.IfNot(LibVlcAvailable(), "libvlc native libraries not available in this environment.");

        using var engine = VideoWallpaperEngineFactory.Create();

        Assert.NotNull(engine);
        // 加载不存在的文件应安全失败而非抛出
        var ok = engine.LoadAsync("C:\\__nonexistent__.mp4", default).GetAwaiter().GetResult();
        Assert.False(ok);
    }
}
```

> `SkippableFact`/`Skip` 来自 `Xunit.SkippableFact` 包。确认主测试项目是否已引用——若 `GamePathValidatorTests` 的跳过用的是该包则直接复用；若用的是其他机制（如自定义 `[Fact]` + 运行时 `return`），对齐其现有跳过写法，不要新增依赖。

- [ ] **步骤 3：运行测试**

运行：`dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --filter "FullyQualifiedName~VideoWallpaperEngineSmokeTests"`
预期：PASS 或 Skipped（取决于运行环境是否有原生 libvlc）。

- [ ] **步骤 4：Commit**

```bash
git add tests/Cafe.Launcher.Avalonia.Tests/VideoWallpaperEngineSmokeTests.cs
git commit -m "test(video): 引擎能力检测烟测"
```

---

## 任务 12：窗口可见性 → 播放暂停/恢复

**文件：**
- 修改：`Views/MainWindow.axaml.cs`（WindowState 变化、tray Hide/Show 时调用 `SetPlaybackActive`）
- 测试：行为依赖真实窗口，由任务 5 的 `SetPlaybackActive` 单测覆盖；本任务做手动验证

- [ ] **步骤 1：在窗口状态变化处暂停/恢复**

`Views/MainWindow.axaml.cs`：重写 `OnPropertyChanged` 监听 `WindowState`（若已重写则在其中追加），最小化时暂停、正常时恢复：

```csharp
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty
            && DataContext is MainWindowViewModel vm)
        {
            var active = (WindowState)change.NewValue! != WindowState.Minimized;
            vm.Background.SetPlaybackActive(active);
        }
    }
```

`HideWindow`（tray 隐藏，`:189-191` 上下文）后调用：

```csharp
        (DataContext as MainWindowViewModel)?.Background.SetPlaybackActive(false);
```

`ShowWindow`（`:212-215`）末尾调用：

```csharp
        (DataContext as MainWindowViewModel)?.Background.SetPlaybackActive(true);
```

> 以 `MainWindow.axaml.cs` 现有 `using`/命名空间为准（`Avalonia`、`Cafe.Launcher.Avalonia.ViewModels`）。`WindowStateProperty`/`WindowState` 来自 `Avalonia.Controls`。

- [ ] **步骤 2：构建验证**

运行：`.\build.ps1`。预期：`0 个警告，0 个错误`。

- [ ] **步骤 3：手动验证（记录于 commit 说明）**

> 由于依赖真实窗口与 libvlc，自动化不覆盖。手动步骤（若本机有原生 libvlc）：设置视频壁纸 → 最小化窗口 → 观察 CPU 下降/暂停 → 恢复 → 续播。无 libvlc 环境跳过，逻辑由 `SetPlaybackActive` 单测保证。

- [ ] **步骤 4：Commit**

```bash
git add Views/MainWindow.axaml.cs
git commit -m "feat(video): 窗口不可见时暂停视频壁纸"
```

---

## 任务 13：文档更新与全量验证

**文件：**
- 修改：`CLAUDE.md`、`AGENTS.md`

- [ ] **步骤 1：更新文档**

在 `CLAUDE.md` 的 Settings reference 表新增三行（`videoBackgroundPath`/`videoBackgroundMuted`/`videoBackgroundVolume`），在 `BackgroundSources` 说明处加入 `video`，在 Services 表新增 `VideoWallpaperEngine`（LibVLCSharp 视频回调 → WriteableBitmap），并记录安装包体积 +~40MB。`AGENTS.md` 做等价同步。

- [ ] **步骤 2：全量构建与测试**

运行：
```
.\build.ps1
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug
```
预期：`0 个警告，0 个错误`；全部测试 PASS，仅能力缺失用例 Skip。

- [ ] **步骤 3：覆盖率验证**

运行：
```
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --collect:"XPlat Code Coverage" --results-directory .\TestResults\cov-video
```
检查 `BackgroundViewModel`（Video 分支）、`SettingsAppearanceViewModel`、设置规范化相关行覆盖 ≥80%。`VideoWallpaperEngine`/XAML 豁免。

- [ ] **步骤 4：Commit**

```bash
git add CLAUDE.md AGENTS.md
git commit -m "docs: 记录视频壁纸功能与体积影响"
```

---

## 自检

**1. 规格覆盖度：**
- 规格 §1 数据模型/设置 → 任务 1、2 ✓
- 规格 §2 播放引擎（隔离原生互操作）→ 任务 3、4、7 ✓
- 规格 §3 BackgroundViewModel 集成（Video 分支、SetPlaybackActive、主题色取帧）→ 任务 5、12 ✓
- 规格 §4 设置 UI + 本地化 → 任务 8、9、10 ✓
- 规格 §5 打包（LibVLC NuGet、Core.Initialize、体积记录）→ 任务 6、13 ✓
- 规格 §6 错误处理（回退 bundled、诊断、Dispose）→ 任务 5、7 ✓
- 规格 §7 测试策略（fake 单测 + 能力检测跳过）→ 任务 4、5、8、9、11 ✓
- 验收：契合度/填充色复用 → 任务 5（沿用 `ApplyBackgroundPresentation`，未改拉伸路径）✓

**2. 占位符扫描：** 计划内代码块均为实际内容；对“以现有辅助对齐”的注记均指向具体已存在文件（`BackgroundViewModelTests.cs`、`TestLocalizationHelper.cs`、`GamePathValidatorTests.cs`、现有 `PickBackgroundImageAsync`），属于必要的代码库对接说明，非含糊占位。

**3. 类型一致性：** `IVideoWallpaperEngine` 成员（`CurrentFrame`/`FrameReady`/`LoadAsync`/`Play`/`Pause`/`Stop`/`SetVolume`/`SetMuted`/`CaptureFrame`/`Dispose`）在接口（任务 3）、Null 实现（任务 3）、Fake（任务 4）、真实实现（任务 7）、调用方 BackgroundViewModel（任务 5）中签名一致。`SetPlaybackActive`、`ChooseBackgroundVideoCommand`、`PickBackgroundVideoAsync`、`VideoVolume`/`IsVideoMuted`/`IsVideoBackgroundSelected`、本地化键命名前后统一。

**已知对接风险（实现时按编译器对齐，非设计变更）：**
- LibVLCSharp 3.9.x 视频回调委托签名以实际包为准。
- 主测试项目的“跳过”机制（SkippableFact vs 其他）以现有 `GamePathValidatorTests` 实写为准。
- 若干测试构造辅助（SettingsViewModel/Localizer）以现有测试文件实写为准。
