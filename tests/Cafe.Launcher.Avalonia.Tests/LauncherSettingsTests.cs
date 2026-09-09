using System.Collections;
using System.Reflection;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Guards the settings copy-constructor contract documented in Models/LauncherSettings.cs:
/// a new setting must be added to the copy constructor, otherwise <see cref="LauncherSettings.DeepClone"/>
/// silently drops it and the value resets whenever settings are normalized, edited, or saved.
/// </summary>
public sealed class LauncherSettingsTests
{
    [Fact]
    public void DeepClone_CopiesEveryPubliclySettableProperty()
    {
        var source = new LauncherSettings();
        var properties = SettableProperties(typeof(LauncherSettings));

        foreach (var property in properties)
        {
            property.SetValue(source, CreateProbeValue(property.PropertyType, property.Name));
        }

        var clone = source.DeepClone();

        foreach (var property in properties)
        {
            Assert.True(
                ValuesMatch(property.GetValue(source), property.GetValue(clone)),
                $"DeepClone did not copy '{property.Name}'. "
                + "Add it to the LauncherSettings copy constructor.");
        }
    }

    [Fact]
    public void DeepClone_CreatesIndependentInstancesForMutableProperties()
    {
        var source = new LauncherSettings
        {
            ThemeColorPalette = ["#FF2E7DF6"],
            GameRuntime = new GameRuntimeSettings { Runner = "umu" }
        };

        var clone = source.DeepClone();

        Assert.NotSame(source.ThemeColorPalette, clone.ThemeColorPalette);
        Assert.NotSame(source.GameRuntime, clone.GameRuntime);
    }

    private static PropertyInfo[] SettableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.GetIndexParameters().Length == 0)
            .ToArray();

    private static object? CreateProbeValue(Type type, string propertyName)
    {
        if (type == typeof(string))
        {
            return $"probe-{propertyName}";
        }

        if (type == typeof(bool))
        {
            return true;
        }

        if (type == typeof(int) || type == typeof(int?))
        {
            return 7;
        }

        if (type == typeof(double?))
        {
            return 1.5;
        }

        if (type == typeof(List<string>))
        {
            return new List<string> { $"probe-{propertyName}" };
        }

        if (type == typeof(GameRuntimeSettings))
        {
            var runtime = new GameRuntimeSettings();
            foreach (var property in SettableProperties(typeof(GameRuntimeSettings)))
            {
                property.SetValue(runtime, CreateProbeValue(property.PropertyType, property.Name));
            }

            return runtime;
        }

        throw new InvalidOperationException(
            $"Unhandled property type '{type}' for '{propertyName}': extend this guard.");
    }

    private static bool ValuesMatch(object? expected, object? actual)
    {
        if (expected is null || actual is null)
        {
            return expected is null && actual is null;
        }

        // Check scalar types before IEnumerable: string is IEnumerable<char>.
        if (expected is string or bool or int or double)
        {
            return expected.Equals(actual);
        }

        if (expected is IEnumerable expectedItems && actual is IEnumerable actualItems)
        {
            return expectedItems.Cast<object>().SequenceEqual(actualItems.Cast<object>());
        }

        if (expected is GameRuntimeSettings expectedRuntime && actual is GameRuntimeSettings actualRuntime)
        {
            return SettableProperties(typeof(GameRuntimeSettings)).All(property => ValuesMatch(
                property.GetValue(expectedRuntime),
                property.GetValue(actualRuntime)));
        }

        throw new InvalidOperationException(
            $"Unhandled value type '{expected.GetType()}': extend this guard.");
    }
}
