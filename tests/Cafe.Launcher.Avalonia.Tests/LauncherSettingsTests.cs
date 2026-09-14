using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Guards the two per-property lists documented in Models/LauncherSettings.cs. The copy
/// constructor: a new setting must be added there, otherwise <see cref="LauncherSettings.DeepClone"/>
/// silently drops it and the value resets whenever settings are normalized, edited, or saved.
/// <c>ComparedProperties</c>: a new setting must be added there as well, otherwise state identity
/// ignores it and the settings page's save button stops tracking that field.
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
            var expected = Describe(property.GetValue(source));
            var actual = Describe(property.GetValue(clone));

            Assert.True(
                expected == actual,
                $"DeepClone did not copy '{property.Name}' (source '{expected}', clone '{actual}'). "
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

    /// <summary>
    /// The declaration tables must name every settable property exactly once. The behavioural guard
    /// below also catches a missing entry, but it reports "the editor stayed clean" — a bug in the
    /// editor — while this one names the table entry that has to be added, which is the contract the
    /// tables' own XML docs state. Reading the private tables is deliberate: the names are declared
    /// for readers, and nothing else would ever consume them.
    /// </summary>
    [Fact]
    public void ComparedProperties_NamesEverySettablePropertyExactlyOnce()
    {
        AssertNamesEverySettableProperty(typeof(LauncherSettings));
        AssertNamesEverySettableProperty(typeof(GameRuntimeSettings));
    }

    private static void AssertNamesEverySettableProperty(Type type)
    {
        var declared = DeclaredComparedPropertyNames(type);

        foreach (var expected in SettableProperties(type).Select(property => property.Name))
        {
            Assert.True(
                declared.Contains(expected),
                $"'{type.Name}.ComparedProperties' is missing '{expected}'. Add it, otherwise state "
                + "identity ignores that field and the settings page's save button stops tracking it.");
        }

        Assert.Equal(declared.Length, declared.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(SettableProperties(type).Length, declared.Length);
    }

    /// <summary>
    /// Reads a private declaration table. Entries are <c>(Name, Read)</c> tuples: this guard reads
    /// the name half, the behavioural guard below exercises the reader half.
    /// </summary>
    private static string[] DeclaredComparedPropertyNames(Type type)
    {
        var field = type.GetField("ComparedProperties", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"'{type.Name}' no longer declares ComparedProperties, which is the table this guard reads.");

        var entries = (Array)field.GetValue(null)!;
        return
        [
            .. entries.Cast<ITuple>().Select(entry => entry[0] as string
                ?? throw new InvalidOperationException(
                    $"'{type.Name}.ComparedProperties' entries are no longer (Name, Read) tuples."))
        ];
    }

    /// <summary>
    /// Every settable property must drive state identity in both directions: changing it has to mark
    /// the editor dirty, and putting an equal-but-detached value back has to clear the flag again.
    /// The change half catches a property missing from <c>ComparedProperties</c>; the restore half
    /// catches a property compared by reference, which would leave the editor permanently dirty and
    /// the save button permanently enabled.
    /// </summary>
    [Fact]
    public void EverySettableProperty_DrivesStateIdentityBothWays()
    {
        // The positive phase, and the only assertion that proves identity can answer "same" at all.
        // It has to compare two settings objects directly: SettingsEditor.IsDirty starts false and
        // ApplySnapshot clears it by assignment, so neither exercises the comparison — a value
        // compared by reference would slip past both and leave every save button permanently enabled.
        Assert.True(
            new LauncherSettings().HasSameSettingsState(new LauncherSettings()),
            "Two default settings do not compare identical: state identity compares a value by reference.");

        foreach (var property in SettableProperties(typeof(LauncherSettings)))
        {
            var editor = new SettingsEditor();
            var original = DetachedCopy(property.GetValue(editor.Current));

            property.SetValue(editor.Current, DifferingProbeValue(property.PropertyType, original, property.Name));
            Assert.True(
                editor.IsDirty,
                $"Changing '{property.Name}' left the editor clean. "
                + "Add it to LauncherSettings.ComparedProperties.");

            property.SetValue(editor.Current, DetachedCopy(original));
            Assert.False(
                editor.IsDirty,
                $"Restoring '{property.Name}' left the editor dirty: it is compared by reference, "
                + "not by value.");
        }

        foreach (var property in SettableProperties(typeof(GameRuntimeSettings)))
        {
            var editor = new SettingsEditor();
            var runtime = editor.Current.GameRuntime;
            var original = property.GetValue(runtime);

            property.SetValue(runtime, DifferingProbeValue(property.PropertyType, original, property.Name));
            Assert.True(
                editor.IsDirty,
                $"Changing 'GameRuntime.{property.Name}' left the editor clean. "
                + "Add it to GameRuntimeSettings.ComparedProperties.");

            property.SetValue(runtime, original);
            Assert.False(
                editor.IsDirty,
                $"Restoring 'GameRuntime.{property.Name}' left the editor dirty.");
        }
    }

    private static PropertyInfo[] SettableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.GetIndexParameters().Length == 0)
            .ToArray();

    /// <summary>
    /// Renders a settings value as text by reading its own members. Deliberately independent of
    /// <see cref="LauncherSettings.ValuesEqual"/>: a guard that reused the production comparator
    /// would report a comparator defect as a copy defect, and would silently stop guarding if that
    /// comparator ever grew too lenient. Nested runtime settings expand leaf by leaf, so a dropped
    /// leaf is caught here even while both declaration tables are missing it.
    /// </summary>
    private static string Describe(object? value) => value switch
    {
        null => "<null>",
        string text => text,
        GameRuntimeSettings runtime => string.Join(
            "; ",
            SettableProperties(typeof(GameRuntimeSettings))
                .Select(property => $"{property.Name}={Describe(property.GetValue(runtime))}")),
        IEnumerable items => string.Join(",", items.Cast<object?>().Select(Describe)),
        _ => value.ToString() ?? "<null>"
    };

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

    /// <summary>
    /// A value equal to <paramref name="value"/> but a separate instance for mutable types, so the
    /// restore half of the state-identity guard proves the comparison is by value, not by reference.
    /// </summary>
    private static object? DetachedCopy(object? value) => value switch
    {
        GameRuntimeSettings runtime => runtime.DeepClone(),
        List<string> palette => new List<string>(palette),
        _ => value
    };

    /// <summary>
    /// A value state identity must report as different from <paramref name="current"/>.
    /// </summary>
    private static object? DifferingProbeValue(Type type, object? current, string propertyName)
    {
        var probe = ProbeValue(type, current, propertyName);

        if (!LauncherSettings.ValuesEqual(current, probe))
        {
            return probe;
        }

        throw new InvalidOperationException(
            $"Probe for '{propertyName}' ({type}) equals its current value: extend this guard.");
    }

    private static object? ProbeValue(Type type, object? current, string propertyName)
    {
        if (type == typeof(bool))
        {
            // Negates rather than reusing CreateProbeValue's fixed `true`: this model's bool settings
            // default to true, so a fixed probe would leave the property unchanged.
            return current is not true;
        }

        if (type == typeof(int) || type == typeof(int?))
        {
            return current is 7 ? 8 : 7;
        }

        if (type == typeof(double?))
        {
            return current is 1.5 ? 2.5 : 1.5;
        }

        if (type == typeof(List<string>))
        {
            return new List<string> { $"probe-{propertyName}" };
        }

        if (type == typeof(GameRuntimeSettings))
        {
            return new GameRuntimeSettings { Runner = $"probe-{propertyName}" };
        }

        return $"probe-{propertyName}";
    }
}
