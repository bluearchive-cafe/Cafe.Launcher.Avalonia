namespace Cafe.Launcher.Core.Models;

/// <summary>
/// Blue Archive 客户端 <c>DeviceOption</c> 文件中的设备级选项快照。
/// </summary>
/// <remarks>
/// 只读模型。稳定字段的语义来自 2026-07 的游戏程序集 dump 与 1.74 实机文件；
/// <c>ScreenModeType</c>、<c>ScreenRatioType</c> 等 PC 窗口字段尚未校准，保留原始整数，
/// Core 不猜测其含义。读取方在必需字段缺失或已知枚举越界时应拒绝整份文件，不能部分套用。
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

    /// <summary>抗锯齿档位。</summary>
    public required GameDeviceAntiAliasing AntiAliasing { get; init; }

    /// <summary>战斗黑边开关；文件字段默认值为 1，表示关闭。</summary>
    public required bool LetterBoxInBattle { get; init; }

    /// <summary>音频数据版本，用于游戏迁移；读取方不得改写。</summary>
    public int? AudioVolumeVersion { get; init; }

    /// <summary>屏幕模式；1.74 实机确认 1 为窗口。</summary>
    public GameDeviceScreenMode? ScreenMode { get; init; }

    /// <summary>屏幕比例；1.74 实机确认 0 为 4:3、1 为 16:9。</summary>
    public GameDeviceScreenRatio? ScreenRatio { get; init; }

    /// <summary>垂直同步原始值；尚不能确定它使用 ToggleValue 还是布尔语义。</summary>
    public int? VSync { get; init; }

    /// <summary>后台声音开关的原始值。</summary>
    public int? BackgroundSound { get; init; }

    /// <summary>控制器提示显示的原始值。</summary>
    public int? ControlDisplay { get; init; }

    /// <summary>鼠标光标相关原始值。</summary>
    public int? MouseCursor { get; init; }

    /// <summary>振动开关的原始值。</summary>
    public int? Vibration { get; init; }

    /// <summary>控制器光标速度的原始值。</summary>
    public int? ControllerCursorSpeed { get; init; }

    /// <summary>光标自动聚焦的原始值。</summary>
    public int? CursorAutoFocus { get; init; }

    /// <summary>下载完成提醒的原始值。</summary>
    public int? NotificationAfterDownloadAlarm { get; init; }

    /// <summary>窗口位置与大小；旧版本文件没有该字段。</summary>
    public GameDeviceWindowRect? WindowRect { get; init; }

    /// <summary>渲染缩放原始值；0 可能是“默认”哨兵，5 是实机改动后的值，映射尚未校准。</summary>
    public double? RenderScale { get; init; }

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

/// <summary>抗锯齿档位；对应游戏程序集里的 <c>AntiAliasing</c> 枚举。</summary>
public enum GameDeviceAntiAliasing
{
    /// <summary>关闭。</summary>
    Off = 0,
    /// <summary>低档。</summary>
    Low = 1,
    /// <summary>高档。</summary>
    High = 2
}

/// <summary>窗口位置与大小；对应 <c>windowRect</c> 的 <c>m_XMin</c>、<c>m_YMin</c>、<c>m_Width</c>、<c>m_Height</c>。</summary>
public sealed record GameDeviceWindowRect(double X, double Y, double Width, double Height);
