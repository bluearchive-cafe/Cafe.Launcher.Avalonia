namespace Cafe.Launcher.Core.Models;

/// <summary>
/// Blue Archive 客户端 <c>DeviceOption</c> 文件中的设备级选项快照。
/// </summary>
/// <remarks>
/// 只读模型。字段语义来自 1.74 PC 客户端程序集（<c>MX.SaveData.DeviceOption</c>）与实机文件；
/// <c>ScreenModeType</c>、<c>ScreenRatioType</c>、<c>renderScale</c> 等 PC 字段已校准。
/// 读取方在必需字段缺失或已知枚举越界时应拒绝整份文件，不能部分套用。
/// </remarks>
public sealed record GameDeviceOptions
{
    /// <summary>四路音量；<c>Master</c> 只在较新版本出现，旧文件为 <see langword="null"/>。</summary>
    public required GameDeviceAudioSettings Audio { get; init; }

    /// <summary>画质档位；游戏字段名是 <c>Resolution</c>，但含义不是像素分辨率。</summary>
    public required GameDeviceQualityPreset QualityPreset { get; init; }

    /// <summary>帧率档位；当前只能确认 High 与 Normal 两种值。</summary>
    public required GameDeviceFrameRateMode FrameRate { get; init; }

    /// <summary>绘制调用模式。</summary>
    public required GameDeviceDrawcallMode DrawcallMode { get; init; }

    /// <summary>后处理开关；文件中的 <c>ToggleValue</c> 以 0 表示 On。</summary>
    public required bool AllowPostProcessing { get; init; }

    /// <summary>抗锯齿开关；1.74 PC 客户端的 <c>AntiAliasing</c> 枚举只有 Off/On。</summary>
    public required GameDeviceAntiAliasing AntiAliasing { get; init; }

    /// <summary>战斗黑边开关；文件字段默认值为 1，表示关闭。</summary>
    public required bool LetterBoxInBattle { get; init; }

    /// <summary>音频数据版本，用于游戏迁移；读取方不得改写。</summary>
    public int? AudioVolumeVersion { get; init; }

    /// <summary>屏幕模式；0 为全屏、1 为窗口。</summary>
    public GameDeviceScreenMode? ScreenMode { get; init; }

    /// <summary>屏幕比例；0 为 4:3、1 为 16:9。</summary>
    public GameDeviceScreenRatio? ScreenRatio { get; init; }

    /// <summary>垂直同步开关。</summary>
    public bool? VSync { get; init; }

    /// <summary>后台声音开关。</summary>
    public bool? BackgroundSound { get; init; }

    /// <summary>控制器提示开关。</summary>
    public bool? ControlDisplay { get; init; }

    /// <summary>鼠标光标开关。</summary>
    public bool? MouseCursor { get; init; }

    /// <summary>振动开关。</summary>
    public bool? Vibration { get; init; }

    /// <summary>控制器光标速度。</summary>
    public GameDeviceControllerCursorSpeed? ControllerCursorSpeed { get; init; }

    /// <summary>光标自动聚焦开关。</summary>
    public bool? CursorAutoFocus { get; init; }

    /// <summary>下载完成提醒开关。</summary>
    public bool? NotificationAfterDownloadAlarm { get; init; }

    /// <summary>窗口位置与大小；旧版本文件没有该字段。</summary>
    public GameDeviceWindowRect? WindowRect { get; init; }

    /// <summary>渲染缩放档位；0 为 100%，5 为 200%。</summary>
    public GameDeviceRenderScale? RenderScale { get; init; }

    /// <summary>标题画面音轨编号；用户可在标题界面手动修改，不是内部状态。</summary>
    public long? TitleVideoAudioId { get; init; }

    /// <summary>游戏是否已完成设备选项初始化；缺失表示旧文件或尚未初始化。</summary>
    public bool? IsInitialized { get; init; }
}

/// <summary>设备选项中的四路音量；<c>Master</c> 只有在较新版本文件中才存在。</summary>
public sealed record GameDeviceAudioSettings(
    GameDeviceAudioChannel Bgm,
    GameDeviceAudioChannel Fxs,
    GameDeviceAudioChannel Voice,
    GameDeviceAudioChannel? Master);

