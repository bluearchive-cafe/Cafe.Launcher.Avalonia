using System.Text.Json;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.Tests;

public sealed class AtomicJsonFileStoreTests
{
    [Fact]
    public async Task WriteAsync_ThenReadAsync_RoundTripsAndLeavesNoTemporaryFiles()
    {
        var directory = TestDirectory.Create();
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
            directory.Dispose();
        }
    }

    private sealed record TestState(string Status, int Attempt);
}
