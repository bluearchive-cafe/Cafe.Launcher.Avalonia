#requires -Version 7.0
<#
.SYNOPSIS
Bounded, read-only research sampling of BA JP and Stella CN official protocols.
.DESCRIPTION
Reads signing constants from existing official ASAR packages without extracting or
executing them. Never persists signing headers, cookies, salts or binary bodies.
This is an opt-in research tool, not a runtime HTTP path or an offline CI gate.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/yostar-protocol-probe'),
    [string]$SamplesRoot = 'E:/Repos/_GameLauncher_Samples',
    [switch]$SkipNetwork,
    [switch]$ResumeBlockedFileProbes
)

$ErrorActionPreference = 'Stop'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$records = [Collections.Generic.List[object]]::new()
$sources = [Collections.Generic.List[object]]::new()
$started = [DateTimeOffset]::UtcNow
if ($ResumeBlockedFileProbes) {
    if ($SkipNetwork) { throw 'ResumeBlockedFileProbes and SkipNetwork cannot be combined.' }
    $previousText = [IO.File]::ReadAllText((Join-Path $outputRoot 'evidence.json'))
    $previous = ConvertFrom-Json -InputObject $previousText
    # ConvertFrom-Json may coerce ISO strings to local DateTime. Preserve original
    # timestamp text, offsets and fractional seconds when appending observations.
    $document = [Text.Json.JsonDocument]::Parse($previousText)
    try {
        $started = [DateTimeOffset]::Parse($document.RootElement.GetProperty('started_at_utc').GetString()).ToUniversalTime()
        $originals = $document.RootElement.GetProperty('observations')
        for ($index = 0; $index -lt $previous.observations.Count; $index++) {
            $record = $previous.observations[$index]
            foreach ($field in @('sampled_at_utc', 'sampled_at_singapore')) {
                if ($record.PSObject.Properties.Name -contains $field) {
                    $record.$field = $originals[$index].GetProperty($field).GetString()
                }
            }
            $records.Add($record)
        }
    } finally { $document.Dispose() }
}
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(25)
$profiles = @(
    @{
        Id = 'blue-archive-jp'; Tag = 'BlueArchive_JP'; Version = '1.7.2'
        Api = 'https://api-launcher-jp.yo-star.com'
        Asar = 'BlueArchive_JP_Launcher/app-32_v1.7.2/resources/app.asar'
        # Backup hostname was observed in the 2026-11-02 official CDN response.
        Hosts = @('api-launcher-jp.yo-star.com', 'launcher-pkg-ba-jp.yo-star.com', 'launcher-pkg-ba-jp-bk.yo-star.com')
    },
    @{
        Id = 'stella-sora-cn'; Tag = 'StellaSora_CN'; Version = '1.3.0'
        Api = 'https://launcher-api.yostar.net'
        Asar = '_StellaSora_Gamelauncher/cn_app-32/resources/app.asar'
        Hosts = @('launcher-api.yostar.net', 'game-launcher-ss-cn.yostar.net', 'game-launcher-ss-cn-bk.yostar.net')
    }
)

function Save-Json([string]$Name, [object]$Value) {
    $json = ConvertTo-Json -InputObject $Value -Depth 35
    [IO.File]::WriteAllText((Join-Path $outputRoot $Name), ($json -replace "`r`n", "`n") + "`n", $utf8)
}

function Get-Sha256([byte[]]$Bytes) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Get-ErrorLayers([Exception]$Exception) {
    # Messages/stack traces can contain request data. Retain safe typed layers only.
    $layers = @()
    for ($item = $Exception; $item; $item = $item.InnerException) {
        $layer = [ordered]@{ type = $item.GetType().FullName }
        if ($item -is [Net.Http.HttpRequestException]) { $layer.http_request_error = [string]$item.HttpRequestError }
        if ($item -is [Net.Sockets.SocketException]) { $layer.socket_error = [string]$item.SocketErrorCode }
        $layers += $layer
    }
    return $layers
}

function Read-AsarMember([IO.FileStream]$Stream, [object]$Header, [long]$DataOffset, [string]$Path) {
    $entry = $Header
    foreach ($part in $Path.Split('/')) { $entry = $entry.files.$part }
    if (-not $entry -or $entry.unpacked -or $entry.link -or $entry.size -gt 4194304) {
        throw 'Required bounded ASAR member is missing, unpacked, linked or too large.'
    }
    $Stream.Position = $DataOffset + [long]$entry.offset
    $bytes = [byte[]]::new([int]$entry.size)
    $Stream.ReadExactly($bytes, 0, $bytes.Length)
    return ,$bytes
}

