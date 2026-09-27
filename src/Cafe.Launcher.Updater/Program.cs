using System.Threading.Tasks;
using Cafe.Launcher.Updater.Core;

namespace Cafe.Launcher.Updater;

internal static class Program
{
    private static Task<int> Main(string[] args) => UpdaterProgram.RunAsync(args);
}
