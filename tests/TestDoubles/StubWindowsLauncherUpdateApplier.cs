using Cafe.Launcher.Avalonia.Services.Update;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// Stands in for the Windows update helper. <see cref="IsAvailable"/> is the host fact the
/// self-update service asks before it offers an in-app download (the real helper only ships
/// in the win-x64 package), and accepted packages are recorded so the apply step can be
/// asserted without spawning a process.
/// </summary>
public sealed class StubWindowsLauncherUpdateApplier(bool isAvailable = true) : IWindowsLauncherUpdateApplier
{
    /// <summary>Whether this stand-in host carries a usable helper.</summary>
    public bool IsAvailable { get; set; } = isAvailable;

    public int StartCount { get; private set; }

    public LauncherSelfUpdatePreparation? LastPreparation { get; private set; }

    public bool TryStartApply(LauncherSelfUpdatePreparation preparation)
    {
        LastPreparation = preparation;
        if (!IsAvailable)
        {
            return false;
        }

        StartCount++;
        return true;
    }

    public void CleanupAbandonedHelperDirectories()
    {
    }
}
