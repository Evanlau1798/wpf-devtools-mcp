param(
    [Parameter(Mandatory = $true)] [string]$PackageArchivePath,
    [string]$WorkRoot = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'tmp\sandbox-ci\artifact-preflight'),
    [ValidateRange(1, 86400)]
    [int]$WaitTimeoutSeconds = 1800,
    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture = 'x64',
    [string]$Client = 'other',
    [string]$SmokeTargetPath = '',
    [ValidatePattern('^[0-9A-Za-z_.-]+$')]
    [string]$DotNetChannel = '8.0',
    [string]$DotNetInstallScriptUrl = 'https://dot.net/v1/dotnet-install.ps1',
    [ValidateSet('Idle', 'BelowNormal', 'Normal', 'AboveNormal', 'High')]
    [string]$SandboxHostPriority = 'AboveNormal',
    [ValidatePattern('^(0x)?[0-9a-fA-F]+$')]
    [string]$SandboxHostProcessorAffinityHex = '',
    [switch]$SkipSandboxHostScheduling,
    [switch]$SkipDotNetProvisioning,
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

function New-MappedFolderXml {
    param(
        [Parameter(Mandatory = $true)] [string]$HostFolder,
        [Parameter(Mandatory = $true)] [string]$SandboxFolder,
        [bool]$ReadOnly = $true
    )

    $readOnlyText = if ($ReadOnly) { 'true' } else { 'false' }
    return @"
    <MappedFolder>
      <HostFolder>$(Convert-ToXmlEscapedValue ([System.IO.Path]::GetFullPath($HostFolder)))</HostFolder>
      <SandboxFolder>$(Convert-ToXmlEscapedValue $SandboxFolder)</SandboxFolder>
      <ReadOnly>$readOnlyText</ReadOnly>
    </MappedFolder>
"@
}

function Copy-PreflightBootstrapFiles {
    param([Parameter(Mandatory = $true)] [string]$Destination)

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SandboxCi.ArtifactPreflight.ps1') -Destination (Join-Path $Destination 'SandboxCi.ArtifactPreflight.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\tools\packaging\Test-PackagedServerRuntime.ps1') -Destination (Join-Path $Destination 'Test-PackagedServerRuntime.ps1') -Force
}

$resolvedPackageArchive = (Resolve-Path -LiteralPath $PackageArchivePath).Path
$packageDirectory = [System.IO.Path]::GetDirectoryName($resolvedPackageArchive)
$packageFileName = [System.IO.Path]::GetFileName($resolvedPackageArchive)
$workRootPath = [System.IO.Path]::GetFullPath($WorkRoot)
$bootstrapRoot = Join-Path $workRootPath 'preflight'
$sandboxOutputPath = Join-Path $workRootPath 'output'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runId = [guid]::NewGuid().ToString('N')
$configPath = Join-Path $workRootPath ("WpfDevTools-ArtifactPreflight-{0}.wsb" -f $timestamp)
$resultPath = Join-Path $sandboxOutputPath 'last-result.txt'

New-Item -ItemType Directory -Force -Path $workRootPath, $sandboxOutputPath | Out-Null
Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
Copy-PreflightBootstrapFiles -Destination $bootstrapRoot

$sandboxReleasePath = 'C:\release'
$sandboxPreflightPath = 'C:\preflight'
$sandboxOutputMappedPath = 'C:\preflight-output'
$sandboxPackageArchive = Join-Path $sandboxReleasePath $packageFileName
$sandboxSmokeTargetArgument = ''
$dotNetProvisioningArgument = ''
$smokeTargetMapping = ''
if (-not [string]::IsNullOrWhiteSpace($SmokeTargetPath)) {
    $resolvedSmokeTarget = (Resolve-Path -LiteralPath $SmokeTargetPath).Path
    $smokeTargetDirectory = [System.IO.Path]::GetDirectoryName($resolvedSmokeTarget)
    $smokeTargetFileName = [System.IO.Path]::GetFileName($resolvedSmokeTarget)
    $sandboxSmokeRoot = 'C:\smoke-target'
    $sandboxSmokeTarget = Join-Path $sandboxSmokeRoot $smokeTargetFileName
    $smokeTargetMapping = New-MappedFolderXml -HostFolder $smokeTargetDirectory -SandboxFolder $sandboxSmokeRoot -ReadOnly $true
    $sandboxSmokeTargetArgument = " -SmokeTargetPath `"$sandboxSmokeTarget`""
}
if ($SkipDotNetProvisioning) {
    $dotNetProvisioningArgument = ' -SkipDotNetProvisioning'
}

$command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$sandboxPreflightPath\SandboxCi.ArtifactPreflight.ps1`" -PackageArchivePath `"$sandboxPackageArchive`" -OutputRoot `"$sandboxOutputMappedPath`" -RunId $runId -Architecture $Architecture -Client $Client -DotNetChannel `"$DotNetChannel`" -DotNetInstallScriptUrl `"$DotNetInstallScriptUrl`"$sandboxSmokeTargetArgument$dotNetProvisioningArgument"

$config = @"
<Configuration>
  <VGpu>Enable</VGpu>
  <Networking>Enable</Networking>
  <MappedFolders>
$(New-MappedFolderXml -HostFolder $packageDirectory -SandboxFolder $sandboxReleasePath -ReadOnly $true)
$(New-MappedFolderXml -HostFolder $bootstrapRoot -SandboxFolder $sandboxPreflightPath -ReadOnly $true)
$(New-MappedFolderXml -HostFolder $sandboxOutputPath -SandboxFolder $sandboxOutputMappedPath -ReadOnly $false)
$smokeTargetMapping
  </MappedFolders>
  <LogonCommand>
    <Command>$(Convert-ToXmlEscapedValue $command)</Command>
  </LogonCommand>
</Configuration>
"@

Set-Content -LiteralPath $configPath -Value $config -Encoding UTF8

Write-Host "Windows Sandbox artifact preflight config: $configPath"
Write-Host "Package archive: $resolvedPackageArchive"
Write-Host "Preflight bootstrap root: $bootstrapRoot"
Write-Host "Preflight output root: $sandboxOutputPath"
Write-Host "Architecture: $Architecture"
Write-Host "Client: $Client"
Write-Host "DotNet channel: $DotNetChannel"
Write-Host "Run ID: $runId"
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

Start-Process -FilePath $sandboxPath -ArgumentList @("`"$configPath`"") | Out-Null
if (-not $SkipSandboxHostScheduling) {
    Set-SandboxHostScheduling -PriorityClass $SandboxHostPriority -ProcessorAffinityHex $SandboxHostProcessorAffinityHex
}

if ($NoWait) {
    return
}

$deadline = [DateTime]::UtcNow.AddSeconds($WaitTimeoutSeconds)
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
    }

    Start-Sleep -Seconds 5
}

$cleanupCommand = ".\scripts\ci\Stop-WindowsSandboxHcs.ps1 -OutputRoot `"$sandboxOutputPath`" -WhatIf"
throw "Timed out waiting for Windows Sandbox artifact preflight result after $WaitTimeoutSeconds seconds. RunId: $runId. Inspect cleanup candidates with: $cleanupCommand. Remove -WhatIf only after verifying the candidates are Windows Sandbox compute systems."
