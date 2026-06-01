param(
    [Parameter(Mandatory = $true)] [string]$PackageArchivePath,
    [string]$WorkRoot = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'tmp\sandbox-ci\artifact-preflight'),
    [ValidateRange(1, 86400)]
    [int]$WaitTimeoutSeconds = 1800,
    [ValidateRange(15, 1800)]
    [int]$GuestStartupTimeoutSeconds = 600,
    [ValidateSet('x64', 'x86')]
    [string]$Architecture = 'x64',
    [ValidateSet('claude-code', 'codex', 'cursor', 'vscode', 'visual-studio', 'claude-desktop', 'other')]
    [string]$Client = 'other',
    [string]$SmokeTargetPath = '',
    [ValidatePattern('^[0-9A-Za-z_.-]+$')]
    [string]$DotNetChannel = '8.0',
    [ValidateScript({
        $uri = $null
        if ([System.Uri]::TryCreate([string]$_, [System.UriKind]::Absolute, [ref]$uri) -and $uri.Scheme -eq 'https') {
            return $true
        }

        throw 'DotNetInstallScriptUrl must be an absolute HTTPS URI.'
    })]
    [string]$DotNetInstallScriptUrl = 'https://dot.net/v1/dotnet-install.ps1',
    [ValidateSet('Idle', 'BelowNormal', 'Normal', 'AboveNormal', 'High')]
    [string]$SandboxHostPriority = 'AboveNormal',
    [ValidatePattern('^(0x)?[0-9a-fA-F]+$')]
    [string]$SandboxHostProcessorAffinityHex = '',
    [switch]$SkipSandboxHostScheduling,
    [switch]$SkipDotNetProvisioning,
    [string]$TrustedCodeSigningCertificatePath = '',
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

function Convert-ToPowerShellSingleQuotedLiteral {
    param([Parameter(Mandatory = $true)] [string]$Value)

    if ($Value.IndexOfAny([char[]]@([char]10, [char]13)) -ge 0) {
        throw "Sandbox command argument contains unsupported newline characters: $Value"
    }

    return "'" + $Value.Replace("'", "''") + "'"
}

function Convert-ToEncodedPowerShellCommand {
    param([Parameter(Mandatory = $true)] [string]$Command)

    return [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($Command))
}

function New-SandboxCommandSwitch {
    param([Parameter(Mandatory = $true)] [string]$Text)

    return [pscustomobject]@{
        Text    = $Text
        IsValue = $false
    }
}

