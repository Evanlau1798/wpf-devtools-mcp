param(
    [Parameter(Mandatory)] [string]$ServerPath,
    [int]$InitializeTimeoutMilliseconds = 10000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ProcessDiagnostics {
    param([Parameter(Mandatory)] [System.Diagnostics.Process]$Process)

    try {
        return ($Process.StandardError.ReadToEnd() ?? '').Trim()
    }
    catch {
        return ''
    }
}

$resolvedServerPath = (Resolve-Path -LiteralPath $ServerPath).Path
$serverDirectory = Split-Path -LiteralPath $resolvedServerPath -Parent

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $resolvedServerPath
$startInfo.WorkingDirectory = $serverDirectory
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.CreateNoWindow = $true
$startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
$startInfo.Environment['WPFDEVTOOLS_RATE_LIMIT_RPM'] = '2000'

$process = New-Object System.Diagnostics.Process
$process.StartInfo = $startInfo

try {
    if (-not $process.Start()) {
        throw "Failed to start packaged server: $resolvedServerPath"
    }

    $initializeRequest = @{
        jsonrpc = '2.0'
        id = 1
        method = 'initialize'
        params = @{
            protocolVersion = '2025-06-18'
            capabilities = @{}
            clientInfo = @{
                name = 'packaged-runtime-smoke'
                version = '1.0.0'
            }
        }
    } | ConvertTo-Json -Compress -Depth 6

    $process.StandardInput.WriteLine($initializeRequest)
    $process.StandardInput.Flush()

    $responseTask = $process.StandardOutput.ReadLineAsync()
    if (-not $responseTask.Wait($InitializeTimeoutMilliseconds)) {
        if (-not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit(5000)
        }

        $stderr = Get-ProcessDiagnostics -Process $process
        throw "Timed out waiting for initialize response from packaged server. Stderr: $stderr"
    }

    $responseLine = $responseTask.Result
    if ([string]::IsNullOrWhiteSpace($responseLine)) {
        if (-not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit(5000)
        }

        $stderr = Get-ProcessDiagnostics -Process $process
        throw "Packaged server closed stdout before returning initialize response. Stderr: $stderr"
    }

    $response = $responseLine | ConvertFrom-Json
    if ($null -ne $response.error) {
        throw "Packaged server initialize returned an error: $($response.error | ConvertTo-Json -Compress -Depth 6)"
    }

    if ($response.result.serverInfo.name -ne 'wpf-devtools-mcp') {
        throw "Unexpected packaged server identity: $($response.result.serverInfo.name)"
    }

    $initializedNotification = @{
        jsonrpc = '2.0'
        method = 'notifications/initialized'
        params = @{}
    } | ConvertTo-Json -Compress -Depth 4

    $process.StandardInput.WriteLine($initializedNotification)
    $process.StandardInput.Flush()
}
finally {
    if ($process -ne $null) {
        try {
            if (-not $process.HasExited) {
                $process.Kill()
                $process.WaitForExit(5000)
            }
        }
        finally {
            $process.Dispose()
        }
    }
}