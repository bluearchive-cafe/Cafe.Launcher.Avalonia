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
}
