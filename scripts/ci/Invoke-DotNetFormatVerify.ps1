param(
    [Parameter(Mandatory = $true)]
    [string] $SolutionPath,

    [Parameter(Mandatory = $true)]
    [string[]] $Include
)

$ErrorActionPreference = 'Stop'

$dotnet = (Get-Command dotnet).Source
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
