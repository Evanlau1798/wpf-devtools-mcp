param(
    [Parameter(Mandatory = $true)] [string]$PackageArchivePath,
    [Parameter(Mandatory = $true)] [string]$OutputRoot,
    [ValidatePattern('^[A-Za-z0-9_.-]+$')]
    [string]$RunId = (Get-Date -Format 'yyyyMMdd-HHmmss'),
    [ValidateSet('x64', 'x86')]
    [string]$Architecture = 'x64',
    [ValidateSet('claude-code', 'codex', 'cursor', 'vscode', 'visual-studio', 'claude-desktop', 'other')]
    [string]$Client = 'other',
    [string]$SmokeTargetPath = '',
    [ValidateRange(1, 300)]
    [int]$SmokeTargetStartupTimeoutSeconds = 30,
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
    [switch]$SkipDotNetProvisioning
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SandboxCi.ProcessCleanup.ps1')

function Write-PreflightResult {
    param(
        [Parameter(Mandatory = $true)] [string]$Value,
        [Parameter(Mandatory = $true)] [System.Text.Encoding]$Encoding
    )

    $resultPath = Join-Path $OutputRoot 'last-result.txt'
    $tempPath = Join-Path $OutputRoot ("last-result.{0}.tmp" -f $RunId)
    [System.IO.File]::WriteAllText($tempPath, $Value, $Encoding)
    Move-Item -LiteralPath $tempPath -Destination $resultPath -Force
}

function Write-PreflightSummary {
    param(
        [Parameter(Mandatory = $true)] [string]$Status,
        [Parameter(Mandatory = $true)] [string]$Message,
        [Parameter(Mandatory = $true)] [string]$PackagePath,
        [Parameter(Mandatory = $true)] [string]$InstallRoot
    )

    $summary = [ordered]@{
        mode = 'ArtifactPreflight'
        status = $Status
        runId = $RunId
        architecture = $Architecture
        client = $Client
        packageArchivePath = $PackagePath
        installRoot = $InstallRoot
        smokeTargetPath = if ([string]::IsNullOrWhiteSpace($SmokeTargetPath)) { $null } else { $SmokeTargetPath }
        message = $Message
        completedAt = (Get-Date).ToString('o')
    }

    $summaryPath = Join-Path $OutputRoot 'preflight-summary.json'
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
}

function Assert-RequiredPath {
    param(
        [Parameter(Mandatory = $true)] [string]$Path,
        [Parameter(Mandatory = $true)] [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Package layout verification failed. Missing ${Description}: $Path"
    }
}

function Resolve-RuntimeSmokeScript {
    $sandboxBootstrapPath = Join-Path $PSScriptRoot 'Test-PackagedServerRuntime.ps1'
    if (Test-Path -LiteralPath $sandboxBootstrapPath) {
        return (Resolve-Path -LiteralPath $sandboxBootstrapPath).Path
    }

    $repoFallbackPath = Join-Path $PSScriptRoot '..\tools\packaging\Test-PackagedServerRuntime.ps1'
    if (Test-Path -LiteralPath $repoFallbackPath) {
        return (Resolve-Path -LiteralPath $repoFallbackPath).Path
    }

    throw "Runtime smoke script was not found at '$sandboxBootstrapPath' or '$repoFallbackPath'."
}

function Get-PreflightLogTail {
    param([Parameter(Mandatory = $true)] [string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return ''
    }

    $lines = @(Get-Content -LiteralPath $Path -Tail 40 -ErrorAction SilentlyContinue)
    return (($lines -join [Environment]::NewLine).Trim())
}

function Get-DotNetExecutablePath {
    $localDotNet = Join-Path (Get-DotNetRoot) 'dotnet.exe'
    if (Test-Path -LiteralPath $localDotNet) {
        return $localDotNet
    }

    if ($Architecture -eq 'x64') {
        $command = Get-Command 'dotnet.exe' -ErrorAction SilentlyContinue
        if ($null -ne $command) {
            return [string]$command.Source
        }
    }

    return ''
}

function Get-DotNetRuntimeArchitecture {
    if ($Architecture -eq 'x86') {
        return 'x86'
    }

    return 'x64'
}

function Get-DotNetRoot {
    return (Join-Path $localRoot ("dotnet-{0}" -f (Get-DotNetRuntimeArchitecture)))
}

function Test-DotNetRuntimeAvailable {
    $dotnetPath = Get-DotNetExecutablePath
    if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
        return $false
    }

    $majorVersion = ($DotNetChannel -split '\.')[0]
    $runtimePattern = '^Microsoft\.NETCore\.App\s+' + [Regex]::Escape($majorVersion) + '\.'
    $runtimes = @(& $dotnetPath --list-runtimes 2>$null)
    return (@($runtimes | Where-Object { $_ -match $runtimePattern }).Count -gt 0)
}

function Use-DotNetRoot {
    param([Parameter(Mandatory = $true)] [string]$DotNetRoot)

    if ((Get-DotNetRuntimeArchitecture) -eq 'x86') {
        [Environment]::SetEnvironmentVariable('DOTNET_ROOT(x86)', $DotNetRoot, 'Process')
    }
    else {
        $env:DOTNET_ROOT = $DotNetRoot
    }

    $env:PATH = "$DotNetRoot;$env:PATH"
}

function Ensure-DotNetRuntime {
    if ($SkipDotNetProvisioning) {
        Write-Host '>>> .NET runtime provisioning skipped'
        return
    }

    if (Test-DotNetRuntimeAvailable) {
        Write-Host ">>> .NET runtime channel $DotNetChannel already available"
        return
    }

    $dotNetRuntimeArchitecture = Get-DotNetRuntimeArchitecture
    $dotNetRoot = Get-DotNetRoot
    $dotNetInstallScript = Join-Path $localRoot 'dotnet-install.ps1'
    New-Item -ItemType Directory -Force -Path $dotNetRoot | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    Write-Host ">>> Download dotnet-install.ps1"
    Invoke-WebRequest -Uri $DotNetInstallScriptUrl -OutFile $dotNetInstallScript -UseBasicParsing
    Invoke-PowerShellStep -Name 'Install .NET runtime' -Arguments @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $dotNetInstallScript,
        '-Channel',
        $DotNetChannel,
        '-Runtime',
        'dotnet',
        '-Architecture',
        $dotNetRuntimeArchitecture,
        '-InstallDir',
        $dotNetRoot,
        '-NoPath'
    )

    Use-DotNetRoot -DotNetRoot $dotNetRoot
    if (-not (Test-DotNetRuntimeAvailable)) {
        throw "Failed to provision .NET runtime channel $DotNetChannel into $dotNetRoot."
    }
}

function Invoke-PowerShellStep {
    param(
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [string[]]$Arguments
    )

    Write-Host ">>> $Name"
    $safeName = ($Name -replace '[^A-Za-z0-9_.-]+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safeName)) {
        $safeName = 'step'
    }

    $stdoutPath = Join-Path $logRoot "$timestamp-$safeName.stdout.log"
    $stderrPath = Join-Path $logRoot "$timestamp-$safeName.stderr.log"
    $stepErrorActionPreference = $ErrorActionPreference
    $exitCode = 0
    try {
        $ErrorActionPreference = 'Continue'
        & powershell.exe @Arguments 1> $stdoutPath 2> $stderrPath
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $stepErrorActionPreference
    }

    if ($exitCode -ne 0) {
        $stdoutTail = Get-PreflightLogTail -Path $stdoutPath
        $stderrTail = Get-PreflightLogTail -Path $stderrPath
        throw "$Name failed with exit code $exitCode. Stdout log: $stdoutPath. Stderr log: $stderrPath. Stdout tail: $stdoutTail. Stderr tail: $stderrTail"
    }
}

function Enable-InstallerTestHarness {
    $script:WpfDevToolsInstallerTestModeHarnessEnabled = $true
    $script:WpfDevToolsInstallerTestModeEnabled = $true
    $env:WPFDEVTOOLS_INSTALLER_TEST_MODE = '1'
    $env:WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA = '1'
    $env:WPFDEVTOOLS_TEST_SIGNATURE_STATUS = 'Valid'
    $env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED = '0'
}

function Invoke-InstallerStep {
    param(
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [string]$ScriptPath,
        [Parameter(Mandatory = $true)] [hashtable]$Parameters
    )

    Write-Host ">>> $Name"
    Enable-InstallerTestHarness
    Set-StrictMode -Off
    try {
        . $ScriptPath @Parameters
    }
    finally {
        Set-StrictMode -Version Latest
    }
}

function Assert-NoPreflightProcessesRemain {
    param([Parameter(Mandatory = $true)] [string]$RootPath)
    $root = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\') + '\'
    $matches = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try {
            -not [string]::IsNullOrWhiteSpace($_.Path) -and
                [System.IO.Path]::GetFullPath($_.Path).StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    })
    if ($matches.Count -gt 0) {
        throw "Preflight left process(es) running from ${RootPath}: $($matches.ProcessName -join ', ')"
    }
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$ascii = [System.Text.Encoding]::ASCII
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputRootFullPath = (Resolve-Path (New-Item -ItemType Directory -Force -Path $OutputRoot)).Path
$logRoot = Join-Path $outputRootFullPath 'logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$transcriptPath = Join-Path $logRoot "artifact-preflight-$timestamp.log"
$localRoot = Join-Path $env:SystemDrive "sandbox-artifact-preflight-$RunId"
$extractRoot = Join-Path $localRoot 'package'
$installRoot = Join-Path $localRoot 'install'
$smokeProcess = $null
$resolvedSmokeTargetPath = ''
$preflightFailureMessage = ''

Start-Transcript -Path $transcriptPath -Force | Out-Null
try {
    Enable-InstallerTestHarness

    $resolvedPackagePath = (Resolve-Path -LiteralPath $PackageArchivePath).Path
    Remove-Item -LiteralPath $localRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $extractRoot, $installRoot | Out-Null

    Write-Host '>>> Environment probe'
    $PSVersionTable | Format-List
    [Environment]::OSVersion | Format-List
    whoami
    Get-ExecutionPolicy -List | Format-Table -AutoSize

    Write-Host '>>> Expand release package'
    Expand-Archive -LiteralPath $resolvedPackagePath -DestinationPath $extractRoot -Force

    $packageRoot = $extractRoot
    $installScript = Join-Path $packageRoot 'bin\install.ps1'
    $manifestPath = Join-Path $packageRoot 'bin\manifest.json'
    Assert-RequiredPath -Path (Join-Path $packageRoot 'run.bat') -Description 'run.bat'
    Assert-RequiredPath -Path $installScript -Description 'package-local installer'
    Assert-RequiredPath -Path $manifestPath -Description 'manifest'
    Assert-RequiredPath -Path (Join-Path $packageRoot 'bin\inspectors\net8.0-windows\WpfDevTools.Inspector.dll') -Description 'net8 inspector'
    Assert-RequiredPath -Path (Join-Path $packageRoot 'bin\inspectors\net48\WpfDevTools.Inspector.dll') -Description 'net48 inspector'
    Assert-RequiredPath -Path (Join-Path $packageRoot "bin\bootstrapper\$Architecture\WpfDevTools.Bootstrapper.$Architecture.dll") -Description 'native bootstrapper'
    Assert-RequiredPath -Path (Join-Path $packageRoot "bin\wpf-devtools-$Architecture.exe") -Description 'packaged server executable'

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.architecture -ne $Architecture) {
        throw "Manifest architecture '$($manifest.architecture)' did not match requested architecture '$Architecture'."
    }

    Invoke-InstallerStep -Name 'Install package-local release' -ScriptPath $installScript -Parameters @{
        InstallRoot = $installRoot
        Architecture = $Architecture
        Client = $Client
        NonInteractive = $true
        Force = $true
        OutputJson = $true
    }

    $serverPath = Join-Path $installRoot "$Architecture\current\bin\wpf-devtools-$Architecture.exe"
    Assert-RequiredPath -Path $serverPath -Description 'installed server executable'

    Ensure-DotNetRuntime
    $smokeProcess = Start-SmokeTarget
    $runtimeSmoke = Resolve-RuntimeSmokeScript
    $runtimeSmokeArguments = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $runtimeSmoke,
        '-ServerPath',
        $serverPath
    )
    if ($null -ne $smokeProcess) {
        $runtimeSmokeArguments += @(
            '-TargetProcessId',
            ([string]$smokeProcess.Id),
            '-TargetProcessPath',
            $resolvedSmokeTargetPath
        )
    }

    Invoke-PowerShellStep -Name 'Run packaged server runtime smoke' -Arguments $runtimeSmokeArguments

    $installedScript = Join-Path $installRoot "$Architecture\current\bin\install.ps1"
    Assert-RequiredPath -Path $installedScript -Description 'installed package-local installer'
    Invoke-InstallerStep -Name 'Uninstall package-local release' -ScriptPath $installedScript -Parameters @{
        Action = 'uninstall'
        InstallRoot = $installRoot
        Architecture = $Architecture
        Client = $Client
        NonInteractive = $true
        OutputJson = $true
    }

    try { Stop-SmokeTarget -Process $smokeProcess } finally { $smokeProcess = $null }
    Assert-NoPreflightProcessesRemain -RootPath $localRoot
    Write-PreflightSummary -Status 'PASS' -Message 'Artifact preflight completed successfully.' -PackagePath $resolvedPackagePath -InstallRoot $installRoot
    Write-PreflightResult -Value "PASS $RunId $timestamp ArtifactPreflight" -Encoding $ascii
    Write-Host "Artifact preflight completed successfully. Results: $outputRootFullPath"
}
catch {
    $message = $_.Exception.Message
    $preflightFailureMessage = $message
    Write-PreflightSummary -Status 'FAIL' -Message $message -PackagePath $PackageArchivePath -InstallRoot $installRoot
    Write-PreflightResult -Value "FAIL $RunId $timestamp ArtifactPreflight $message" -Encoding $utf8NoBom
    throw
}
finally {
    $cleanupFailureMessage = ''
    try {
        if ($null -ne $smokeProcess) {
            try { Stop-SmokeTarget -Process $smokeProcess } finally { $smokeProcess = $null }
        }
    }
    catch { $cleanupFailureMessage = $_.Exception.Message }
    try { Stop-Transcript | Out-Null } catch {}
    if (-not [string]::IsNullOrWhiteSpace($cleanupFailureMessage)) {
        if (-not [string]::IsNullOrWhiteSpace($preflightFailureMessage)) {
            $combinedMessage = "Preflight cleanup failed after primary failure. Primary failure: $preflightFailureMessage Cleanup failure: $cleanupFailureMessage"
            Write-PreflightSummary -Status 'FAIL' -Message $combinedMessage -PackagePath $PackageArchivePath -InstallRoot $installRoot
            Write-PreflightResult -Value "FAIL $RunId $timestamp ArtifactPreflight $combinedMessage" -Encoding $utf8NoBom
            throw $combinedMessage
        }
        throw $cleanupFailureMessage
    }
}
