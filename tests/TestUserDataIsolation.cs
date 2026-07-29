using System.Runtime.CompilerServices;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Testing;

internal static class TestUserDataIsolation
{
    private static readonly string userDataDirectory = Path.Combine(
        Path.GetTempPath(),
        "Cafe.Launcher.Avalonia.Tests",
        "UserData",
        typeof(TestUserDataIsolation).Assembly.GetName().Name ?? "UnknownAssembly",
        $"{Environment.ProcessId}-{Guid.NewGuid():N}");

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(userDataDirectory);
        Environment.SetEnvironmentVariable(
            LauncherUserDataDirectory.TestOverrideEnvironmentVariable,
            userDataDirectory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDeleteUserDataDirectory();
    }

    private static void TryDeleteUserDataDirectory()
    {
        try
        {
            if (Directory.Exists(userDataDirectory))
            {
                Directory.Delete(userDataDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
