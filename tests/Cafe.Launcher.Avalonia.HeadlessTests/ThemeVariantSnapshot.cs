using System;
using Avalonia;
using Avalonia.Styling;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// 把 <see cref="Application.RequestedThemeVariant"/> 置为指定值，并在释放时还原为进入前的值。
/// </summary>
/// <remarks>
/// 无头套件共享一个 <see cref="Application"/> 且整套按程序集串行执行（见 AssemblyInfo），
/// 因此一个用例改完变体不复位，后面每个用例就都跟着它的值渲染——截图截到亮色还是暗色取决于
/// 谁先跑（AUD-TEST-013）。凡是**主动改变体**的用例都走这里还原，而不是各写一遍
/// <c>previousTheme</c> 加 <c>try/finally</c>；golden 那条路径反过来，由
/// <c>PrepareGoldenWindow</c> 每次显式钉住基线值，与谁先跑无关。
/// </remarks>
internal sealed class ThemeVariantSnapshot : IDisposable
{
    private readonly Application application;
    private readonly ThemeVariant? previous;
    private bool disposed;

    private ThemeVariantSnapshot(Application application, ThemeVariant variant)
    {
        this.application = application;
        previous = application.RequestedThemeVariant;
        application.RequestedThemeVariant = variant;
    }

    /// <summary>
    /// 记下当前变体并改为 <paramref name="variant"/>。无头宿主未起时直接失败：那说明调用点
    /// 本身写错了，静默返回空快照只会把问题挪到更难读的地方。
    /// </summary>
    public static ThemeVariantSnapshot Capture(ThemeVariant variant)
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Headless application is not initialised.");
        return new ThemeVariantSnapshot(application, variant);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        application.RequestedThemeVariant = previous;
    }
}