function New-SandboxCommandValue {
    param([Parameter(Mandatory = $true)] [string]$Text)

    return [pscustomobject]@{
        Text    = $Text
        IsValue = $true
    }
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
        'WindowsSandboxServer'
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
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SandboxCi.Process.ps1') -Destination (Join-Path $Destination 'SandboxCi.Process.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SandboxCi.ProcessCleanup.ps1') -Destination (Join-Path $Destination 'SandboxCi.ProcessCleanup.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SandboxCi.TrustedSigner.ps1') -Destination (Join-Path $Destination 'SandboxCi.TrustedSigner.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\tools\packaging\Test-PackagedServerRuntime.ps1') -Destination (Join-Path $Destination 'Test-PackagedServerRuntime.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\tools\packaging\Test-InstallResidue.ps1') -Destination (Join-Path $Destination 'Test-InstallResidue.ps1') -Force
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
$sandboxCommandArguments = @(
    (New-SandboxCommandValue (Join-Path $sandboxPreflightPath 'SandboxCi.ArtifactPreflight.ps1')),
    (New-SandboxCommandSwitch '-PackageArchivePath'),
    (New-SandboxCommandValue $sandboxPackageArchive),
    (New-SandboxCommandSwitch '-OutputRoot'),
    (New-SandboxCommandValue $sandboxOutputMappedPath),
    (New-SandboxCommandSwitch '-RunId'),
    (New-SandboxCommandValue $runId),
    (New-SandboxCommandSwitch '-Architecture'),
    (New-SandboxCommandValue $Architecture),
    (New-SandboxCommandSwitch '-Client'),
    (New-SandboxCommandValue $Client),
    (New-SandboxCommandSwitch '-DotNetChannel'),
    (New-SandboxCommandValue $DotNetChannel),
    (New-SandboxCommandSwitch '-DotNetInstallScriptUrl'),
    (New-SandboxCommandValue $DotNetInstallScriptUrl)
)
$smokeTargetMapping = ''
if (-not [string]::IsNullOrWhiteSpace($SmokeTargetPath)) {
    $resolvedSmokeTarget = (Resolve-Path -LiteralPath $SmokeTargetPath).Path
    $smokeTargetDirectory = [System.IO.Path]::GetDirectoryName($resolvedSmokeTarget)
    $smokeTargetFileName = [System.IO.Path]::GetFileName($resolvedSmokeTarget)
    $sandboxSmokeRoot = 'C:\smoke-target'
    $sandboxSmokeTarget = Join-Path $sandboxSmokeRoot $smokeTargetFileName
    $smokeTargetMapping = New-MappedFolderXml -HostFolder $smokeTargetDirectory -SandboxFolder $sandboxSmokeRoot -ReadOnly $true
    $sandboxCommandArguments += @(
        (New-SandboxCommandSwitch '-SmokeTargetPath'),
        (New-SandboxCommandValue $sandboxSmokeTarget)
    )
}
if ($SkipDotNetProvisioning) {
    $sandboxCommandArguments += New-SandboxCommandSwitch '-SkipDotNetProvisioning'
}

$trustedSignerMapping = ''
$sandboxTrustedSignerCertificate = ''
if (-not [string]::IsNullOrWhiteSpace($TrustedCodeSigningCertificatePath)) {
    $resolvedTrustedSignerCertificate = (Resolve-Path -LiteralPath $TrustedCodeSigningCertificatePath).Path
    $trustedSignerExtension = [System.IO.Path]::GetExtension($resolvedTrustedSignerCertificate)
    if ($trustedSignerExtension -notin @('.cer', '.crt')) {
        throw 'TrustedCodeSigningCertificatePath must point to a .cer or .crt public certificate file.'
    }

    $trustedSignerDirectory = [System.IO.Path]::GetDirectoryName($resolvedTrustedSignerCertificate)
    $trustedSignerFileName = [System.IO.Path]::GetFileName($resolvedTrustedSignerCertificate)
    $sandboxTrustedSignerRoot = 'C:\trusted-signer'
    $sandboxTrustedSignerCertificate = Join-Path $sandboxTrustedSignerRoot $trustedSignerFileName
    $trustedSignerMapping = New-MappedFolderXml -HostFolder $trustedSignerDirectory -SandboxFolder $sandboxTrustedSignerRoot -ReadOnly $true
    $sandboxCommandArguments += @(
        (New-SandboxCommandSwitch '-TrustedCodeSigningCertificatePath'),
        (New-SandboxCommandValue $sandboxTrustedSignerCertificate)
    )
}

$sandboxScriptCommand = '& ' + (($sandboxCommandArguments | ForEach-Object {
            if ($_.IsValue) { Convert-ToPowerShellSingleQuotedLiteral -Value ([string]$_.Text) } else { [string]$_.Text }
        }) -join ' ')
$command = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand ' + (Convert-ToEncodedPowerShellCommand -Command $sandboxScriptCommand)

$config = @"
<Configuration>
  <VGpu>Enable</VGpu>
  <Networking>Enable</Networking>
  <MappedFolders>
$(New-MappedFolderXml -HostFolder $packageDirectory -SandboxFolder $sandboxReleasePath -ReadOnly $true)
$(New-MappedFolderXml -HostFolder $bootstrapRoot -SandboxFolder $sandboxPreflightPath -ReadOnly $true)
$(New-MappedFolderXml -HostFolder $sandboxOutputPath -SandboxFolder $sandboxOutputMappedPath -ReadOnly $false)
$smokeTargetMapping
$trustedSignerMapping
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
Write-Host "Wait timeout seconds: $WaitTimeoutSeconds"
Write-Host "Guest startup timeout seconds: $GuestStartupTimeoutSeconds"
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
            "Windows Sandbox artifact preflight guest did not write RUNNING/PASS/FAIL within $GuestStartupTimeoutSeconds seconds. " +
            "RunId: $runId. Inspect the generated .wsb file: $configPath. " +
            "Inspect cleanup candidates with: $cleanupCommand. " +
            "After verifying the candidates are Windows Sandbox compute systems, rerun cleanup with -Force or -Confirm:`$false."
        )
    }

    Start-Sleep -Seconds 5
}

$cleanupCommand = ".\scripts\ci\Stop-WindowsSandboxHcs.ps1 -OutputRoot `"$sandboxOutputPath`" -WhatIf"
throw ("Timed out waiting for Windows Sandbox artifact preflight result after $WaitTimeoutSeconds seconds. RunId: $runId. Inspect cleanup candidates with: $cleanupCommand. After verifying the candidates are Windows Sandbox compute systems, rerun cleanup with " + '-Force or -Confirm:$false.')
