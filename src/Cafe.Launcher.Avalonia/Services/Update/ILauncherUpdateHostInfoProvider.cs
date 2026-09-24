namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>Supplies the current host facts the package selector answers from.</summary>
public interface ILauncherUpdateHostInfoProvider
{
    LauncherUpdateHostInfo GetHostInfo();
}
