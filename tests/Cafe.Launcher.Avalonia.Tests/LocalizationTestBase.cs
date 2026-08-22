using System.Globalization;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Restores process-wide culture state after tests that exercise localization.
/// </summary>
public abstract class LocalizationTestBase : IDisposable
{
    private readonly CultureInfo currentCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo currentUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? defaultThreadCulture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? defaultThreadUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
    private bool disposed;

    public virtual void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CultureInfo.CurrentCulture = currentCulture;
        CultureInfo.CurrentUICulture = currentUiCulture;
        CultureInfo.DefaultThreadCurrentCulture = defaultThreadCulture;
        CultureInfo.DefaultThreadCurrentUICulture = defaultThreadUiCulture;
    }
}
