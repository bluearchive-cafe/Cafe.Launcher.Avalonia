using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cafe.Launcher.Updater;

/// <summary>
/// The private command line shared by the launcher and its update helper: everything
/// needed to replace the launcher on disk after its process has exited. Parsing is
/// strict so a malformed or incompatible invocation never degrades into applying the
/// wrong package to the wrong directory.
/// </summary>
/// <remarks>
/// The main application constructs this record and calls <see cref="ToArgumentArray"/>,
/// so option names, mode values, formatting, and parsing have one owner.
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
    public const string ApplyCommand = "apply";
    public const string ProtocolVersionOption = "--protocol-version";
    public const int CurrentProtocolVersion = 1;
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
        ProtocolVersionOption,
        ModeOption,
        PackageOption,
        InstallDirOption,
        ExeOption,
        ParentPidOption,
        Sha256Option,
        LogOption
    };

    /// <summary>Returns the command-line value used for an apply mode.</summary>
    public static string ModeName(UpdateApplyMode mode) => mode switch
    {
        UpdateApplyMode.Installer => InstallerMode,
        UpdateApplyMode.Portable => PortableMode,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown update apply mode.")
    };

    /// <summary>Serializes this command using the current private protocol version.</summary>
    public string[] ToArgumentArray() =>
    [
        ApplyCommand,
        ProtocolVersionOption, CurrentProtocolVersion.ToString(CultureInfo.InvariantCulture),
        ModeOption, ModeName(Mode),
        PackageOption, PackagePath,
        InstallDirOption, InstallDirectory,
        ExeOption, ExecutableName,
        ParentPidOption, ParentProcessId.ToString(CultureInfo.InvariantCulture),
        Sha256Option, ExpectedSha256,
        LogOption, LogPath
    ];

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

        if (!string.Equals(args[0], ApplyCommand, StringComparison.Ordinal))
        {
            error = $"Unknown command '{args[0]}'.";
            return false;
        }

        if ((args.Length - 1) % 2 != 0)
        {
            error = $"Option '{args[^1]}' is missing a value.";
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
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

        if (!TryGetRequired(values, ProtocolVersionOption, out var protocolVersionText, out error))
        {
            return false;
        }

        if (!int.TryParse(
                protocolVersionText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var protocolVersion)
            || protocolVersion != CurrentProtocolVersion)
        {
            error = $"Unsupported updater protocol version '{protocolVersionText}'; expected '{CurrentProtocolVersion}'.";
            return false;
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
