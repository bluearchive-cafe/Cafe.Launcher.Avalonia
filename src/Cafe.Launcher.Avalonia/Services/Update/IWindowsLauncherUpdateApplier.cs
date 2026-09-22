namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// Hands a verified, staged package to the detached helper. The caller shuts the
/// launcher down once this returns true; a false result means the update could not
/// be started and the current version stays in place.
/// </summary>
public interface IWindowsLauncherUpdateApplier
{
    bool TryStartApply(LauncherSelfUpdatePreparation preparation);
}
