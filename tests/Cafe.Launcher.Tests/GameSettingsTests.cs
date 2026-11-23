using System.Reflection;
using System.Text.Json;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Tests;

public sealed class GameSettingsTests
{
    [Fact]
    public void DeepClone_CopiesEverySettablePropertyAndDetachesRuntime()
    {
        var source = new GameSettings();
        foreach (var property in SettableProperties())
        {
            property.SetValue(source, Probe(property));
        }

        var clone = source.DeepClone();

        Assert.Equal(JsonSerializer.Serialize(source), JsonSerializer.Serialize(clone));
        Assert.NotSame(source, clone);
        Assert.NotSame(source.GameRuntime, clone.GameRuntime);
        clone.GameRuntime.RunnerPath = "changed";
        Assert.Equal("probe-path", source.GameRuntime.RunnerPath);
    }

    [Fact]
    public void HasSameSettingsState_EverySettablePropertyParticipatesByValue()
    {
        Assert.True(new GameSettings().HasSameSettingsState(new GameSettings()));
        Assert.False(new GameSettings().HasSameSettingsState(null));

        foreach (var property in SettableProperties())
        {
            var source = new GameSettings();
            var changed = source.DeepClone();
            property.SetValue(changed, Probe(property));

            Assert.False(source.HasSameSettingsState(changed), $"{property.Name} did not affect settings identity.");
            Assert.False(changed.HasSameSettingsState(source));
            Assert.True(changed.HasSameSettingsState(changed.DeepClone()));
        }
    }

    [Fact]
    public void DeepClone_WhenRuntimeIsExplicitlyNull_PreservesItForDocumentValidation()
    {
        var source = new GameSettings { GameRuntime = null! };

        var clone = source.DeepClone();

        Assert.Null(clone.GameRuntime);
        Assert.True(source.HasSameSettingsState(clone));
    }

    [Fact]
    public void Setters_WhenValuesChange_NotifyEachGameProperty()
    {
        var settings = new GameSettings();
        var changes = new List<string?>();
        settings.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        foreach (var property in SettableProperties())
        {
            property.SetValue(settings, Probe(property));
            Assert.Contains(property.Name, changes);
        }
    }

    private static PropertyInfo[] SettableProperties() => typeof(GameSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.CanWrite && property.GetIndexParameters().Length == 0)
        .ToArray();

    private static object Probe(PropertyInfo property) => property.PropertyType == typeof(GameRuntimeSettings)
        ? new GameRuntimeSettings
        {
            Runner = GameRuntimeRunners.Wine,
            RunnerPath = "probe-path",
            PrefixPath = "probe-prefix",
            ProtonPath = "probe-proton",
            EnableMangoHud = true
        }
        : property.PropertyType == typeof(bool) ? true
        : property.PropertyType == typeof(int) ? 37
        : $"probe-{property.Name}";
}
