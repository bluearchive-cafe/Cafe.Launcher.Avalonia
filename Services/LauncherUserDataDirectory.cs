using System;
using System.IO;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Resolves the root directory for launcher-owned per-user data.
/// </summary>
internal static class LauncherUserDataDirectory
{
    internal const string TestOverrideEnvironmentVariable =
        "CAFE_LAUNCHER_TEST_USER_DATA_DIRECTORY";

    public static string Path
    {
        get => Resolve(
            Environment.GetEnvironmentVariable(TestOverrideEnvironmentVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    internal static string Resolve(string? testOverride, string localApplicationData)
    {
        if (!string.IsNullOrWhiteSpace(testOverride))
        {
            return System.IO.Path.GetFullPath(testOverride);
        }

        return System.IO.Path.Combine(
            localApplicationData,
            LauncherConstants.ProductName);
    }
}
