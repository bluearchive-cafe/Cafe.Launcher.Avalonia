using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// <see cref="ISystemTrayPlatform"/> 的共享测试替身：记录 Initialize/UpdateText 调用次数、
/// 收到的菜单文本与回调委托、是否已释放；<see cref="InitializeResult"/> 可配置 Initialize
/// 的返回值。原 SystemTrayServiceTests（记录型）与 MainWindowHeadlessTests（无操作型）
/// 各有一份，此处合并为两者能力的并集：默认行为即无操作型 fake 的行为
/// （Initialize 返回 true，不关心计数时直接忽略公开属性即可）。
/// </summary>
internal sealed class TestTrayPlatform : ISystemTrayPlatform
{
    public bool InitializeResult { get; set; } = true;

    public int InitializeCount { get; private set; }

    public int UpdateCount { get; private set; }

    public bool Disposed { get; private set; }

    public SystemTrayMenuText Text { get; private set; } = new("", "", "", "", "");

    public Action? ShowWindow { get; private set; }

    public Action? ExitApplication { get; private set; }

    public bool Initialize(
        SystemTrayMenuText text,
        Action showWindow,
        Action exitApplication)
    {
        InitializeCount++;
        Text = text;
        ShowWindow = showWindow;
        ExitApplication = exitApplication;
        return InitializeResult;
    }

    public void UpdateText(SystemTrayMenuText text)
    {
        UpdateCount++;
        Text = text;
    }

    public void Dispose()
    {
        Disposed = true;
    }
}
