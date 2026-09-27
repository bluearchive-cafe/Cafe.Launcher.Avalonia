using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Updater;

/// <summary>Entry point logic for the detached update helper.</summary>
public static class UpdaterProgram
{
    public const string HelpText =
        "Cafe Launcher updater (internal protocol; not intended for manual use).\n"
        + "Usage: Cafe.Launcher.Updater apply --protocol-version 1 --mode <installer|portable> "
        + "--package <path> --install-dir <path> --exe <name> --parent-pid <pid> "
        + "--sha256 <hex> --log <path>";

    public static async Task<int> RunAsync(string[] args)
    {
        if (IsHelpRequest(args))
        {
            await Console.Out.WriteLineAsync(HelpText).ConfigureAwait(false);
            return (int)UpdaterExitCode.Success;
        }

        if (!UpdaterArguments.TryParse(args, out var arguments, out var error))
        {
            await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
            return (int)UpdaterExitCode.Usage;
        }

        try
        {
            var exitCode = await UpdateApplier
                .ApplyAsync(arguments!, CancellationToken.None)
                .ConfigureAwait(false);
            return (int)exitCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            UpdateLog.Write(arguments!, $"Fatal: {exception}");
            return (int)UpdaterExitCode.UnexpectedFailure;
        }
    }

    private static bool IsHelpRequest(string[] args) =>
        args.Length == 1 && (args[0] == "--help" || args[0] == "-h")
        || args.Length == 2 && args[0] == UpdaterArguments.ApplyCommand && args[1] == "--help";
}
