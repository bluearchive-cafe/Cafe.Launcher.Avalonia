using System;
using System.Runtime.InteropServices;

namespace Cafe.Launcher.Avalonia.Services;

public sealed partial class WindowsAnimationSettingsProvider
{
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private readonly Func<(bool Success, bool Enabled)> readAnimationsEnabled;

    public WindowsAnimationSettingsProvider()
        : this(ReadAnimationsEnabled)
    {
    }

    internal WindowsAnimationSettingsProvider(Func<(bool Success, bool Enabled)> readAnimationsEnabled)
    {
        this.readAnimationsEnabled = readAnimationsEnabled;
    }

    public bool? GetWindowsAnimationsEnabled()
    {
        var result = readAnimationsEnabled();
        return result.Success ? result.Enabled : null;
    }

    private static (bool Success, bool Enabled) ReadAnimationsEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (false, false);
        }

        var success = SystemParametersInfoW(SpiGetClientAreaAnimation, 0, out var enabled, 0);
        return (success, enabled);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(
        uint uiAction,
        uint uiParam,
        [MarshalAs(UnmanagedType.Bool)] out bool pvParam,
        uint fWinIni);
}