function Read-OfficialSource([hashtable]$Profile) {
    $path = Join-Path $SamplesRoot $Profile.Asar
    $stream = [IO.File]::OpenRead($path)
    try {
        $prefix = [byte[]]::new(16)
        $stream.ReadExactly($prefix, 0, 16)
        $headerSize = [BitConverter]::ToUInt32($prefix, 4)
        $jsonSize = [BitConverter]::ToUInt32($prefix, 12)
        if ($jsonSize -gt 8388608 -or $headerSize -lt ($jsonSize + 8)) { throw 'ASAR header bounds failed.' }
        $jsonBytes = [byte[]]::new($jsonSize)
        $stream.ReadExactly($jsonBytes, 0, $jsonBytes.Length)
        $header = ConvertFrom-Json -InputObject $utf8.GetString($jsonBytes)
        $dataOffset = 8L + $headerSize
        $packageBytes = Read-AsarMember $stream $header $dataOffset 'package.json'
        $mainBytes = Read-AsarMember $stream $header $dataOffset 'out/main/index.js'
        $package = ConvertFrom-Json -InputObject $utf8.GetString($packageBytes)
        $main = $utf8.GetString($mainBytes)
        if ($package.version -ne $Profile.Version -or -not $main.Contains($Profile.Api)) {
            throw 'Official source version/API does not match the declared observation profile.'
        }
        $match = [regex]::Match($main, 'gameId:\s*"' + [regex]::Escape($Profile.Tag) + '"\s*,\s*version:\s*electron\.app\.getVersion\(\),\s*salt:\s*"([A-Fa-f0-9]+)"')
        if (-not $match.Success) { throw 'The previously observed signing source shape changed; no guessing is allowed.' }
        $Profile.Salt = $match.Groups[1].Value
        $members = @(
            [ordered]@{ path = 'package.json'; sha256 = Get-Sha256 $packageBytes },
            [ordered]@{ path = 'out/main/index.js'; sha256 = Get-Sha256 $mainBytes }
        )
        foreach ($member in @($header.files.out.files.main.files.PSObject.Properties.Name | Where-Object { $_ -match '^index-[^.]+\.js$' })) {
            $relative = 'out/main/' + $member
            $memberBytes = Read-AsarMember $stream $header $dataOffset $relative
            $members += [ordered]@{ path = $relative; sha256 = Get-Sha256 $memberBytes }
        }
        $sources.Add([ordered]@{
            game = $Profile.Id; official_launcher_version = $package.version
            source_relative_path = $Profile.Asar
            asar_sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            members = $members
        })
    } finally { $stream.Dispose() }
}

function New-OfficialAuthorization([hashtable]$Profile) {
    $head = [ordered]@{
        game_tag = $Profile.Tag
        time = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        version = $Profile.Version
    } | ConvertTo-Json -Compress
    $sign = [Convert]::ToHexString([Security.Cryptography.MD5]::HashData($utf8.GetBytes($head + $Profile.Salt))).ToLowerInvariant()
    return '{"head":' + $head + ',"sign":"' + $sign + '"}'
}

