function Get-ProcessDiagnostics {
    param([Parameter(Mandatory)] [System.Diagnostics.Process]$Process)

    if (-not $Process.HasExited) {
        return "Process $($Process.Id) is still running; stderr was not drained to avoid blocking diagnostics."
    }

    try {
        $output = $Process.StandardError.ReadToEnd()
        if ($null -eq $output) {
            return ''
        }

        return $output.Trim()
    }
    catch {
        return ''
    }
}

function Stop-PackagedServerProcess {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process]$Process,
        [string]$TaskKillCommand = 'taskkill.exe',
        [int]$WaitMilliseconds = 5000
    )

    if ($Process.HasExited) {
        return $true
    }

    $hadLastExitCode = Test-Path -LiteralPath 'Variable:\global:LASTEXITCODE'
    if ($hadLastExitCode) {
        $previousLastExitCode = $global:LASTEXITCODE
    }

    try {
        & $TaskKillCommand /PID $Process.Id /T /F *> $null
        $Process.WaitForExit($WaitMilliseconds) | Out-Null
        if (-not $Process.HasExited) {
            & $TaskKillCommand /PID $Process.Id /T /F *> $null
        }

        $Process.WaitForExit($WaitMilliseconds) | Out-Null
    }
    finally {
        if ($hadLastExitCode) {
            $global:LASTEXITCODE = $previousLastExitCode
        }
        else {
            Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
        }
    }

    return [bool]$Process.HasExited
}
