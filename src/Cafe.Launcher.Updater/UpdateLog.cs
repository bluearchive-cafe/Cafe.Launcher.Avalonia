using System;
using System.IO;

namespace Cafe.Launcher.Updater;

/// <summary>Best-effort helper log: console stderr plus the app-data file the launcher names.</summary>
internal static class UpdateLog
{
    public static void Write(UpdaterArguments arguments, string message)
    {
        var line = $"{DateTimeOffset.Now:O} {message}";
        Console.Error.WriteLine(line);
        try
        {
            var directory = Path.GetDirectoryName(arguments.LogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(arguments.LogPath, line + Environment.NewLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Logging is best-effort; stderr above is the fallback channel.
        }
    }
}
