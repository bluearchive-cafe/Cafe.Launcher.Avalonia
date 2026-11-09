# Compatibility entry point; the catalog owns all generated projections.
& "$PSScriptRoot/Generate-LocalizationCatalog.ps1" @args
if (-not $?) { exit 1 }
