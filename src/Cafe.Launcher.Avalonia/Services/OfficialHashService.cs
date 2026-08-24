using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Computes integrity hashes matching the official launcher's wire protocol.
/// Uses MD5 for local tamper-evidence checks (manifest/config Vc fields), not
/// for cryptographic authentication. An attacker who can modify the manifest
/// can also recompute its Vc, so these hashes guard only against accidental
/// corruption. This matches the original launcher's check-summing scheme.
/// </summary>
public static class OfficialHashService
{
    public static string GetManifestInfoHash(string name, string version, string basis)
    {
        return GetObjectHash([name, version, basis]);
    }

    public static string GetManifestFileHash(ManifestFile file)
    {
        // Field order MUST match the official manifest's JSON key order (path, hash, size).
        // The official launcher computes vc = MD5(Object.values(file).join(";")) over the
        // remote file object, whose keys are emitted in path/hash/size order. The Vc field
        // order in the serialized ManifestFile (see Models/LocalGameContracts.cs) must stay
        // in lockstep so both launchers read each other's manifest.json without flagging it
        // corrupted.
        return GetObjectHash([file.Path, file.Hash, file.Size]);
    }

    public static string GetGameConfigHash(GameLauncherConfig config)
    {
        return GetObjectHash([
            config.Tag ?? "",
            config.Name ?? "",
            string.Join(",", config.Params),
            config.Version ?? ""
        ]);
    }

    public static bool IsManifestInfoHashValid(LocalManifest manifest)
    {
        return string.Equals(
            manifest.Vc,
            GetManifestInfoHash(manifest.Name ?? "", manifest.Version ?? "", manifest.Basis ?? ""),
            StringComparison.Ordinal);
    }

    public static bool IsManifestFileHashValid(ManifestFile file)
    {
        return string.Equals(file.Vc, GetManifestFileHash(file), StringComparison.Ordinal);
    }

    public static bool IsGameConfigHashValid(GameLauncherConfig config)
    {
        return string.Equals(config.Vc, GetGameConfigHash(config), StringComparison.Ordinal);
    }

    private static string GetObjectHash(IReadOnlyList<string> values)
    {
        var text = string.Join(";", values);
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(hash);
    }
}
