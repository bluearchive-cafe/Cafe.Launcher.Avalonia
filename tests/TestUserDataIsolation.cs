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
        Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(userDataDirectory);
        Environment.SetEnvironmentVariable(
            LauncherUserDataDirectory.TestOverrideEnvironmentVariable,
            userDataDirectory);
    }
}
