# Sign Binaries with Code Signing Certificate
# This script signs WpfDevTools DLL and EXE outputs from the selected build configuration.

param(
    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,

    [Parameter(Mandatory = $true)]
    [string]$Password,

    [string]$BuildConfiguration = "Release",

    [string]$TimestampServer = "http://timestamp.digicert.com"
)

function Resolve-SignToolPath {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_SIGNTOOL_PATH)) {
        return $env:WPFDEVTOOLS_SIGNTOOL_PATH
    }

    $command = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path $kitsRoot)) {
        return $null
    }

    $candidate = Get-ChildItem -Path $kitsRoot -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*\\x64\\signtool.exe" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    return $candidate.FullName
}

function Get-BinaryRoots {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_SIGN_BINARIES_ROOTS)) {
        return @($env:WPFDEVTOOLS_SIGN_BINARIES_ROOTS.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries))
    }

    return @(".\\src", ".\\tests")
}

function Get-BinariesToSign {
    param([Parameter(Mandatory = $true)] [string[]]$Roots)

    $binaries = @()
    foreach ($root in $Roots) {
        if (-not (Test-Path $root)) {
            continue
        }

        $binaries += Get-ChildItem -Path $root -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object {
                ($_.Extension -in @('.dll', '.exe')) -and
                $_.FullName -like "*\bin\$BuildConfiguration\*" -and
                $_.Name -like "WpfDevTools.*"
            }
    }

    return @($binaries | Sort-Object FullName -Unique)
}

Write-Host "Signing binaries with code signing certificate..." -ForegroundColor Green

$signtool = Resolve-SignToolPath
if ([string]::IsNullOrWhiteSpace($signtool) -or -not (Test-Path $signtool)) {
    Write-Host "ERROR: signtool.exe not found!" -ForegroundColor Red
    Write-Host "Please install Windows SDK or set WPFDEVTOOLS_SIGNTOOL_PATH." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $CertificatePath)) {
    Write-Host "ERROR: Certificate file not found: $CertificatePath" -ForegroundColor Red
    exit 1
}

$binaries = Get-BinariesToSign -Roots (Get-BinaryRoots)
if ($binaries.Count -eq 0) {
    Write-Host "No binaries found to sign. Did you build the project?" -ForegroundColor Yellow
    exit 0
}

Write-Host "Found $($binaries.Count) binaries to sign" -ForegroundColor Cyan

$successCount = 0
$failCount = 0

foreach ($binary in $binaries) {
    Write-Host "Signing: $($binary.FullName)" -ForegroundColor Gray

    $result = & $signtool sign `
        /f $CertificatePath `
        /p $Password `
        /fd SHA256 `
        /tr $TimestampServer `
        /td SHA256 `
        /v `
        $binary.FullName 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Signed successfully" -ForegroundColor Green
        $successCount++
    }
    else {
        Write-Host "  Failed to sign" -ForegroundColor Red
        Write-Host "  Error: $result" -ForegroundColor Red
        $failCount++
    }
}

Write-Host "`nSigning Summary:" -ForegroundColor Yellow
Write-Host "  Success: $successCount" -ForegroundColor Green
Write-Host "  Failed: $failCount" -ForegroundColor Red

if ($failCount -gt 0) {
    Write-Host "`nSome binaries failed to sign. Please check the errors above." -ForegroundColor Red
    exit 1
}

Write-Host "`nAll binaries signed successfully!" -ForegroundColor Green
