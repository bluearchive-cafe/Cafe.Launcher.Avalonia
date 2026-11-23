using System;
using System.Text.Json.Serialization;

namespace Cafe.Launcher.Core.Models;

/// <summary>一个明确游戏条目的可编辑设置；不包含全局偏好或当前查看的条目身份。</summary>
public sealed class GameSettings : SettingsModel
{
    private string gamePath = "";
    private string patchUrlGroup = PatchUrlGroups.Official;
    private string launchCheckMode = LaunchCheckModes.LocalManifest;
    private GameRuntimeSettings gameRuntime = new();
    private string afterLaunchBehavior = AfterLaunchBehaviors.Minimize;
    private string resourcePanelUid = "";
    private string resourcePanelUidSource = ResourcePanelUidSources.Auto;
    private bool useBackgroundOverride;
    private string backgroundSource = BackgroundSources.Bundled;
    private string customBackgroundPath = "";
    private string backgroundFit = BackgroundFits.UniformToFill;
    private string backgroundFillColor = "#FF000000";
    private int backgroundVideoVolume;
    private bool backgroundVideoMuted;

    /// <summary>此条目的安装目录；空值表示尚未选择目录。</summary>
    [JsonPropertyName("gamePath")]
    public string GamePath { get => gamePath; set => SetProperty(ref gamePath, value); }

    /// <summary>此条目的下载源代码；可用来源由对应游戏能力约束。</summary>
    [JsonPropertyName("patchUrlGroup")]
    public string PatchUrlGroup { get => patchUrlGroup; set => SetProperty(ref patchUrlGroup, value); }

    /// <summary>启动此条目前采用的文件检查方式。</summary>
    [JsonPropertyName("launchCheckMode")]
    public string LaunchCheckMode { get => launchCheckMode; set => SetProperty(ref launchCheckMode, value); }

    /// <summary>此条目的运行器与兼容环境；不与其他游戏共享可编辑对象。</summary>
    [JsonPropertyName("gameRuntime")]
    public GameRuntimeSettings GameRuntime { get => gameRuntime; set => SetProperty(ref gameRuntime, value); }

    /// <summary>成功启动此条目后请求的启动器窗口行为。</summary>
    [JsonPropertyName("afterLaunchBehavior")]
    public string AfterLaunchBehavior { get => afterLaunchBehavior; set => SetProperty(ref afterLaunchBehavior, value); }

    /// <summary>此条目适用的汉化资源面板 UID；不适用的游戏保持空值。</summary>
    [JsonPropertyName("resourcePanelUid")]
    public string ResourcePanelUid { get => resourcePanelUid; set => SetProperty(ref resourcePanelUid, value); }

    /// <summary>此条目资源面板 UID 的来源代码。</summary>
    [JsonPropertyName("resourcePanelUidSource")]
    public string ResourcePanelUidSource { get => resourcePanelUidSource; set => SetProperty(ref resourcePanelUidSource, value); }

    /// <summary>此条目是否使用自己的完整背景配置；关闭时继承全局默认背景。</summary>
    [JsonPropertyName("useBackgroundOverride")]
    public bool UseBackgroundOverride { get => useBackgroundOverride; set => SetProperty(ref useBackgroundOverride, value); }

    /// <summary>此条目专属背景的来源。</summary>
    [JsonPropertyName("backgroundSource")]
    public string BackgroundSource { get => backgroundSource; set => SetProperty(ref backgroundSource, value); }

    /// <summary>此条目选择的本机图片、视频或文件夹。</summary>
    [JsonPropertyName("customBackgroundPath")]
    public string CustomBackgroundPath { get => customBackgroundPath; set => SetProperty(ref customBackgroundPath, value); }

    /// <summary>此条目专属背景的适配方式。</summary>
    [JsonPropertyName("backgroundFit")]
    public string BackgroundFit { get => backgroundFit; set => SetProperty(ref backgroundFit, value); }

    /// <summary>此条目专属背景周围的填充色。</summary>
    [JsonPropertyName("backgroundFillColor")]
    public string BackgroundFillColor { get => backgroundFillColor; set => SetProperty(ref backgroundFillColor, value); }

    /// <summary>此条目专属背景视频保留的音量。</summary>
    [JsonPropertyName("backgroundVideoVolume")]
    public int BackgroundVideoVolume { get => backgroundVideoVolume; set => SetProperty(ref backgroundVideoVolume, value); }

    /// <summary>此条目专属背景视频是否静音而不丢弃音量。</summary>
    [JsonPropertyName("backgroundVideoMuted")]
    public bool BackgroundVideoMuted { get => backgroundVideoMuted; set => SetProperty(ref backgroundVideoMuted, value); }

    /// <summary>复制全部字段与嵌套运行环境；显式空运行环境留待格式校验或旧值归一化，不在克隆时修复。</summary>
    public GameSettings DeepClone() => new()
    {
        GamePath = GamePath,
        PatchUrlGroup = PatchUrlGroup,
        LaunchCheckMode = LaunchCheckMode,
        GameRuntime = GameRuntime?.DeepClone()!,
        AfterLaunchBehavior = AfterLaunchBehavior,
        ResourcePanelUid = ResourcePanelUid,
        ResourcePanelUidSource = ResourcePanelUidSource,
        UseBackgroundOverride = UseBackgroundOverride,
        BackgroundSource = BackgroundSource,
        CustomBackgroundPath = CustomBackgroundPath,
        BackgroundFit = BackgroundFit,
        BackgroundFillColor = BackgroundFillColor,
        BackgroundVideoVolume = BackgroundVideoVolume,
        BackgroundVideoMuted = BackgroundVideoMuted
    };

