[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string[]]$Scenarios = @('net8-net8', 'net8-net48', 'net48-net8'),
    [string]$OutputRoot,
    [switch]$SkipBuild,
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    Write-Host "> $FilePath $($Arguments -join ' ')"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function ConvertTo-WindowsCommandLineArgument {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string]$Argument)

    if ($Argument.Length -eq 0) {
        return '""'
    }

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    $builder = New-Object System.Text.StringBuilder
    $backslash = [char]0x5c
    $quote = [char]0x22
    $backslashCount = 0

    [void]$builder.Append($quote)
    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq $backslash) {
            $backslashCount++
            continue
        }

        if ($character -eq $quote) {
            [void]$builder.Append(($backslash.ToString() * (($backslashCount * 2) + 1)))
            [void]$builder.Append($quote)
            $backslashCount = 0
            continue
        }

        if ($backslashCount -gt 0) {
            [void]$builder.Append(($backslash.ToString() * $backslashCount))
            $backslashCount = 0
        }

        [void]$builder.Append($character)
    }

    if ($backslashCount -gt 0) {
        [void]$builder.Append(($backslash.ToString() * ($backslashCount * 2)))
    }

    [void]$builder.Append($quote)
    return $builder.ToString()
}

function ConvertTo-WindowsCommandLine {
    param([Parameter(Mandatory)] [string[]]$Arguments)

    return (($Arguments | ForEach-Object { ConvertTo-WindowsCommandLineArgument -Argument $_ }) -join ' ')
}

function Start-HarnessProcess {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$StandardOutputPath,
        [Parameter(Mandatory)] [string]$StandardErrorPath
    )

    $argumentLine = ConvertTo-WindowsCommandLine -Arguments $Arguments
    return Start-Process `
        -FilePath $FilePath `
        -ArgumentList $argumentLine `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $StandardOutputPath `
        -RedirectStandardError $StandardErrorPath
}

function Convert-ScenarioRuntime {
    param([Parameter(Mandatory)] [string]$Value)

    switch ($Value) {
        'net8' { return 'net8' }
        'net48' { return 'net48' }
        default { throw "Unsupported TLS scenario runtime '$Value'. Use net8 or net48." }
    }
}

function Get-HarnessExecutablePath {
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)] [string]$Runtime,
        [Parameter(Mandatory)] [string]$BuildConfiguration
    )

    $targetFramework = switch ($Runtime) {
        'net8' { 'net8.0' }
        'net48' { 'net48' }
        default { throw "Unsupported harness runtime '$Runtime'." }
    }

    return Join-Path $RepositoryRoot "tests\WpfDevTools.Tests.TlsNegotiationHarness\bin\$BuildConfiguration\$targetFramework\WpfDevTools.Tests.TlsNegotiationHarness.exe"
}

function Wait-ForFile {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [int]$TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            return
        }

        Start-Sleep -Milliseconds 100
    }

    throw "Timed out waiting for file: $Path"
}

function Wait-ForProcessExit {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)] [int]$TimeoutSeconds,
        [Parameter(Mandatory)] [string]$Description
    )

    if (-not $Process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        }
        catch {
        }

        throw "Timed out waiting for $Description process $($Process.Id)."
    }
}

function Get-ProcessExitCode {
    param([Parameter(Mandatory)] [System.Diagnostics.Process]$Process)

    try {
        $Process.Refresh()
        return $Process.ExitCode
    }
    catch {
        return $null
    }
}

function Read-KeyValueFile {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Expected result file was not created: $Path"
    }

    $result = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $parts = $line.Split([char]'=', 2)
        if ($parts.Count -eq 2) {
            $result[$parts[0]] = $parts[1]
        }
    }

    return $result
}

function Assert-TlsResult {
    param(
        [Parameter(Mandatory)] [hashtable]$Result,
        [Parameter(Mandatory)] [string]$ExpectedRole,
        [Parameter(Mandatory)] [string]$ExpectedFramework,
        [Parameter(Mandatory)] [string]$Scenario
    )

    $expectedFrameworkName = switch ($ExpectedFramework) {
        'net8' { 'net8.0' }
        'net48' { 'net48' }
        default { throw "Unsupported expected framework '$ExpectedFramework'." }
    }

    if ($Result['role'] -ne $ExpectedRole) {
        throw "Scenario '$Scenario' expected $ExpectedRole result role but got '$($Result['role'])'."
    }

    if ($Result['framework'] -ne $expectedFrameworkName) {
        throw "Scenario '$Scenario' expected $ExpectedRole framework '$expectedFrameworkName' but got '$($Result['framework'])'."
    }

    if ($Result['policy'] -ne 'Tls12') {
        throw "Scenario '$Scenario' expected shared transport policy Tls12 but got '$($Result['policy'])'."
    }

    if ($Result['protocol'] -ne 'Tls12') {
        throw "Scenario '$Scenario' expected negotiated protocol Tls12 but got '$($Result['protocol'])'."
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'tmp\tls-negotiation'
}

$pathSeparators = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$tmpRoot = ((Resolve-Path (New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot 'tmp'))).Path).TrimEnd($pathSeparators)
$outputRootFullPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot).TrimEnd($pathSeparators)
$tmpRootWithSeparator = $tmpRoot + [System.IO.Path]::DirectorySeparatorChar
if (-not ($outputRootFullPath.Equals($tmpRoot, [StringComparison]::OrdinalIgnoreCase) -or $outputRootFullPath.StartsWith($tmpRootWithSeparator, [StringComparison]::OrdinalIgnoreCase))) {
    throw "OutputRoot must be under '$tmpRoot' to keep disposable TLS verification artifacts out of commits."
}

