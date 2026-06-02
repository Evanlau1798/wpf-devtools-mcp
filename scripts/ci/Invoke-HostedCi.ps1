param(
    [ValidateSet('FocusedFlakes', 'UnitDebug', 'UnitRelease', 'FullManaged', 'NativeSmoke', 'NativeFull', 'HostedWindowsX64')]
    [string]$Mode = 'HostedWindowsX64',

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,

    [string]$WorkRoot = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'tmp\hosted-ci'),

    [ValidateRange(1, 100)]
    [int]$Repeat = 1,

    [ValidateRange(1, 8)]
    [int]$MaxParallelLanes = 4,

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
    [int]$ReleaseUnitShardCount = 8
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)
$workRootPath = [System.IO.Path]::GetFullPath($WorkRoot)
$outputRootPath = Join-Path $workRootPath 'output'
$localWorkRootPath = Join-Path $workRootPath 'local-work'
$startScriptPath = Join-Path $PSScriptRoot 'Start-SandboxCi.ps1'

New-Item -ItemType Directory -Force -Path $workRootPath | Out-Null
New-Item -ItemType Directory -Force -Path $outputRootPath | Out-Null

$gitCommand = Get-Command 'git.exe' -ErrorAction SilentlyContinue
if ($null -ne $gitCommand) {
    $trackedFiles = @(& $gitCommand.Source -C $repoRootPath ls-files 2>$null)
    if ($LASTEXITCODE -eq 0) {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllLines(
            (Join-Path $workRootPath 'git-tracked-files.txt'),
            [string[]]$trackedFiles,
            $utf8NoBom)
    }
}

& $startScriptPath `
    -Mode $Mode `
    -Repeat $Repeat `
    -MappedRepoRoot $repoRootPath `
    -MappedWorkRoot $workRootPath `
    -MappedOutputRoot $outputRootPath `
    -LocalWorkRoot $localWorkRootPath `
    -RunId ([guid]::NewGuid().ToString('N')) `
    -MaxParallelLanes $MaxParallelLanes `
    -UnitDebugShardCount $UnitDebugShardCount `
    -ReleaseUnitShardCount $ReleaseUnitShardCount
