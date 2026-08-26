using System.Text.Json;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class AtomicJsonFileStoreTests
{
    [Fact]
    public async Task WriteAsync_ThenReadAsync_RoundTripsAndLeavesNoTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "state.json");
        var value = new TestState("ready", 3);

        try
        {
            await AtomicJsonFileStore.WriteAsync(path, value, JsonDefaults.Strict);

            var actual = await AtomicJsonFileStore.ReadAsync<TestState>(
                path,
                JsonDefaults.Strict);

            Assert.Equal(value, actual);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed record TestState(string Status, int Attempt);
}
