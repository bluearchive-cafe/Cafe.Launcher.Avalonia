using System.Text.Json.Serialization;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.Tests;

public sealed partial class AtomicJsonFileStoreTests
{
    [Fact]
    public async Task WriteAsync_ThenReadAsync_RoundTripsAndLeavesNoTemporaryFiles()
    {
        var directory = TestDirectory.Create();
        var path = Path.Combine(directory, "state.json");
        var value = new TestState("ready", 3);

        try
        {
            await AtomicJsonFileStore.WriteAsync(path, value, TestJsonContext.Default.TestState);

            var actual = await AtomicJsonFileStore.ReadAsync<TestState>(
                path,
                TestJsonContext.Default.TestState);

            Assert.Equal(value, actual);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            directory.Dispose();
        }
    }

    private sealed record TestState(string Status, int Attempt);

    [JsonSerializable(typeof(TestState))]
    private sealed partial class TestJsonContext : JsonSerializerContext;
}