    private static readonly (string Name, Func<GameSettings, object?> Read)[] ComparedProperties =
    [
        (nameof(GamePath), settings => settings.GamePath),
        (nameof(PatchUrlGroup), settings => settings.PatchUrlGroup),
        (nameof(LaunchCheckMode), settings => settings.LaunchCheckMode),
        (nameof(GameRuntime), settings => settings.GameRuntime),
        (nameof(AfterLaunchBehavior), settings => settings.AfterLaunchBehavior),
        (nameof(ResourcePanelUid), settings => settings.ResourcePanelUid),
        (nameof(ResourcePanelUidSource), settings => settings.ResourcePanelUidSource),
        (nameof(UseBackgroundOverride), settings => settings.UseBackgroundOverride),
        (nameof(BackgroundSource), settings => settings.BackgroundSource),
        (nameof(CustomBackgroundPath), settings => settings.CustomBackgroundPath),
        (nameof(BackgroundFit), settings => settings.BackgroundFit),
        (nameof(BackgroundFillColor), settings => settings.BackgroundFillColor),
        (nameof(BackgroundVideoVolume), settings => settings.BackgroundVideoVolume),
        (nameof(BackgroundVideoMuted), settings => settings.BackgroundVideoMuted)
    ];

    /// <summary>仅将用户相对基线明确修改的字段合并到最新已保存值，保留外部更新。</summary>
    internal void ApplyDraftChanges(GameSettings draft, GameSettings baseline)
    {
        if (!LauncherSettings.ValuesEqual(draft.GamePath, baseline.GamePath)) GamePath = draft.GamePath;
        if (!LauncherSettings.ValuesEqual(draft.PatchUrlGroup, baseline.PatchUrlGroup)) PatchUrlGroup = draft.PatchUrlGroup;
        if (!LauncherSettings.ValuesEqual(draft.LaunchCheckMode, baseline.LaunchCheckMode)) LaunchCheckMode = draft.LaunchCheckMode;
        if (!LauncherSettings.ValuesEqual(draft.AfterLaunchBehavior, baseline.AfterLaunchBehavior)) AfterLaunchBehavior = draft.AfterLaunchBehavior;
        if (!LauncherSettings.ValuesEqual(draft.ResourcePanelUid, baseline.ResourcePanelUid)) ResourcePanelUid = draft.ResourcePanelUid;
        if (!LauncherSettings.ValuesEqual(draft.ResourcePanelUidSource, baseline.ResourcePanelUidSource)) ResourcePanelUidSource = draft.ResourcePanelUidSource;
        if (!LauncherSettings.ValuesEqual(draft.UseBackgroundOverride, baseline.UseBackgroundOverride)) UseBackgroundOverride = draft.UseBackgroundOverride;
        if (!LauncherSettings.ValuesEqual(draft.BackgroundSource, baseline.BackgroundSource)) BackgroundSource = draft.BackgroundSource;
        if (!LauncherSettings.ValuesEqual(draft.CustomBackgroundPath, baseline.CustomBackgroundPath)) CustomBackgroundPath = draft.CustomBackgroundPath;
        if (!LauncherSettings.ValuesEqual(draft.BackgroundFit, baseline.BackgroundFit)) BackgroundFit = draft.BackgroundFit;
        if (!LauncherSettings.ValuesEqual(draft.BackgroundFillColor, baseline.BackgroundFillColor)) BackgroundFillColor = draft.BackgroundFillColor;
        if (!LauncherSettings.ValuesEqual(draft.BackgroundVideoVolume, baseline.BackgroundVideoVolume)) BackgroundVideoVolume = draft.BackgroundVideoVolume;
        if (!LauncherSettings.ValuesEqual(draft.BackgroundVideoMuted, baseline.BackgroundVideoMuted)) BackgroundVideoMuted = draft.BackgroundVideoMuted;
        if (GameRuntime is not null && draft.GameRuntime is not null && baseline.GameRuntime is not null)
        {
            GameRuntime.ApplyDraftChanges(draft.GameRuntime, baseline.GameRuntime);
        }
        else if (!LauncherSettings.ValuesEqual(draft.GameRuntime, baseline.GameRuntime))
        {
            GameRuntime = draft.GameRuntime?.DeepClone()!;
        }
    }

    /// <summary>文件任务只保护会影响安装与运行的设置，纯背景编辑不占用文件设置租约。</summary>
    public bool HasSameOperationSettingsState(GameSettings other) =>
        LauncherSettings.ValuesEqual(GamePath, other.GamePath)
        && LauncherSettings.ValuesEqual(PatchUrlGroup, other.PatchUrlGroup)
        && LauncherSettings.ValuesEqual(LaunchCheckMode, other.LaunchCheckMode)
        && LauncherSettings.ValuesEqual(GameRuntime, other.GameRuntime)
        && LauncherSettings.ValuesEqual(AfterLaunchBehavior, other.AfterLaunchBehavior)
        && LauncherSettings.ValuesEqual(ResourcePanelUid, other.ResourcePanelUid)
        && LauncherSettings.ValuesEqual(ResourcePanelUidSource, other.ResourcePanelUidSource);

    /// <summary>逐字段比较游戏设置及嵌套运行环境，用于根设置的草稿状态同一性。</summary>
    public bool HasSameSettingsState(GameSettings? other)
    {
        if (other is null)
        {
            return false;
        }

        foreach (var (_, read) in ComparedProperties)
        {
            if (!LauncherSettings.ValuesEqual(read(this), read(other)))
            {
                return false;
            }
        }

        return true;
    }
}
