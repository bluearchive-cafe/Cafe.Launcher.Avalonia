function Add-CoverageBranchCount {
    param(
        [hashtable]$Counts,
        [string]$Key,
        [string]$ConditionCoverage,
        [string]$Location
    )

    if ($ConditionCoverage -notmatch '\((\d+)/(\d+)\)\s*$') {
        throw "Missing branch counts at $Location."
    }
    $covered = [int]$Matches[1]
    $total = [int]$Matches[2]
    if ($total -eq 0 -or $covered -gt $total) {
        throw "Invalid branch counts at ${Location}: $ConditionCoverage."
    }

    $previous = $Counts[$Key]
    if ($null -ne $previous -and $previous.Total -ne $total) {
        throw "Branch totals disagree at $Location."
    }
    # Cobertura omits outcome identities. The larger count is a lower bound on the union.
    if ($null -eq $previous -or $covered -gt $previous.Covered) {
        $Counts[$Key] = @{ Covered = $covered; Total = $total }
    }
}
