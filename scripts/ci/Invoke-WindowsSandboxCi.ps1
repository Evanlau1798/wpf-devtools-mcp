param(
    [ValidateSet('FocusedFlakes', 'UnitDebug', 'UnitRelease', 'FullManaged', 'NativeSmoke', 'NativeFull', 'HostedWindowsX64', 'HostedWindowsX64Fast')]
    [string]$Mode = 'FocusedFlakes',

    [ValidateRange(1, 100)]
    [int]$Repeat = 1,

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,

    [string]$WorkRoot = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'tmp\sandbox-ci'),

    [ValidateRange(1, 86400)]
    [int]$WaitTimeoutSeconds = 7200,

    [ValidateRange(15, 1800)]
    [int]$GuestStartupTimeoutSeconds = 600,

    [ValidateRange(1, 8)]
    [int]$MaxParallelLanes = 2,

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
    [int]$ReleaseUnitShardCount = 1,

    [ValidateSet('Idle', 'BelowNormal', 'Normal', 'AboveNormal', 'High')]
    [string]$SandboxHostPriority = 'AboveNormal',

    [ValidatePattern('^(0x)?[0-9a-fA-F]+$')]
    [string]$SandboxHostProcessorAffinityHex = '',

    [switch]$SkipSandboxHostScheduling,

    [switch]$GenerateOnly,

    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SandboxCi.HostScheduling.ps1')

function Convert-ToXmlEscapedValue {
    param([Parameter(Mandatory = $true)] [string]$Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function Resolve-WindowsSandboxPath {
    $command = Get-Command 'WindowsSandbox.exe' -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }

    return [string]$command.Source
}

function Get-ActiveWindowsSandboxProcessSummaries {
    $sandboxProcessNames = @(
        'WindowsSandbox',
        'WindowsSandboxClient',
        'WindowsSandboxRemoteSession',
        'WindowsSandboxServer',
        'vmmemWindowsSandbox'
    )

    return @(Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $sandboxProcessNames -contains $_.ProcessName } |
        Sort-Object ProcessName, Id |
        ForEach-Object { "$($_.ProcessName):$($_.Id)" })
}

function Assert-NoActiveWindowsSandboxProcesses {
    param([Parameter(Mandatory = $true)] [string]$SandboxOutputPath)

    $activeSandboxProcesses = @(Get-ActiveWindowsSandboxProcessSummaries)
    if ($activeSandboxProcesses.Count -eq 0) {
        return
    }

    $cleanupCommand = ".\scripts\ci\Stop-WindowsSandboxHcs.ps1 -OutputRoot `"$SandboxOutputPath`" -WhatIf"
    throw (
        "Existing Windows Sandbox process(es) were found before launch: $($activeSandboxProcesses -join ', '). " +
        "Close Windows Sandbox or inspect cleanup candidates with: $cleanupCommand. " +
        "After verifying the candidates are Windows Sandbox compute systems, rerun cleanup with -Force or -Confirm:`$false."
    )
}

function Resolve-GitInstallRoot {
    $command = Get-Command 'git.exe' -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }

    $gitCommandPath = [System.IO.Path]::GetFullPath([string]$command.Source)
    $candidateRoot = Split-Path $gitCommandPath -Parent
    while (-not [string]::IsNullOrWhiteSpace($candidateRoot)) {
        if (Test-Path -LiteralPath (Join-Path $candidateRoot 'cmd\git.exe')) {
            return $candidateRoot
        }

        $parent = Split-Path $candidateRoot -Parent
        if ([string]::Equals($parent, $candidateRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $candidateRoot = $parent
    }

    return $null
}

function Resolve-VisualStudioInstallRoot {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswherePath) {
        $installPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null | Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($installPath) -and (Test-Path -LiteralPath $installPath -PathType Container)) {
            return [System.IO.Path]::GetFullPath([string]$installPath)
        }
    }

    $msbuildCommand = Get-Command 'MSBuild.exe' -ErrorAction SilentlyContinue
    if ($null -ne $msbuildCommand) {
        $candidateRoot = Split-Path ([System.IO.Path]::GetFullPath([string]$msbuildCommand.Source)) -Parent
        while (-not [string]::IsNullOrWhiteSpace($candidateRoot)) {
            if (Test-Path -LiteralPath (Join-Path $candidateRoot 'MSBuild\Current\Bin\MSBuild.exe')) {
                return $candidateRoot
            }

            $parent = Split-Path $candidateRoot -Parent
            if ([string]::Equals($parent, $candidateRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                break
            }

            $candidateRoot = $parent
        }
    }

    return $null
}