New-Item -ItemType Directory -Force -Path $outputRootFullPath | Out-Null

$projectPath = Join-Path $repoRoot 'tests\WpfDevTools.Tests.TlsNegotiationHarness\WpfDevTools.Tests.TlsNegotiationHarness.csproj'
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "TLS negotiation harness project was not found: $projectPath"
}

if (-not $SkipBuild) {
    Invoke-Checked -FilePath 'dotnet' -Arguments @('restore', $projectPath, '--locked-mode')
    Invoke-Checked -FilePath 'dotnet' -Arguments @('build', $projectPath, '-c', $Configuration, '-f', 'net8.0', '--no-restore', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false')
    Invoke-Checked -FilePath 'dotnet' -Arguments @('build', $projectPath, '-c', $Configuration, '-f', 'net48', '--no-restore', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false')
}

$runtimeExecutables = @{
    net8 = Get-HarnessExecutablePath -RepositoryRoot $repoRoot -Runtime 'net8' -BuildConfiguration $Configuration
    net48 = Get-HarnessExecutablePath -RepositoryRoot $repoRoot -Runtime 'net48' -BuildConfiguration $Configuration
}

foreach ($runtime in $runtimeExecutables.Keys) {
    if (-not (Test-Path -LiteralPath $runtimeExecutables[$runtime] -PathType Leaf)) {
        throw "TLS negotiation harness executable for $runtime was not found: $($runtimeExecutables[$runtime])"
    }
}

$results = New-Object System.Collections.Generic.List[object]
foreach ($scenario in $Scenarios) {
    $parts = $scenario.Split([char]'-', 2)
    if ($parts.Count -ne 2) {
        throw "Invalid TLS scenario '$scenario'. Expected format '<serverRuntime>-<clientRuntime>'."
    }

    $serverRuntime = Convert-ScenarioRuntime -Value $parts[0]
    $clientRuntime = Convert-ScenarioRuntime -Value $parts[1]
    $scenarioRoot = Join-Path $outputRootFullPath $scenario
    if (Test-Path -LiteralPath $scenarioRoot) {
        Remove-Item -LiteralPath $scenarioRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $scenarioRoot | Out-Null
    $pipeName = "wpfdevtools_tls_$($scenario)_$([guid]::NewGuid().ToString('N'))"
    $certDir = Join-Path $scenarioRoot 'certs'
    $readyFile = Join-Path $scenarioRoot 'server.ready'
    $serverResult = Join-Path $scenarioRoot 'server.result'
    $clientResult = Join-Path $scenarioRoot 'client.result'
    $serverStdout = Join-Path $scenarioRoot 'server.stdout.log'
    $serverStderr = Join-Path $scenarioRoot 'server.stderr.log'
    $clientStdout = Join-Path $scenarioRoot 'client.stdout.log'
    $clientStderr = Join-Path $scenarioRoot 'client.stderr.log'

    Write-Host "Running TLS negotiation scenario: $scenario"
    $serverProcess = Start-HarnessProcess `
        -FilePath $runtimeExecutables[$serverRuntime] `
        -Arguments @('server', '--pipe', $pipeName, '--cert-dir', $certDir, '--ready-file', $readyFile, '--result-file', $serverResult, '--connect-timeout-seconds', $TimeoutSeconds.ToString()) `
        -StandardOutputPath $serverStdout `
        -StandardErrorPath $serverStderr

    try {
        Wait-ForFile -Path $readyFile -TimeoutSeconds $TimeoutSeconds
        $clientProcess = Start-HarnessProcess `
            -FilePath $runtimeExecutables[$clientRuntime] `
            -Arguments @('client', '--pipe', $pipeName, '--cert-dir', $certDir, '--ready-file', (Join-Path $scenarioRoot 'client.ready'), '--result-file', $clientResult, '--connect-timeout-seconds', $TimeoutSeconds.ToString()) `
            -StandardOutputPath $clientStdout `
            -StandardErrorPath $clientStderr

        Wait-ForProcessExit -Process $clientProcess -TimeoutSeconds $TimeoutSeconds -Description "$scenario client"
        Wait-ForProcessExit -Process $serverProcess -TimeoutSeconds $TimeoutSeconds -Description "$scenario server"

        $clientExitCode = Get-ProcessExitCode -Process $clientProcess
        $serverExitCode = Get-ProcessExitCode -Process $serverProcess
        if ($null -ne $clientExitCode -and $clientExitCode -ne 0) {
            throw "Scenario '$scenario' client failed with exit code $clientExitCode. See $clientStderr"
        }

        if ($null -ne $serverExitCode -and $serverExitCode -ne 0) {
            throw "Scenario '$scenario' server failed with exit code $serverExitCode. See $serverStderr"
        }

        $server = Read-KeyValueFile -Path $serverResult
        $client = Read-KeyValueFile -Path $clientResult
        Assert-TlsResult -Result $server -ExpectedRole 'server' -ExpectedFramework $serverRuntime -Scenario $scenario
        Assert-TlsResult -Result $client -ExpectedRole 'client' -ExpectedFramework $clientRuntime -Scenario $scenario

        $results.Add([pscustomobject]@{
            Scenario = $scenario
            ServerFramework = $server['framework']
            ClientFramework = $client['framework']
            ServerProtocol = $server['protocol']
            ClientProtocol = $client['protocol']
        })
    }
    finally {
        if ($serverProcess -and -not $serverProcess.HasExited) {
            Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

$results | Format-Table -AutoSize
Write-Host "TLS negotiation verification passed for $($results.Count) scenario(s)."
