[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SolutionPath,

    [Parameter(Mandatory = $true)]
    [string[]] $Include,

    [string] $DotNetPath = '',

    [int] $MaxAttempts = 3,

    [int] $RetryDelaySeconds = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$transientCliResolutionMessage = 'Unable to locate dotnet CLI. Ensure that it is on the PATH.'

function Resolve-DotNetExecutable {
    param([string] $ExplicitPath)

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

function Invoke-DotNetAndCaptureOutput {
    param(
        [string] $ResolvedDotNetPath,
        [string[]] $Arguments
    )

    $global:LASTEXITCODE = 0
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $ResolvedDotNetPath @Arguments 2>&1)
        $exitCode = if ($global:LASTEXITCODE -is [int]) { $global:LASTEXITCODE } else { 0 }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
        Text = ($output | Out-String)
    }
}

if ($MaxAttempts -lt 1) {
    throw 'MaxAttempts must be at least 1.'
}

if ($RetryDelaySeconds -lt 0) {
    throw 'RetryDelaySeconds must not be negative.'
}

$dotnet = Resolve-DotNetExecutable -ExplicitPath $DotNetPath
$dotnetDirectory = Split-Path -Parent $dotnet
$env:DOTNET_ROOT = $dotnetDirectory
$env:PATH = "$dotnetDirectory;$env:PATH"
$env:DOTNET_HOST_PATH = $dotnet

$arguments = @(
    'format'
    $SolutionPath
    '--verify-no-changes'
    '--no-restore'
    '--include'
) + $Include

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    $formatResult = Invoke-DotNetAndCaptureOutput -ResolvedDotNetPath $dotnet -Arguments $arguments
    $formatResult.Output | Write-Output

    if ($formatResult.ExitCode -eq 0) {
        exit 0
    }

    $isTransientCliResolutionFailure = $formatResult.Text.IndexOf(
        $transientCliResolutionMessage,
        [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    if (-not $isTransientCliResolutionFailure) {
        exit $formatResult.ExitCode
    }

    if ($attempt -ge $MaxAttempts) {
        Write-Error "dotnet format verification failed after $MaxAttempts attempts due to transient dotnet CLI resolution failure." -ErrorAction Continue
        exit $formatResult.ExitCode
    }

    Write-Host 'Retrying dotnet format verification after transient dotnet CLI resolution failure.'
    if ($RetryDelaySeconds -gt 0) {
        Start-Sleep -Seconds $RetryDelaySeconds
    }
}
