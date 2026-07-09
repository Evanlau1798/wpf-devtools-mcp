param(
    [Parameter(Mandatory = $true)]
    [string] $SolutionPath,

    [Parameter(Mandatory = $true)]
    [string[]] $Include
)

$ErrorActionPreference = 'Stop'

function Resolve-DotNetExecutable {
    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
        $rootCandidate = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
        if (Test-Path -LiteralPath $rootCandidate) {
            return (Resolve-Path -LiteralPath $rootCandidate).ProviderPath
        }
    }

    return (Get-Command dotnet -ErrorAction Stop).Source
}

$dotnet = Resolve-DotNetExecutable
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

& $dotnet @arguments
exit $LASTEXITCODE
