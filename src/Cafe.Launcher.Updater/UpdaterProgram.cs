using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Updater;

/// <summary>Entry point logic for the detached update helper.</summary>
internal static class UpdaterProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!UpdaterArguments.TryParse(args, out var arguments, out var error))
        {
            await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
            return 64;
        }

        try
        {
            return await UpdateApplier
                .ApplyAsync(arguments!, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            UpdateLog.Write(arguments!, $"Fatal: {exception}");
            return 1;
        }
    }
}
