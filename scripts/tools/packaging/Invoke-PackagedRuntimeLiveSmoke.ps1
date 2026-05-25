param(
    [Parameter(Mandatory)] [string]$ServerPath,
    [ValidateSet('x64', 'x86', 'arm64')] [string]$Architecture = 'x64',
    [string]$Configuration = 'Release',
    [int]$StartupTimeoutSeconds = 30,
    [int]$RequestTimeoutMilliseconds = 20000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    $current = $PSScriptRoot
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path (Join-Path $current 'WpfDevTools.sln')) {
            return $current
        }

        $parent = Split-Path -Parent $current
        if ($parent -eq $current) {
            break
        }

        $current = $parent
    }

    throw 'Could not locate repository root from packaging script path.'
}

function ConvertTo-TestAppPlatform {
    param([Parameter(Mandatory)] [string]$Value)

    switch ($Value) {
        'x64' { return 'x64' }
        'x86' { return 'x86' }
        'arm64' { return 'ARM64' }
        default { throw "Unsupported architecture: $Value" }
    }
}

function Wait-TestAppReady {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)] [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "TestApp exited before its main window appeared. Exit code: $($Process.ExitCode)"
        }

        if ($Process.MainWindowHandle -ne [System.IntPtr]::Zero) {
            return
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "TestApp main window did not appear within $TimeoutSeconds seconds."
}

$repoRoot = Get-RepoRoot
$platform = ConvertTo-TestAppPlatform -Value $Architecture
$testAppProject = 'tests/WpfDevTools.Tests.TestApp/WpfDevTools.Tests.TestApp.csproj'

Push-Location $repoRoot
try {
    & dotnet build $testAppProject `
        --configuration $Configuration `
        -m:1 `
        "-p:Platform=$platform" `
        -nodeReuse:false `
        -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) {
        throw "TestApp build failed for $Architecture."
    }

    $targetProcessPath = Join-Path $repoRoot "tests\WpfDevTools.Tests.TestApp\bin\$Configuration\net8.0-windows\WpfDevTools.Tests.TestApp.exe"
    if (-not (Test-Path $targetProcessPath)) {
        throw "Could not locate built TestApp executable at $targetProcessPath."
    }

    $targetProcess = Start-Process -FilePath $targetProcessPath -PassThru
    try {
        Wait-TestAppReady -Process $targetProcess -TimeoutSeconds $StartupTimeoutSeconds

        & powershell -ExecutionPolicy Bypass -File scripts/tools/packaging/Test-PackagedServerRuntime.ps1 `
            -ServerPath $ServerPath `
            -TargetProcessId $targetProcess.Id `
            -TargetProcessPath $targetProcessPath `
            -RequestTimeoutMilliseconds $RequestTimeoutMilliseconds
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged runtime live smoke failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        if ($null -ne $targetProcess) {
            try {
                $targetProcess.Refresh()
                if (-not $targetProcess.HasExited) {
                    Stop-Process -Id $targetProcess.Id -Force
                    $targetProcess.WaitForExit(30000) | Out-Null
                }
            }
            finally {
                $targetProcess.Dispose()
            }
        }
    }
}
finally {
    Pop-Location
}
