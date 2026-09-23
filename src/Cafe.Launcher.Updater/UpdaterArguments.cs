using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cafe.Launcher.Updater;

/// <summary>
/// The helper's command line: everything needed to replace the launcher on disk after
/// its process has exited. Parsing is strict so a malformed argument never degrades
/// into applying the wrong package to the wrong directory.
/// </summary>
/// <remarks>
/// The option names are mirrored by the production argument builder in the main
/// application (<c>Services/Update/UpdateHelperCommand.cs</c>); a round-trip test keeps
/// the two halves in sync.
/// </remarks>
public sealed record UpdaterArguments(
    UpdateApplyMode Mode,
    string PackagePath,
    string InstallDirectory,
    string ExecutableName,
    int ParentProcessId,
    string ExpectedSha256,
    string LogPath)
{
    public const string ModeOption = "--mode";
    public const string PackageOption = "--package";
    public const string InstallDirOption = "--install-dir";
    public const string ExeOption = "--exe";
    public const string ParentPidOption = "--parent-pid";
    public const string Sha256Option = "--sha256";
    public const string LogOption = "--log";

    private const string InstallerMode = "installer";
    private const string PortableMode = "portable";

    private static readonly HashSet<string> KnownOptions = new(StringComparer.Ordinal)
    {
        ModeOption,
        PackageOption,
        InstallDirOption,
        ExeOption,
        ParentPidOption,
        Sha256Option,
        LogOption
    };

    /// <summary>Parses the helper command line, reporting the first problem it finds.</summary>
    public static bool TryParse(string[] args, out UpdaterArguments? parsed, out string error)
    {
        parsed = null;
        error = "";
        if (args is null || args.Length == 0)
        {
            error = "No arguments were supplied.";
            return false;
        }

        if (args.Length % 2 != 0)
        {
            error = $"Option '{args[^1]}' is missing a value.";
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            var option = args[index];
            if (!KnownOptions.Contains(option))
            {
                error = $"Unknown option '{option}'.";
                return false;
            }

            if (!values.TryAdd(option, args[index + 1]))
            {
                error = $"Option '{option}' is repeated.";
                return false;
            }
        }

        if (!TryGetRequired(values, ModeOption, out var modeText, out error)
            || !TryGetRequired(values, PackageOption, out var packagePath, out error)
            || !TryGetRequired(values, InstallDirOption, out var installDirectory, out error)
            || !TryGetRequired(values, ExeOption, out var executableName, out error)
            || !TryGetRequired(values, ParentPidOption, out var parentPidText, out error)
            || !TryGetRequired(values, Sha256Option, out var expectedSha256, out error)
            || !TryGetRequired(values, LogOption, out var logPath, out error))
        {
            return false;
        }

        UpdateApplyMode mode;
        if (string.Equals(modeText, InstallerMode, StringComparison.Ordinal))
        {
            mode = UpdateApplyMode.Installer;
        }
        else if (string.Equals(modeText, PortableMode, StringComparison.Ordinal))
        {
            mode = UpdateApplyMode.Portable;
        }
        else
        {
            error = $"Unknown mode '{modeText}'.";
            return false;
        }

        if (!int.TryParse(parentPidText, NumberStyles.None, CultureInfo.InvariantCulture, out var parentProcessId)
            || parentProcessId <= 0)
        {
            error = "The parent process id is not a positive integer.";
            return false;
        }

        if (!PackageIntegrity.IsHexSha256(expectedSha256))
        {
            error = "The expected SHA-256 is not 64 hexadecimal characters.";
            return false;
        }

        parsed = new UpdaterArguments(
            mode,
            packagePath,
            installDirectory,
            executableName,
            parentProcessId,
            expectedSha256,
            logPath);
        return true;
    }

    private static bool TryGetRequired(
        IReadOnlyDictionary<string, string> values,
        string option,
        out string value,
        out string error)
    {
        if (values.TryGetValue(option, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            error = "";
            return true;
        }

        value = "";
        error = $"Option '{option}' is required.";
        return false;
    }
}
