namespace Cafe.Launcher.Avalonia.Models;

public sealed class PatchUrlGroupDefinition
{
    public string Code { get; set; } = PatchUrlGroups.Official;

    public string PackageHostFrom { get; set; } = "";

    public string PackageHostTo { get; set; } = "";
}
