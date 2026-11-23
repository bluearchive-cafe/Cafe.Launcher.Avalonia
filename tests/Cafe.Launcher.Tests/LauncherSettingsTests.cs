using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Tests;

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
        Assert.NotSame(source.Games, clone.Games);
        foreach (var gameId in source.Games.Keys)
        {
            Assert.NotSame(source.GetGameSettings(gameId), clone.GetGameSettings(gameId));
            Assert.NotSame(source.GetGameSettings(gameId).GameRuntime, clone.GetGameSettings(gameId).GameRuntime);
        }
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
        AssertNamesEverySettableProperty(typeof(GameSettings));
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
    /// 状态同一性对空引用必须回答"不同"：设置页的脏标记先过这一关，比较才能安全继续。
    /// </summary>
    [Fact]
    public void HasSameSettingsState_AgainstNull_IsNeverTheSame()
    {
        Assert.False(new LauncherSettings().HasSameSettingsState(null));
    }

    /// <summary>
    /// 调色板逐元素比较：长度相同、只在同一位置换了颜色也算不同状态。长度不同的分支由
    /// <see cref="EverySettableProperty_DrivesStateIdentityBothWays"/> 的探针值覆盖，
    /// 这一条补的是取值差异——它决定"提取算法换了配色"能不能让保存按钮亮起来。
    /// </summary>
    [Fact]
    public void HasSameSettingsState_WhenPaletteDiffersAtTheSameIndex_IsNotTheSame()
    {
        var left = new LauncherSettings { ThemeColorPalette = ["#FF112233", "#FF445566"] };
        var right = new LauncherSettings { ThemeColorPalette = ["#FF112233", "#FF778899"] };

        Assert.False(left.HasSameSettingsState(right));
        Assert.False(right.HasSameSettingsState(left));
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

    [Fact]
    public void DefaultConstructor_CreatesIndependentSettingsForBothGames()
    {
        var settings = new LauncherSettings();

        Assert.Equal(SettingsFormatVersions.Current, settings.FormatVersion);
        Assert.Equal(LauncherProfiles.BlueArchiveJapan.RuntimeId, settings.LastViewedGameId);
        Assert.Equal(2, settings.Games.Count);
        Assert.Same(StringComparer.Ordinal, settings.Games.Comparer);
        Assert.NotSame(settings.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId),
            settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId));
        Assert.NotSame(settings.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).GameRuntime,
            settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GameRuntime);
        Assert.Equal(PatchUrlGroups.Official, settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).PatchUrlGroup);
    }

    [Fact]
    public void CreateDefaults_WhenCultureIsChinese_UsesCafeSourceForBlueArchiveOnly()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");

            var settings = LauncherSettings.CreateDefaults();

            Assert.Equal(PatchUrlGroups.Cafe, settings.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).PatchUrlGroup);
            Assert.Equal(PatchUrlGroups.Official, settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).PatchUrlGroup);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void CompatibilityAliases_WhenViewingStella_StillReadAndWriteBlueArchive()
    {
        var settings = new LauncherSettings { LastViewedGameId = LauncherProfiles.StellaSoraChina.RuntimeId };
        var originalStella = settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).DeepClone();
        var changes = new List<string?>();
        settings.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        foreach (var name in new[] { nameof(LauncherSettings.GamePath), nameof(LauncherSettings.PatchUrlGroup),
            nameof(LauncherSettings.LaunchCheckMode), nameof(LauncherSettings.GameRuntime),
            nameof(LauncherSettings.AfterLaunchBehavior), nameof(LauncherSettings.ResourcePanelUid),
            nameof(LauncherSettings.ResourcePanelUidSource) })
        {
            var alias = typeof(LauncherSettings).GetProperty(name)!;
            var gameProperty = typeof(GameSettings).GetProperty(name)!;
            var probe = CreateProbeValue(alias.PropertyType, name);
            alias.SetValue(settings, probe);

            Assert.Equal(Describe(probe), Describe(alias.GetValue(settings)));
            Assert.Equal(Describe(probe), Describe(gameProperty.GetValue(settings.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId))));
            Assert.Contains(name, changes);
        }

        Assert.True(originalStella.HasSameSettingsState(settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("BLUE-ARCHIVE-JP")]
    [InlineData("BlueArchive_JP")]
    public void GetGameSettings_WhenIdentityIsUnsupported_ThrowsWithoutChangingEntries(string gameId)
    {
        var settings = new LauncherSettings();
        settings.Games[gameId] = new GameSettings();
        var count = settings.Games.Count;

        Assert.Throws<ArgumentException>(() => settings.GetGameSettings(gameId));
        Assert.Equal(count, settings.Games.Count);
    }

    [Fact]
    public void GetGameSettings_WhenSupportedEntryIsMissing_DoesNotRecreateOrFallback()
    {
        var settings = new LauncherSettings();
        settings.Games.Remove(LauncherProfiles.StellaSoraChina.RuntimeId);

        Assert.Throws<InvalidOperationException>(() => settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId));
        Assert.Single(settings.Games);
    }

    [Fact]
    public void GetGameSettings_WhenSupportedEntryIsNull_RejectsIt()
    {
        var settings = new LauncherSettings();
        settings.Games[LauncherProfiles.StellaSoraChina.RuntimeId] = null!;

        Assert.Throws<InvalidOperationException>(() => settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId));
    }

    [Fact]
    public void DeepClone_WhenGamesAreMissingOrNull_PreservesInvalidShapeForDocumentValidation()
    {
        var missing = new LauncherSettings { Games = new Dictionary<string, GameSettings>(StringComparer.Ordinal) };
        var nullEntry = new LauncherSettings();
        nullEntry.Games[LauncherProfiles.StellaSoraChina.RuntimeId] = null!;
        var nullGames = new LauncherSettings { Games = null! };

        var missingClone = missing.DeepClone();
        var nullEntryClone = nullEntry.DeepClone();
        var nullGamesClone = nullGames.DeepClone();

        Assert.Empty(missingClone.Games);
        Assert.Null(nullEntryClone.Games[LauncherProfiles.StellaSoraChina.RuntimeId]);
        Assert.Null(nullGamesClone.Games);
        Assert.True(missing.HasSameSettingsState(missingClone));
        Assert.True(nullEntry.HasSameSettingsState(nullEntryClone));
        Assert.True(nullGames.HasSameSettingsState(nullGamesClone));
        Assert.Throws<InvalidOperationException>(() => nullGamesClone.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId));
    }

    [Fact]
    public void HasSameSettingsState_WhenGameDictionaryOrderDiffers_StillComparesByKeyAndValue()
    {
        var left = new LauncherSettings();
        var right = left.DeepClone();
        right.Games = right.Games.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        Assert.True(left.HasSameSettingsState(right));
        Assert.True(right.HasSameSettingsState(left));
        right.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GameRuntime.RunnerPath = "different";
        Assert.False(left.HasSameSettingsState(right));
        Assert.False(right.HasSameSettingsState(left));
        Assert.Null(left.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GameRuntime.RunnerPath);
    }

    [Fact]
    public void Games_WhenAssignedCaseInsensitiveDictionary_StoresOrdinalKeys()
    {
        var settings = new LauncherSettings { Games = new Dictionary<string, GameSettings>(StringComparer.OrdinalIgnoreCase)
        {
            [LauncherProfiles.BlueArchiveJapan.RuntimeId] = new(),
            [LauncherProfiles.StellaSoraChina.RuntimeId] = new()
        } };

        Assert.Same(StringComparer.Ordinal, settings.Games.Comparer);
        Assert.False(settings.Games.ContainsKey("BLUE-ARCHIVE-JP"));
    }

    [Fact]
    public void Serialize_WhenAliasesHaveValues_WritesOnlyGameOwnedFieldsInsideGames()
    {
        var settings = new LauncherSettings { GamePath = "BA-path", ResourcePanelUid = "ABCDEFGH" };
        settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GamePath = "Stella-path";
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(settings));
        var root = document.RootElement;

        foreach (var name in new[] { "gamePath", "patchUrlGroup", "launchCheckMode", "gameRuntime",
            "afterLaunchBehavior", "resourcePanelUid", "resourcePanelUidSource" })
        {
            Assert.False(root.TryGetProperty(name, out _));
            Assert.True(root.GetProperty("games").GetProperty(LauncherProfiles.BlueArchiveJapan.RuntimeId).TryGetProperty(name, out _));
        }

        Assert.Equal("BA-path", root.GetProperty("games").GetProperty(LauncherProfiles.BlueArchiveJapan.RuntimeId).GetProperty("gamePath").GetString());
        Assert.Equal("Stella-path", root.GetProperty("games").GetProperty(LauncherProfiles.StellaSoraChina.RuntimeId).GetProperty("gamePath").GetString());
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
        Dictionary<string, GameSettings> games => string.Join(";", games.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={Describe(pair.Value)}")),
        GameSettings game => string.Join(";", SettableProperties(typeof(GameSettings))
            .Select(property => $"{property.Name}={Describe(property.GetValue(game))}")),
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

        if (type == typeof(Dictionary<string, GameSettings>))
        {
            return new Dictionary<string, GameSettings>(StringComparer.Ordinal)
            {
                [LauncherProfiles.BlueArchiveJapan.RuntimeId] = (GameSettings)CreateProbeValue(typeof(GameSettings), propertyName)!,
                [LauncherProfiles.StellaSoraChina.RuntimeId] = new() { GamePath = "stella-probe" }
            };
        }

        if (type == typeof(GameSettings))
        {
            var game = new GameSettings();
            foreach (var property in SettableProperties(typeof(GameSettings)))
            {
                property.SetValue(game, CreateProbeValue(property.PropertyType, property.Name));
            }

            return game;
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
        GameSettings game => game.DeepClone(),
        Dictionary<string, GameSettings> games => games.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone(), StringComparer.Ordinal),
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

        if (type == typeof(Dictionary<string, GameSettings>) || type == typeof(GameSettings))
        {
            return CreateProbeValue(type, propertyName);
        }

        if (type == typeof(GameRuntimeSettings))
        {
            return new GameRuntimeSettings { Runner = $"probe-{propertyName}" };
        }

        return $"probe-{propertyName}";
    }
}
