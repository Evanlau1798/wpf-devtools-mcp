[CmdletBinding()]
param(
    [string]$SolutionPath = 'WpfDevTools.sln',
    [string]$DotNetPath = '',
    [int]$MaxAttempts = 3,
    [int]$RetryDelaySeconds = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$transientCliResolutionMessage = 'Unable to locate dotnet CLI. Ensure that it is on the PATH.'

function Resolve-DotNetExecutable {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath)) {
            throw "DotNetPath does not exist: $ExplicitPath"
        }

        return (Resolve-Path -LiteralPath $ExplicitPath).ProviderPath
    }

    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
        $rootCandidate = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
        if (Test-Path -LiteralPath $rootCandidate) {
            return (Resolve-Path -LiteralPath $rootCandidate).ProviderPath
        }
    }

    return (Get-Command dotnet -ErrorAction Stop).Source
}

function Set-DotNetCliEnvironment {
    param([string]$ResolvedDotNetPath)

    $dotnetDirectory = Split-Path -Parent $ResolvedDotNetPath
    $env:DOTNET_ROOT = $dotnetDirectory
    $env:PATH = "$dotnetDirectory;$env:PATH"
    $env:DOTNET_HOST_PATH = $ResolvedDotNetPath
}

function Invoke-DotNetAndCaptureOutput {
    param(
        [string]$ResolvedDotNetPath,
        [string[]]$Arguments
    )

    $global:LASTEXITCODE = 0
    $output = @(& $ResolvedDotNetPath @Arguments 2>&1)
    $exitCode = if ($global:LASTEXITCODE -is [int]) { $global:LASTEXITCODE } else { 0 }

    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
        Text = ($output | Out-String)
    }
}

function Write-CapturedOutput {
    param([object[]]$Output)

    foreach ($line in $Output) {
        Write-Output $line
    }
}

if ($MaxAttempts -lt 1) {
    throw 'MaxAttempts must be at least 1.'
}

if ($RetryDelaySeconds -lt 0) {
    throw 'RetryDelaySeconds must not be negative.'
}

$dotnet = Resolve-DotNetExecutable -ExplicitPath $DotNetPath
Set-DotNetCliEnvironment -ResolvedDotNetPath $dotnet

$infoResult = Invoke-DotNetAndCaptureOutput -ResolvedDotNetPath $dotnet -Arguments @('--info')
Write-CapturedOutput -Output $infoResult.Output
if ($infoResult.ExitCode -ne 0) {
    throw "dotnet --info failed with exit code $($infoResult.ExitCode)."
}

$formatArguments = @(
    'format',
    $SolutionPath,
    'analyzers',
    '--verify-no-changes',
    '--severity',
    'error',
    '--no-restore'
)

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    Write-Host "Running .NET analyzer gate (attempt $attempt of $MaxAttempts)."
    $formatResult = Invoke-DotNetAndCaptureOutput -ResolvedDotNetPath $dotnet -Arguments $formatArguments
    Write-CapturedOutput -Output $formatResult.Output

    if ($formatResult.ExitCode -eq 0) {
        exit 0
    }

    $isTransientCliResolutionFailure = $formatResult.Text.IndexOf(
        $transientCliResolutionMessage,
        [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    if (-not $isTransientCliResolutionFailure) {
        throw "dotnet format analyzer gate failed with exit code $($formatResult.ExitCode)."
    }

    if ($attempt -ge $MaxAttempts) {
        throw "dotnet format analyzer gate failed after $MaxAttempts attempts due to transient dotnet CLI resolution failure."
    }

    Write-Host "Retrying dotnet format analyzer gate after transient dotnet CLI resolution failure."
    if ($RetryDelaySeconds -gt 0) {
        Start-Sleep -Seconds $RetryDelaySeconds
    }
}
