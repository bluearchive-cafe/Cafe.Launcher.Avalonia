using System;
using System.Linq;

namespace Cafe.Launcher.Avalonia.Models;

/// <summary>
/// Defines the explicit persisted-settings field contract shared by deep cloning and dirty-state comparison.
/// This intentionally avoids reflection so settings compatibility stays reviewable when fields are added.
/// </summary>
internal static class LauncherSettingsContract
{
    private static readonly Field[] Fields =
    [
        new((target, source) => target.GamePath = source.GamePath,
            (left, right) => string.Equals(left.GamePath, right.GamePath, StringComparison.Ordinal)),
        new((target, source) => target.LaunchCheckMode = source.LaunchCheckMode,
            (left, right) => string.Equals(left.LaunchCheckMode, right.LaunchCheckMode, StringComparison.Ordinal)),
        new((target, source) => target.ProxyMode = source.ProxyMode,
            (left, right) => string.Equals(left.ProxyMode, right.ProxyMode, StringComparison.Ordinal)),
        new((target, source) => target.CloseBehavior = source.CloseBehavior,
            (left, right) => string.Equals(left.CloseBehavior, right.CloseBehavior, StringComparison.Ordinal)),
        new((target, source) => target.Language = source.Language,
            (left, right) => string.Equals(left.Language, right.Language, StringComparison.Ordinal)),
        new((target, source) => target.ThemeMode = source.ThemeMode,
            (left, right) => string.Equals(left.ThemeMode, right.ThemeMode, StringComparison.Ordinal)),
        new((target, source) => target.MotionMode = source.MotionMode,
            (left, right) => string.Equals(left.MotionMode, right.MotionMode, StringComparison.Ordinal)),
        new((target, source) => target.ThemeColorMode = source.ThemeColorMode,
            (left, right) => string.Equals(left.ThemeColorMode, right.ThemeColorMode, StringComparison.Ordinal)),
        new((target, source) => target.CustomThemeColor = source.CustomThemeColor,
            (left, right) => string.Equals(left.CustomThemeColor, right.CustomThemeColor, StringComparison.Ordinal)),
        new((target, source) => target.ThemeColorPalette = [.. source.ThemeColorPalette],
            (left, right) => left.ThemeColorPalette.SequenceEqual(right.ThemeColorPalette, StringComparer.Ordinal)),
        new((target, source) => target.SelectedThemeColorPaletteIndex = source.SelectedThemeColorPaletteIndex,
            (left, right) => left.SelectedThemeColorPaletteIndex == right.SelectedThemeColorPaletteIndex),
        new((target, source) => target.DownloadSpeedLimit = source.DownloadSpeedLimit,
            (left, right) => string.Equals(left.DownloadSpeedLimit, right.DownloadSpeedLimit, StringComparison.Ordinal)),
        new((target, source) => target.EnableStartupUpdateCheck = source.EnableStartupUpdateCheck,
            (left, right) => left.EnableStartupUpdateCheck == right.EnableStartupUpdateCheck),
        new((target, source) => target.ShowRemoteContentCard = source.ShowRemoteContentCard,
            (left, right) => left.ShowRemoteContentCard == right.ShowRemoteContentCard),
        new((target, source) => target.PatchUrlGroup = source.PatchUrlGroup,
            (left, right) => string.Equals(left.PatchUrlGroup, right.PatchUrlGroup, StringComparison.Ordinal)),
        new((target, source) => target.CustomBackgroundPath = source.CustomBackgroundPath,
            (left, right) => string.Equals(left.CustomBackgroundPath, right.CustomBackgroundPath, StringComparison.Ordinal)),
        new((target, source) => target.BackgroundSource = source.BackgroundSource,
            (left, right) => string.Equals(left.BackgroundSource, right.BackgroundSource, StringComparison.Ordinal)),
        new((target, source) => target.BackgroundFit = source.BackgroundFit,
            (left, right) => string.Equals(left.BackgroundFit, right.BackgroundFit, StringComparison.Ordinal)),
        new((target, source) => target.BackgroundFillColor = source.BackgroundFillColor,
            (left, right) => string.Equals(left.BackgroundFillColor, right.BackgroundFillColor, StringComparison.Ordinal)),
        new((target, source) => target.ResourcePanelUid = source.ResourcePanelUid,
            (left, right) => string.Equals(left.ResourcePanelUid, right.ResourcePanelUid, StringComparison.Ordinal)),
        new((target, source) => target.ResourcePanelUidSource = source.ResourcePanelUidSource,
            (left, right) => string.Equals(left.ResourcePanelUidSource, right.ResourcePanelUidSource, StringComparison.Ordinal)),
        new((target, source) => target.StatusDetailMode = source.StatusDetailMode,
            (left, right) => string.Equals(left.StatusDetailMode, right.StatusDetailMode, StringComparison.Ordinal)),
        new((target, source) => target.UpdateChannel = source.UpdateChannel,
            (left, right) => string.Equals(left.UpdateChannel, right.UpdateChannel, StringComparison.Ordinal)),
        new((target, source) => target.LogLevel = source.LogLevel,
            (left, right) => string.Equals(left.LogLevel, right.LogLevel, StringComparison.Ordinal))
    ];

    /// <summary>
    /// Copies every persisted settings field from <paramref name="source"/> into <paramref name="target"/>.
    /// </summary>
    public static void CopyAll(LauncherSettings target, LauncherSettings source)
    {
        foreach (var field in Fields)
        {
            field.Copy(target, source);
        }
    }

    /// <summary>
    /// Returns whether all persisted settings fields are equivalent.
    /// </summary>
    public static bool Matches(LauncherSettings left, LauncherSettings right)
    {
        foreach (var field in Fields)
        {
            if (!field.Matches(left, right))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record Field(
        Action<LauncherSettings, LauncherSettings> Copy,
        Func<LauncherSettings, LauncherSettings, bool> Matches);
}