function Read-Remote([hashtable]$Profile, [string]$Name, [string]$Url,
    [string]$Method = 'GET', [int]$BodyLimit = 262144, [switch]$Signed, [switch]$RangeProbe) {
    $record = [ordered]@{
        game = $Profile.Id; stage = $Name; sampled_at_utc = [DateTimeOffset]::UtcNow.ToString('o')
        sampled_at_singapore = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('o')
        method = $Method; url = $Url; status = $null; outcome = 'pending'
        body_limit = $BodyLimit; requested_range = $(if ($RangeProbe) { 'bytes=0-4095' } else { $null })
    }
    $request = $null; $response = $null; $stream = $null
    try {
        $uri = [Uri]$Url
        if ($uri.Scheme -ne 'https' -or $uri.Host -notin $Profile.Hosts -or $uri.UserInfo -or -not $uri.IsDefaultPort) {
            $record.outcome = 'blocked-host-policy'
            return $null
        }
        if ($Signed -and $uri.Host -ne ([Uri]$Profile.Api).Host) { throw 'Signing is restricted to the game API host.' }
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), $uri)
        if ($Signed) { $request.Headers.TryAddWithoutValidation('Authorization', (New-OfficialAuthorization $Profile)) | Out-Null }
        if ($RangeProbe) { $request.Headers.TryAddWithoutValidation('Range', 'bytes=0-4095') | Out-Null }
        $response = $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $record.status = [int]$response.StatusCode
        $record.response_headers = [ordered]@{}
        foreach ($name in @('Content-Type', 'Content-Length', 'Content-Range', 'Accept-Ranges', 'ETag', 'x-oss-hash-crc64ecma')) {
            $values = $null
            if ($response.Headers.TryGetValues($name, [ref]$values) -or $response.Content.Headers.TryGetValues($name, [ref]$values)) {
                $record.response_headers[$name] = @($values) -join ', '
            }
        }
        $bytes = [byte[]]::new(0)
        if ($Method -ne 'HEAD') {
            $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
            $memory = [IO.MemoryStream]::new()
            $timeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(25))
            try {
                $buffer = [byte[]]::new(8192)
                while ($memory.Length -lt $BodyLimit) {
                    $count = [Math]::Min($buffer.Length, $BodyLimit - [int]$memory.Length)
                    $read = $stream.ReadAsync($buffer, 0, $count, $timeout.Token).GetAwaiter().GetResult()
                    if (-not $read) { break }
                    $memory.Write($buffer, 0, $read)
                }
                $bytes = $memory.ToArray()
            } finally { $timeout.Dispose(); $memory.Dispose() }
        }
        $record.bytes_read = $bytes.Length
        $record.body_limit_reached = $bytes.Length -eq $BodyLimit
        if ($bytes.Length) { $record.original_body_sha256 = Get-Sha256 $bytes }
        if ($RangeProbe -and $bytes.Length) { $record.prefix_hex = [Convert]::ToHexString($bytes[0..([Math]::Min(15, $bytes.Length - 1))]) }
        $record.outcome = $(if ([int]$response.StatusCode -ge 300 -and [int]$response.StatusCode -lt 400) { 'redirect-not-followed' }
            elseif (-not $response.IsSuccessStatusCode) { 'http-failure' } else { 'received' })
        return [pscustomobject]@{ Record = $record; Text = $utf8.GetString($bytes) }
    } catch {
        $record.outcome = 'transport-or-read-failure'
        $record.error_layers = @(Get-ErrorLayers $_.Exception)
        return $null
    } finally {
        $records.Add($record)
        if ($stream) { $stream.Dispose() }
        if ($response) { $response.Dispose() }
        if ($request) { $request.Dispose() }
    }
}

function Read-Api([hashtable]$Profile, [string]$Name, [string]$Path) {
    $result = Read-Remote $Profile $Name ($Profile.Api + '/api/launcher/' + $Path) -Signed
    if (-not $result -or $result.Record.outcome -ne 'received' -or $result.Record.body_limit_reached) { return $null }
    try {
        $body = ConvertFrom-Json -InputObject $result.Text
        $result.Record.api_code = $body.code
        if ($result.Record.status -ne 200 -or $body.code -ne 200) { $result.Record.outcome = 'api-failure'; return $null }
        # Only these public configuration endpoints are permitted by this script.
        Save-Json ($Profile.Id + '-' + $Name + '.json') $body
        return $body.data
    } catch { $result.Record.outcome = 'json-failure'; return $null }
}

