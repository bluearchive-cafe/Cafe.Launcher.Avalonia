param([Parameter(Mandatory = $true)][string]$Hwnd, [int]$Presses = 1)

$ErrorActionPreference = 'Stop'
$kagami = 'E:\Repos\kagami-desktop\artifacts\kagami-win-x64\kagami.exe'

function Find-Button([string]$name) {
    $f = & $kagami find --hwnd $Hwnd --control-type Button --name $name --max-results 20 | ConvertFrom-Json
    if (-not $f.success) { throw "find failed '$name'" }
    if ($f.data.Count -ne 1) { throw "find '$name' count $($f.data.Count)" }
    return ($f.data[0].locator | ConvertTo-Json -Compress -Depth 8)
}

for ($i = 0; $i -lt $Presses; $i++) {
    $loc = Find-Button 'Replay'
    $v = & $kagami invoke --locator $loc | ConvertFrom-Json
    if (-not $v.success) { throw "invoke $($v.error | ConvertTo-Json -Compress)" }
    Start-Sleep -Milliseconds 500
}

$s = & $kagami screenshot --hwnd $Hwnd --mode window | ConvertFrom-Json
if (-not $s.success) { throw 'shot failed' }
$s.data.path