function New-MappedFolderXml {
    param(
        [Parameter(Mandatory = $true)] [string]$HostFolder,
        [string]$SandboxFolder,
        [bool]$ReadOnly = $true
    )

    if (-not (Test-Path -LiteralPath $HostFolder -PathType Container)) {
        return ''
    }

    $sandboxFolderXml = if ([string]::IsNullOrWhiteSpace($SandboxFolder)) {
        ''
    }
    else {
        "`n      <SandboxFolder>$(Convert-ToXmlEscapedValue $SandboxFolder)</SandboxFolder>"
    }

    $readOnlyText = if ($ReadOnly) { 'true' } else { 'false' }
    return @"
    <MappedFolder>
      <HostFolder>$(Convert-ToXmlEscapedValue ([System.IO.Path]::GetFullPath($HostFolder)))</HostFolder>$sandboxFolderXml
      <ReadOnly>$readOnlyText</ReadOnly>
    </MappedFolder>
"@
}

$repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)
$workRootPath = [System.IO.Path]::GetFullPath($WorkRoot)
$sandboxWorkPath = Join-Path $workRootPath 'work'
$sandboxOutputPath = Join-Path $workRootPath 'output'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runId = [guid]::NewGuid().ToString('N')
$configPath = Join-Path $workRootPath ("WpfDevTools-LocalCi-{0}.wsb" -f $timestamp)
$resultPath = Join-Path $sandboxOutputPath 'last-result.txt'

New-Item -ItemType Directory -Force -Path $sandboxWorkPath | Out-Null
New-Item -ItemType Directory -Force -Path $sandboxOutputPath | Out-Null
Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue

$sandboxRepoPath = 'C:\r'
$sandboxWorkMappedPath = 'C:\w'
$sandboxOutputMappedPath = 'C:\o'
$bootstrapPath = Join-Path $sandboxRepoPath 'scripts\ci\Start-SandboxCi.ps1'
$gitInstallRoot = Resolve-GitInstallRoot
$gitMappedFolder = if ([string]::IsNullOrWhiteSpace($gitInstallRoot)) {
    ''
}
else {
    New-MappedFolderXml -HostFolder $gitInstallRoot -SandboxFolder 'C:\Git'
}

$visualStudioInstallRoot = Resolve-VisualStudioInstallRoot
$vsInstallerRoot = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
$windowsKitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$netFxSdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\NETFXSDK'
$referenceAssembliesRoot = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies'
$microsoftSdksRoot = Join-Path ${env:ProgramFiles(x86)} 'Microsoft SDKs'
$nativeMappedFolders = @(
    if (-not [string]::IsNullOrWhiteSpace($visualStudioInstallRoot)) {
        New-MappedFolderXml -HostFolder $visualStudioInstallRoot -SandboxFolder $visualStudioInstallRoot
    }
    New-MappedFolderXml -HostFolder $vsInstallerRoot -SandboxFolder $vsInstallerRoot
    New-MappedFolderXml -HostFolder $windowsKitsRoot -SandboxFolder $windowsKitsRoot
    New-MappedFolderXml -HostFolder $windowsKitsRoot
    New-MappedFolderXml -HostFolder $netFxSdkRoot -SandboxFolder $netFxSdkRoot
    New-MappedFolderXml -HostFolder $netFxSdkRoot
    New-MappedFolderXml -HostFolder $referenceAssembliesRoot -SandboxFolder $referenceAssembliesRoot
    New-MappedFolderXml -HostFolder $microsoftSdksRoot -SandboxFolder $microsoftSdksRoot
    New-MappedFolderXml -HostFolder $microsoftSdksRoot
) -join ''

$trackedFilesPath = Join-Path $sandboxWorkPath 'git-tracked-files.txt'
$gitForManifest = Get-Command 'git.exe' -ErrorAction SilentlyContinue
if ($null -ne $gitForManifest) {
    $trackedFiles = @(& $gitForManifest.Source -C $repoRootPath ls-files 2>$null)
    if ($LASTEXITCODE -eq 0) {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllLines($trackedFilesPath, [string[]]$trackedFiles, $utf8NoBom)
    }
}