try {
    foreach ($profile in $profiles) {
        try { Read-OfficialSource $profile }
        catch {
            $records.Add([ordered]@{ game = $profile.Id; stage = 'official-source'; outcome = 'source-validation-failure'; error_layers = @(Get-ErrorLayers $_.Exception) })
            continue
        }
        if ($SkipNetwork) { continue }
        if ($ResumeBlockedFileProbes) {
            $blocked = @($previous.observations | Where-Object { $_.game -eq $profile.Id -and $_.outcome -eq 'blocked-host-policy' })
            foreach ($record in $blocked) {
                # Only retry an exact previously blocked file URL and bounded method;
                # do not repeat API/manifest reads or broaden the hostname policy.
                if ($record.stage -match '-executable-head$' -and $record.method -eq 'HEAD') {
                    Read-Remote $profile $record.stage $record.url -Method HEAD | Out-Null
                } elseif ($record.stage -match '-executable-range$' -and $record.requested_range -eq 'bytes=0-4095') {
                    Read-Remote $profile $record.stage $record.url -BodyLimit 4096 -RangeProbe | Out-Null
                }
            }
            continue
        }
        # Independent endpoints remain observable even when the other fails.
        $game = Read-Api $profile 'game-config' 'game/config'
        $cdn = Read-Api $profile 'cdn-config' 'advanced/game/download/cdn'
        if (-not $game) {
            $records.Add([ordered]@{ game = $profile.Id; stage = 'manifest-location/manifest/executable'; outcome = 'not-attempted'; reason = 'game-config unavailable; no guessed URL or alternate signature' })
            continue
        }
        $manifestPath = 'game/config/json?version=' + [Uri]::EscapeDataString($game.game_latest_version) + '&file_path=' + [Uri]::EscapeDataString($game.game_latest_file_path)
        $location = Read-Api $profile 'manifest-location' $manifestPath
        if (-not $location) {
            $records.Add([ordered]@{ game = $profile.Id; stage = 'manifest/executable'; outcome = 'not-attempted'; reason = 'manifest-location unavailable' })
            continue
        }
        $result = Read-Remote $profile 'manifest' $location.url -BodyLimit 4194304
        if (-not $result -or $result.Record.status -ne 200 -or $result.Record.body_limit_reached) {
            $records.Add([ordered]@{ game = $profile.Id; stage = 'executable'; outcome = 'not-attempted'; reason = 'complete bounded manifest unavailable' })
            continue
        }
        try { $manifest = ConvertFrom-Json -InputObject $result.Text }
        catch { $result.Record.outcome = 'json-failure'; continue }
        $executables = @($manifest.file | Where-Object { $_.path -match '\.exe$' })
        $scripts = @($manifest.file | Where-Object { $_.path -match '\.(bat|cmd|ps1|sh)$' })
        Save-Json ($profile.Id + '-manifest-selected.json') ([ordered]@{
            sample_kind = 'Selected original entries; not a complete installable manifest'
            source_url = $location.url; original_body_sha256 = $result.Record.original_body_sha256
            original_body_bytes = $result.Record.bytes_read; original_root_fields = @($manifest.PSObject.Properties.Name)
            source = $manifest.source; original_file_count = @($manifest.file).Count
            original_total_file_bytes = [long](($manifest.file | ForEach-Object { [long]$_.size } | Measure-Object -Sum).Sum)
            executable_entries = $executables; script_entries = $scripts
            file = @($manifest.file | Select-Object -First 3)
        })
        $entry = $manifest.file | Where-Object { $_.path -eq ('/' + $game.game_start_exe_name + '.exe') } | Select-Object -First 1
        if (-not $cdn -or -not $entry) {
            $records.Add([ordered]@{ game = $profile.Id; stage = 'executable'; outcome = 'not-attempted'; reason = 'CDN config or exact API start entry unavailable' })
            continue
        }
        foreach ($key in @('primary_cdn', 'back_up_cdn')) {
            $domain = $cdn.$key
            if (-not $domain) { continue }
            $segments = @($manifest.source.Split('/') + $entry.path.Split('/') | Where-Object { $_ })
            $segments[-1] = [Uri]::EscapeDataString($segments[-1])
            $url = $domain.TrimEnd('/') + '/' + ($segments -join '/')
            Read-Remote $profile ($key + '-executable-head') $url -Method HEAD | Out-Null
            Read-Remote $profile ($key + '-executable-range') $url -BodyLimit 4096 -RangeProbe | Out-Null
        }
    }
} finally {
    Save-Json 'official-source-hashes.json' @($sources.ToArray())
    Save-Json 'evidence.json' ([ordered]@{
        started_at_utc = $started.ToString('o'); finished_at_utc = [DateTimeOffset]::UtcNow.ToString('o')
        started_at_singapore = $started.ToOffset([TimeSpan]::FromHours(8)).ToString('o')
        finished_at_singapore = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('o')
        network_skipped = [bool]$SkipNetwork; redirects_followed = $false; cookies_enabled = $false
        authentication_headers_or_salts_persisted = $false; binary_bodies_saved = $false
        game_state_written = $false; binaries_executed = $false; observations = @($records.ToArray())
    })
    $client.Dispose()
}
foreach ($record in $records) { Write-Host ('{0} {1}: {2} HTTP {3}' -f $record.game, $record.stage, $record.outcome, $record.status) }
