param(
    [string]$WorkRoot = '',

    [ValidateRange(1, 8)]
    [int]$MaxParallelLanes = 8,

    [ValidateScript({
        if ($_ -eq 1 -or $_ -eq 4) {
            return $true
        }

        throw 'UnitDebugShardCount currently supports 1 or 4.'
    })]
    [int]$UnitDebugShardCount = 1,

    [ValidateScript({
        if ($_ -eq 1 -or $_ -eq 4 -or $_ -eq 8) {
            return $true
        }

        throw 'ReleaseUnitShardCount currently supports 1, 4, or 8.'
    })]
    [int]$ReleaseUnitShardCount = 8,

    [ValidateRange(1, 100)]
    [int]$Repeat = 1
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$entryPoint = Join-Path $repoRoot 'scripts\ci\Invoke-HostedCi.ps1'
$defaultWorkRoot = Join-Path $repoRoot 'tmp\hosted-ci'
$resolvedWorkRoot = if ([string]::IsNullOrWhiteSpace($WorkRoot)) {
    $defaultWorkRoot
}
else {
    $WorkRoot
}

& $entryPoint `
    -Mode HostedWindowsX64 `
    -WorkRoot $resolvedWorkRoot `
    -MaxParallelLanes $MaxParallelLanes `
    -UnitDebugShardCount $UnitDebugShardCount `
    -ReleaseUnitShardCount $ReleaseUnitShardCount `
    -Repeat $Repeat