/// <summary>
/// 单路音量。游戏用 <c>Mute.On = 0</c> 表示静音、<c>Mute.Off = 1</c> 表示未静音，
/// 这里已归一为 <see cref="IsMuted"/>；<see cref="Volume"/> 的范围是 0–1。
/// </summary>
public sealed record GameDeviceAudioChannel(bool IsMuted, float Volume);

/// <summary>画质档位；对应游戏程序集里的 <c>Resolution</c> 枚举，不是像素分辨率。</summary>
public enum GameDeviceQualityPreset
{
    /// <summary>游戏枚举的 <c>VeryHigh</c>。</summary>
    VeryHigh = 0,
    /// <summary>游戏枚举的 <c>High</c>。</summary>
    High = 1,
    /// <summary>游戏枚举的 <c>Normal</c>。</summary>
    Normal = 2,
    /// <summary>游戏枚举的 <c>Low</c>。</summary>
    Low = 3
}

/// <summary>帧率档位；实机确认 0 为 60 FPS、1 为 30 FPS。</summary>
public enum GameDeviceFrameRateMode
{
    /// <summary>60 FPS；游戏枚举名为 <c>High</c>。</summary>
    Sixty = 0,
    /// <summary>30 FPS；游戏枚举名为 <c>Normal</c>。</summary>
    Thirty = 1
}

/// <summary>屏幕模式；实机确认 1 为窗口，0 为全屏（由改动前快照推断）。</summary>
public enum GameDeviceScreenMode
{
    /// <summary>全屏；改动前快照为 0。</summary>
    FullScreen = 0,
    /// <summary>窗口；2026-11-22 实机确认。</summary>
    Windowed = 1
}

/// <summary>屏幕比例；实机确认 0 为 4:3、1 为 16:9。</summary>
public enum GameDeviceScreenRatio
{
    /// <summary>4:3；2026-11-22 实机 1280×960 确认。</summary>
    FourByThree = 0,
    /// <summary>16:9；1.74 快照 1280×720 确认。</summary>
    SixteenByNine = 1
}

/// <summary>绘制调用模式；对应游戏程序集里的 <c>DrawcallMode</c> 枚举。</summary>
public enum GameDeviceDrawcallMode
{
    /// <summary>SRP Batcher。</summary>
    SrpBatcher = 0,
    /// <summary>动态合批。</summary>
    DynamicBatching = 1,
    /// <summary>关闭合批。</summary>
    Off = 2
}

/// <summary>抗锯齿开关；对应 1.74 PC 客户端 <c>AntiAliasing</c> 枚举的 Off/On。</summary>
public enum GameDeviceAntiAliasing
{
    /// <summary>关闭。</summary>
    Off = 0,
    /// <summary>开启。</summary>
    On = 1
}

/// <summary>
/// 渲染缩放档位；对应游戏程序集里的 <c>RenderScaleMultiplier</c>，
/// 0 为 100%，之后每档增加 20%。
/// </summary>
public enum GameDeviceRenderScale
{
    /// <summary>100%。</summary>
    Percent100 = 0,
    /// <summary>120%。</summary>
    Percent120 = 1,
    /// <summary>140%。</summary>
    Percent140 = 2,
    /// <summary>160%。</summary>
    Percent160 = 3,
    /// <summary>180%。</summary>
    Percent180 = 4,
    /// <summary>200%。</summary>
    Percent200 = 5
}

/// <summary>控制器光标速度；对应游戏程序集里的 <c>ControllerCursorSpeed</c> 枚举。</summary>
public enum GameDeviceControllerCursorSpeed
{
    /// <summary>标准。</summary>
    Normal = 0,
    /// <summary>快。</summary>
    Fast = 1,
    /// <summary>慢。</summary>
    Slow = 2
}

/// <summary>窗口位置与大小；对应 <c>windowRect</c> 的 <c>m_XMin</c>、<c>m_YMin</c>、<c>m_Width</c>、<c>m_Height</c>。</summary>
public sealed record GameDeviceWindowRect(double X, double Y, double Width, double Height);