$command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$bootstrapPath`" -Mode $Mode -Repeat $Repeat -MappedRepoRoot `"$sandboxRepoPath`" -MappedWorkRoot `"$sandboxWorkMappedPath`" -MappedOutputRoot `"$sandboxOutputMappedPath`" -RunId $runId -MaxParallelLanes $MaxParallelLanes -UnitDebugShardCount $UnitDebugShardCount -ReleaseUnitShardCount $ReleaseUnitShardCount"

$config = @"
<Configuration>
  <VGpu>Enable</VGpu>
  <Networking>Enable</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$(Convert-ToXmlEscapedValue $repoRootPath)</HostFolder>
      <SandboxFolder>$(Convert-ToXmlEscapedValue $sandboxRepoPath)</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$(Convert-ToXmlEscapedValue $sandboxWorkPath)</HostFolder>
      <SandboxFolder>$(Convert-ToXmlEscapedValue $sandboxWorkMappedPath)</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$(Convert-ToXmlEscapedValue $sandboxOutputPath)</HostFolder>
      <SandboxFolder>$(Convert-ToXmlEscapedValue $sandboxOutputMappedPath)</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
$gitMappedFolder$nativeMappedFolders
  </MappedFolders>
  <LogonCommand>
    <Command>$(Convert-ToXmlEscapedValue $command)</Command>
  </LogonCommand>
</Configuration>
"@

Set-Content -LiteralPath $configPath -Value $config -Encoding UTF8

Write-Host "Windows Sandbox config: $configPath"
Write-Host "Sandbox work root: $sandboxWorkPath"
Write-Host "Sandbox output root: $sandboxOutputPath"
if (-not [string]::IsNullOrWhiteSpace($gitInstallRoot)) {
  Write-Host "Mapped Git root: $gitInstallRoot"
}
if (-not [string]::IsNullOrWhiteSpace($visualStudioInstallRoot)) {
  Write-Host "Mapped Visual Studio root: $visualStudioInstallRoot"
}
Write-Host "Mode: $Mode"
Write-Host "Run ID: $runId"
Write-Host "Repeat: $Repeat"
Write-Host "Wait timeout seconds: $WaitTimeoutSeconds"
Write-Host "Guest startup timeout seconds: $GuestStartupTimeoutSeconds"
Write-Host "Max parallel lanes: $MaxParallelLanes"
Write-Host "Unit debug shard count: $UnitDebugShardCount"
Write-Host "Release unit shard count: $ReleaseUnitShardCount"
Write-Host "Sandbox host priority: $SandboxHostPriority"
if (-not [string]::IsNullOrWhiteSpace($SandboxHostProcessorAffinityHex)) {
    Write-Host "Sandbox host processor affinity: $SandboxHostProcessorAffinityHex"
}
if ($SkipSandboxHostScheduling) {
    Write-Host 'Sandbox host scheduling tuning: disabled'
}
else {
    Write-Host 'Sandbox host scheduling tuning: enabled'
}

if ($GenerateOnly) {
    Write-Host 'GenerateOnly was specified; not launching Windows Sandbox.'
    return
}

$sandboxPath = Resolve-WindowsSandboxPath
if ([string]::IsNullOrWhiteSpace($sandboxPath)) {
    throw 'WindowsSandbox.exe was not found. Enable Windows Sandbox from Windows Features, reboot, then rerun this script. The .wsb file was still generated.'
}

Assert-NoActiveWindowsSandboxProcesses -SandboxOutputPath $sandboxOutputPath
Start-Process -FilePath $sandboxPath -ArgumentList @("`"$configPath`"") | Out-Null
if (-not $SkipSandboxHostScheduling) {
    Set-SandboxHostScheduling -PriorityClass $SandboxHostPriority -ProcessorAffinityHex $SandboxHostProcessorAffinityHex
}

if ($NoWait) {
    return
}

$deadline = [DateTime]::UtcNow.AddSeconds($WaitTimeoutSeconds)
$startupDeadline = [DateTime]::UtcNow.AddSeconds($GuestStartupTimeoutSeconds)
$guestStarted = $false
while ([DateTime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $resultPath) {
        try {
            $result = (Get-Content -LiteralPath $resultPath -Raw).Trim()
        }
        catch [System.IO.IOException], [System.UnauthorizedAccessException] {
            Start-Sleep -Seconds 1
            continue
        }

        if ($result.StartsWith("PASS $runId ", [System.StringComparison]::Ordinal)) {
            Write-Host $result
            return
        }

        if ($result.StartsWith("FAIL $runId ", [System.StringComparison]::Ordinal)) {
            throw $result
        }

        if (-not $guestStarted -and $result.StartsWith("RUNNING $runId ", [System.StringComparison]::Ordinal)) {
            $guestStarted = $true
            Write-Host $result
        }
    }

    if (-not $guestStarted -and [DateTime]::UtcNow -ge $startupDeadline) {
        $cleanupCommand = ".\scripts\ci\Stop-WindowsSandboxHcs.ps1 -OutputRoot `"$sandboxOutputPath`" -WhatIf"
        throw (
            "Windows Sandbox guest did not write RUNNING/PASS/FAIL within $GuestStartupTimeoutSeconds seconds. " +
            "RunId: $runId. Inspect the generated .wsb file: $configPath. " +
            "Inspect cleanup candidates with: $cleanupCommand. " +
            "After verifying the candidates are Windows Sandbox compute systems, rerun cleanup with -Force or -Confirm:`$false."
        )
    }

    Start-Sleep -Seconds 5
}

$cleanupCommand = ".\scripts\ci\Stop-WindowsSandboxHcs.ps1 -OutputRoot `"$sandboxOutputPath`" -WhatIf"
throw ("Timed out waiting for Windows Sandbox CI result after $WaitTimeoutSeconds seconds. RunId: $runId. Inspect cleanup candidates with: $cleanupCommand. After verifying the candidates are Windows Sandbox compute systems, rerun cleanup with " + '-Force or -Confirm:$false.')
