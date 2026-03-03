# Sign Binaries with Code Signing Certificate
# This script signs all DLL and EXE files in the build output

param(
    [Parameter(Mandatory=$true)]
    [string]$CertificatePath,

    [Parameter(Mandatory=$true)]
    [string]$Password,

    [string]$BuildConfiguration = "Release",

    [string]$TimestampServer = "http://timestamp.digicert.com"
)

Write-Host "Signing binaries with code signing certificate..." -ForegroundColor Green

# Check if signtool.exe is available
$signtool = "signtool.exe"
try {
    & $signtool /? | Out-Null
} catch {
    Write-Host "ERROR: signtool.exe not found!" -ForegroundColor Red
    Write-Host "Please install Windows SDK: https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/" -ForegroundColor Yellow
    exit 1
}

# Check if certificate file exists
if (-not (Test-Path $CertificatePath)) {
    Write-Host "ERROR: Certificate file not found: $CertificatePath" -ForegroundColor Red
    exit 1
}

# Find all binaries to sign
$binaries = @()
$binaries += Get-ChildItem -Path ".\src\*\bin\$BuildConfiguration" -Include "*.dll","*.exe" -Recurse
$binaries += Get-ChildItem -Path ".\tests\*\bin\$BuildConfiguration" -Include "*.dll","*.exe" -Recurse

# Filter out third-party binaries (only sign our own)
$binaries = $binaries | Where-Object {
    $_.Name -like "WpfDevTools.*"
}

if ($binaries.Count -eq 0) {
    Write-Host "No binaries found to sign. Did you build the project?" -ForegroundColor Yellow
    exit 0
}

Write-Host "Found $($binaries.Count) binaries to sign" -ForegroundColor Cyan

$successCount = 0
$failCount = 0

foreach ($binary in $binaries) {
    Write-Host "Signing: $($binary.FullName)" -ForegroundColor Gray

    # Sign the binary
    $result = & $signtool sign `
        /f $CertificatePath `
        /p $Password `
        /fd SHA256 `
        /tr $TimestampServer `
        /td SHA256 `
        /v `
        $binary.FullName 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Signed successfully" -ForegroundColor Green
        $successCount++
    } else {
        Write-Host "  ✗ Failed to sign" -ForegroundColor Red
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
