using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// Computes integrity hashes matching the official launcher's wire protocol.
/// Uses MD5 for local tamper-evidence checks (manifest/config Vc fields), not
/// for cryptographic authentication. An attacker who can modify the manifest
/// can also recompute its Vc, so these hashes guard only against accidental
/// corruption. This matches the original launcher's check-summing scheme.
/// </summary>
internal static class OfficialHashService
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

    /// <summary>按模型的官方写出顺序计算校验值；缺失与空 <c>params</c> 不互相归一化。</summary>
    public static string GetGameConfigHash(GameLauncherConfig config)
    {
        if (config.Params is null)
        {
            return GetObjectHash([config.Tag ?? "", config.Name ?? "", config.Version ?? ""]);
        }

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

    /// <summary>映射模型前按原始 JSON 字段序验证校验值，并拒绝重复、未知及类型错误的字段。</summary>
    internal static bool IsGameConfigHashValid(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? expected = null;
        foreach (var property in config.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                return false;
            }

            if (property.Name == "vc")
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                expected = property.Value.GetString();
            }
            else if (property.Name == "params")
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                var parameters = new List<string>();
                foreach (var parameter in property.Value.EnumerateArray())
                {
                    if (parameter.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    parameters.Add(parameter.GetString()!);
                }

                values.Add(string.Join(",", parameters));
            }
            else if (property.Name is "tag" or "name" or "version")
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                values.Add(property.Value.GetString()!);
            }
            else
            {
                return false;
            }
        }

        return seen.Contains("tag") && seen.Contains("name") && seen.Contains("version")
            && string.Equals(expected, GetObjectHash(values), StringComparison.Ordinal);
    }

    private static string GetObjectHash(IReadOnlyList<string> values)
    {
        var text = string.Join(";", values);
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(hash);
    }
}
