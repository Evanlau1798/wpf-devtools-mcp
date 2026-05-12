function Invoke-External {
    param(
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [string]$FilePath,
        [Parameter(Mandatory = $true)] [string[]]$Arguments
    )

    Write-Host ""
    Write-Host ">>> $Name"
    & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Invoke-ExternalWithTimeout {
    param(
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [string]$FilePath,
        [Parameter(Mandatory = $true)] [string[]]$Arguments,
        [Parameter(Mandatory = $true)] [int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)] [string]$OutputRoot,
        [Parameter(Mandatory = $true)] [string]$Timestamp
    )

    Write-Host ""
    Write-Host ">>> $Name"

    $safeName = $Name -replace '[^A-Za-z0-9_.-]', '_'
    $processLogRoot = Join-Path $OutputRoot "logs\process\$Timestamp"
    New-Item -ItemType Directory -Force -Path $processLogRoot | Out-Null
    $commandPath = Join-Path $processLogRoot "$safeName.command.log"
    $stdoutPath = Join-Path $processLogRoot "$safeName.stdout.log"
    $stderrPath = Join-Path $processLogRoot "$safeName.stderr.log"

    Remove-Item -LiteralPath $commandPath, $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue

    $processStartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $processStartInfo.FileName = $FilePath
    $processStartInfo.Arguments = ConvertTo-ProcessArguments -Arguments $Arguments
    $processStartInfo.WorkingDirectory = (Get-Location).ProviderPath
    $processStartInfo.UseShellExecute = $false
    $processStartInfo.CreateNoWindow = $true
    $processStartInfo.RedirectStandardOutput = $true
    $processStartInfo.RedirectStandardError = $true
    $processStartInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $processStartInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8

    [System.IO.File]::WriteAllLines(
        $commandPath,
        @($FilePath) + $Arguments,
        [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($stdoutPath, '', [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($stderrPath, '', [System.Text.Encoding]::UTF8)

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $processStartInfo

    if (-not $process.Start()) {
        throw "$Name did not start."
    }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Write-Host "$Name timed out after $TimeoutSeconds seconds. Killing process tree for PID $($process.Id)."
        KillProcessTree -ProcessId $process.Id
        if (-not $process.WaitForExit(30000)) {
            $process.Dispose()
            throw "$Name timed out after $TimeoutSeconds seconds and did not exit within 30 seconds after cleanup."
        }

        $process.WaitForExit()
        $process.Refresh()
        Write-CompletedProcessLogs -Name $Name -StdoutTask $stdoutTask -StderrTask $stderrTask -StdoutPath $stdoutPath -StderrPath $stderrPath
        throw "$Name timed out after $TimeoutSeconds seconds."
    }

    $process.WaitForExit()
    $process.Refresh()
    Write-CompletedProcessLogs -Name $Name -StdoutTask $stdoutTask -StderrTask $stderrTask -StdoutPath $stdoutPath -StderrPath $stderrPath

    if ([int]$process.ExitCode -ne 0) {
        throw "$Name failed with exit code $([int]$process.ExitCode)."
    }
}

function ConvertTo-ProcessArguments {
    param([Parameter(Mandatory = $true)] [string[]]$Arguments)

    return ($Arguments | ForEach-Object { ConvertTo-ProcessArgument -Argument $_ }) -join ' '
}

function ConvertTo-ProcessArgument {
    param([Parameter(Mandatory = $true)] [string]$Argument)

    if ($Argument.Length -eq 0) {
        return '""'
    }

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $backslashCount = 0

    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq '\') {
            $backslashCount++
            continue
        }

        if ($character -eq '"') {
            if ($backslashCount -gt 0) {
                [void]$builder.Append('\' * ($backslashCount * 2))
            }

            [void]$builder.Append('\"')
            $backslashCount = 0
            continue
        }

        if ($backslashCount -gt 0) {
            [void]$builder.Append('\' * $backslashCount)
            $backslashCount = 0
        }

        [void]$builder.Append($character)
    }

    if ($backslashCount -gt 0) {
        [void]$builder.Append('\' * ($backslashCount * 2))
    }

    [void]$builder.Append('"')
    return $builder.ToString()
}

function Write-CompletedProcessLogs {
    param(
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [System.Threading.Tasks.Task[string]]$StdoutTask,
        [Parameter(Mandatory = $true)] [System.Threading.Tasks.Task[string]]$StderrTask,
        [Parameter(Mandatory = $true)] [string]$StdoutPath,
        [Parameter(Mandatory = $true)] [string]$StderrPath
    )

    if (-not [System.Threading.Tasks.Task]::WaitAll(@($StdoutTask, $StderrTask), 30000)) {
        throw "$Name exited, but redirected output streams did not close within 30 seconds."
    }

    [System.IO.File]::WriteAllText($StdoutPath, [string]$StdoutTask.Result, [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($StderrPath, [string]$StderrTask.Result, [System.Text.Encoding]::UTF8)
    Write-ProcessLogs -StdoutPath $StdoutPath -StderrPath $StderrPath
}

function KillProcessTree {
    param([Parameter(Mandatory = $true)] [int]$ProcessId)

    # This only cleans up timed-out child commands inside the sandbox runner.
    # Windows Sandbox VM cleanup must use Stop-WindowsSandboxHcs.ps1.
    & taskkill.exe /F /T /PID $ProcessId 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "taskkill.exe exited with code $LASTEXITCODE while cleaning up timed-out PID $ProcessId."
    }
}

function Write-ProcessLogs {
    param(
        [Parameter(Mandatory = $true)] [string]$StdoutPath,
        [Parameter(Mandatory = $true)] [string]$StderrPath
    )

    if (Test-Path -LiteralPath $StdoutPath) {
        Get-Content -LiteralPath $StdoutPath | ForEach-Object { Write-Host $_ }
    }

    if (Test-Path -LiteralPath $StderrPath) {
        Get-Content -LiteralPath $StderrPath | ForEach-Object { Write-Host $_ }
    }
}
