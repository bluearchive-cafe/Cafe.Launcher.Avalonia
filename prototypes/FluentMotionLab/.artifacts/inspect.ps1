param(
  [Parameter(Mandatory = $true)][string]$Hwnd,
  [Parameter(Mandatory = $true)][string]$Label,
  [string]$Scenes = '',
  [string]$RapidFireScene,
  [string]$PostButtons = '',
  [int]$NextVariantPresses = 0,
  [string]$OutDir = ''
)

$sceneList = @($Scenes.Split('|', [System.StringSplitOptions]::RemoveEmptyEntries))
$postList = @($PostButtons.Split('|', [System.StringSplitOptions]::RemoveEmptyEntries))

$ErrorActionPreference = 'Stop'
$kagami = 'E:\Repos\kagami-desktop\artifacts\kagami-win-x64\kagami.exe'
$arrowRight = [string][char]0x2192

function Observe-Fresh {
    $r = & $kagami observe --hwnd $Hwnd --depth 1 --max-nodes 200 --interactive-only --include-locators interactive | ConvertFrom-Json
    if (-not $r.success) { throw "observe failed: $($r.error | ConvertTo-Json -Compress)" }
    if (-not $r.data.stable) { throw "observe unstable: $($r.data.instability_reasons -join ';')" }
    return $r.data
}

function Find-Button([string]$name) {
    $f = & $kagami find --hwnd $Hwnd --control-type Button --name $name --max-results 20 | ConvertFrom-Json
    if (-not $f.success) { throw "find failed for '$name'" }
    if ($f.data.Count -ne 1) { throw "expected exactly one Button '$name', got $($f.data.Count)" }
    return ($f.data[0].locator | ConvertTo-Json -Compress -Depth 8)
}

function Invoke-Named([string]$name) {
    $before = Observe-Fresh
    $loc = Find-Button $name
    $v = & $kagami invoke --locator $loc --expected-state $before.guard_path | ConvertFrom-Json
    if (-not $v.success) { throw "invoke '$name' failed: $($v.error | ConvertTo-Json -Compress -Depth 5)" }
    Start-Sleep -Milliseconds 80
}

function Get-Shot([string]$tag) {
    $s = & $kagami screenshot --hwnd $Hwnd --mode window | ConvertFrom-Json
    if (-not $s.success) { throw "screenshot '$tag' failed" }
    if ($s.data.fallback_used) { throw "screenshot '$tag' fell back to an unsafe capture mode" }
    if ($s.data.actual_mode -ne 'window') { throw "screenshot '$tag' actual_mode=$($s.data.actual_mode)" }
    return $s.data
}

function Save-Shot([string]$tag, $shotData) {
    if (-not $OutDir) { return }
    $name = ($tag -replace '[^A-Za-z0-9._-]+', '-') + '.png'
    Copy-Item -LiteralPath $shotData.path -Destination (Join-Path $OutDir $name) -Force
}

$results = [System.Collections.Generic.List[object]]::new()

for ($i = 0; $i -lt $NextVariantPresses; $i++) {
    Invoke-Named $arrowRight
    Start-Sleep -Milliseconds 120
}

if (($sceneList.Count -gt 0) -or $RapidFireScene) {
    Invoke-Named 'Reset'
    Start-Sleep -Milliseconds 250
}

foreach ($scene in $sceneList) {
    Invoke-Named $scene
    Start-Sleep -Milliseconds 150
    Invoke-Named 'Replay'
    Start-Sleep -Milliseconds 700
}

foreach ($button in $postList) {
    Invoke-Named $button
    Start-Sleep -Milliseconds 500
}

if ($sceneList.Count -gt 0) {
    $shot = Get-Shot ("{0} {1}" -f $Label, ($sceneList | Select-Object -Last 1))
    Save-Shot ("{0} scene {1}" -f $Label, ($sceneList | Select-Object -Last 1)) $shot
    $results.Add([pscustomobject]@{ kind = 'scene'; label = $Label; scene = ($sceneList | Select-Object -Last 1); path = $shot.path })
}

if ($RapidFireScene) {
    Invoke-Named $RapidFireScene
    Start-Sleep -Milliseconds 150
    Invoke-Named 'Rapid fire'
    $mid = Get-Shot ("{0} rapid-mid {1}" -f $Label, $RapidFireScene)
    Start-Sleep -Milliseconds 900
    $null = Observe-Fresh
    $end = Get-Shot ("{0} rapid-end {1}" -f $Label, $RapidFireScene)
    Save-Shot ("{0} rapid-mid {1}" -f $Label, $RapidFireScene) $mid
    Save-Shot ("{0} rapid-end {1}" -f $Label, $RapidFireScene) $end
    $results.Add([pscustomobject]@{ kind = 'rapidfire'; label = $Label; scene = $RapidFireScene; mid = $mid.path; end = $end.path })
}

$results | ConvertTo-Json -Depth 4
