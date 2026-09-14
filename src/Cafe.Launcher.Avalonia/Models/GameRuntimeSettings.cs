using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Models;

public sealed class GameRuntimeSettings : ObservableObject
{
    private string runner = GameRuntimeRunners.Auto;
    private string? runnerPath;
    private string? prefixPath;
    private string? protonPath;

    [JsonPropertyName("runner")]
    public string Runner { get => runner; set => SetProperty(ref runner, value); }

    [JsonPropertyName("runnerPath")]
    public string? RunnerPath { get => runnerPath; set => SetProperty(ref runnerPath, value); }

    [JsonPropertyName("prefixPath")]
    public string? PrefixPath { get => prefixPath; set => SetProperty(ref prefixPath, value); }

    [JsonPropertyName("protonPath")]
    public string? ProtonPath { get => protonPath; set => SetProperty(ref protonPath, value); }

    public GameRuntimeSettings DeepClone() => new()
    {
        Runner = Runner,
        RunnerPath = RunnerPath,
        PrefixPath = PrefixPath,
        ProtonPath = ProtonPath
    };

    /// <summary>
    /// The properties that participate in <see cref="HasSameSettingsState"/>, as (name, reader)
    /// pairs. Declared here rather than enumerated by reflection so the compared set stays greppable;
    /// LauncherSettingsTests reads the names and drives the readers, so a settable property missing
    /// from this table fails the tests instead of silently dropping out of state identity.
    /// </summary>
    private static readonly (string Name, Func<GameRuntimeSettings, object?> Read)[] ComparedProperties =
    [
        (nameof(Runner), settings => settings.Runner),
        (nameof(RunnerPath), settings => settings.RunnerPath),
        (nameof(PrefixPath), settings => settings.PrefixPath),
        (nameof(ProtonPath), settings => settings.ProtonPath)
    ];

    /// <summary>
    /// Reports whether <paramref name="other"/> holds the same runtime settings state.
    /// <see cref="LauncherSettings.HasSameSettingsState"/> recurses into this instead of comparing
    /// the two objects directly: this type has no value equality and <see cref="DeepClone"/> always
    /// hands out distinct references, so comparing whole objects would always answer "different".
    /// </summary>
    public bool HasSameSettingsState(GameRuntimeSettings? other)
    {
        if (other is null)
        {
            return false;
        }

        foreach (var (_, read) in ComparedProperties)
        {
            if (!LauncherSettings.ValuesEqual(read(this), read(other)))
            {
                return false;
            }
        }

        return true;
    }
}
