param(
    [Parameter(Mandatory)] [string]$ServerPath,
    [int]$TargetProcessId = 0,
    [int]$InitializeTimeoutMilliseconds = 10000,
    [int]$RequestTimeoutMilliseconds = 10000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ProcessDiagnostics {
    param([Parameter(Mandatory)] [System.Diagnostics.Process]$Process)

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
    param([Parameter(Mandatory)] [System.Diagnostics.Process]$Process)

    if (-not $Process.HasExited) {
        $Process.Kill()
        $Process.WaitForExit(5000) | Out-Null
    }
}

function Read-McpResponse {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)] [string]$OperationName,
        [Parameter(Mandatory)] [int]$ExpectedResponseId,
        [Parameter(Mandatory)] [int]$TimeoutMilliseconds
    )

    $responseTask = $Process.StandardOutput.ReadLineAsync()
    if (-not $responseTask.Wait($TimeoutMilliseconds)) {
        Stop-PackagedServerProcess -Process $Process
        $stderr = Get-ProcessDiagnostics -Process $Process
        throw "Timed out waiting for $OperationName response from packaged server. Stderr: $stderr"
    }

    $responseLine = $responseTask.Result
    if ([string]::IsNullOrWhiteSpace($responseLine)) {
        Stop-PackagedServerProcess -Process $Process
        $stderr = Get-ProcessDiagnostics -Process $Process
        throw "Packaged server closed stdout before returning $OperationName response. Stderr: $stderr"
    }

    try {
        $response = $responseLine | ConvertFrom-Json
    }
    catch {
        throw "Packaged server $OperationName returned malformed JSON-RPC: $responseLine. Error: $($_.Exception.Message)"
    }

    $jsonRpcVersion = Get-JsonProperty -Object $response -Name 'jsonrpc'
    if ($jsonRpcVersion -ne '2.0') {
        throw "Packaged server $OperationName returned unexpected JSON-RPC version: $jsonRpcVersion. Response: $responseLine"
    }

    $responseId = Get-JsonProperty -Object $response -Name 'id'
    if (-not ($responseId -is [int] -or $responseId -is [long])) {
        throw "Packaged server $OperationName returned non-integer response id $responseId. Response: $responseLine"
    }

    if ($responseId -ne $ExpectedResponseId) {
        throw "Packaged server $OperationName returned response id $responseId, expected $ExpectedResponseId. Response: $responseLine"
    }

    $errorPayload = Get-JsonProperty -Object $response -Name 'error'
    if ($null -ne $errorPayload) {
        throw "Packaged server $OperationName returned an error: $($errorPayload | ConvertTo-Json -Compress -Depth 8)"
    }

    return $response
}

function Invoke-McpRequest {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)] [int]$Id,
        [Parameter(Mandatory)] [string]$Method,
        [Parameter(Mandatory)] [object]$Params,
        [Parameter(Mandatory)] [int]$TimeoutMilliseconds
    )

    $request = @{
        jsonrpc = '2.0'
        id = $Id
        method = $Method
        params = $Params
    } | ConvertTo-Json -Compress -Depth 12

    $Process.StandardInput.WriteLine($request)
    $Process.StandardInput.Flush()

    return Read-McpResponse -Process $Process -OperationName $Method -ExpectedResponseId $Id -TimeoutMilliseconds $TimeoutMilliseconds
}

function Send-McpNotification {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)] [string]$Method,
        [Parameter(Mandatory)] [object]$Params
    )

    $notification = @{
        jsonrpc = '2.0'
        method = $Method
        params = $Params
    } | ConvertTo-Json -Compress -Depth 8

    $Process.StandardInput.WriteLine($notification)
    $Process.StandardInput.Flush()
}

function Get-JsonProperty {
    param(
        [Parameter(Mandatory)] [object]$Object,
        [Parameter(Mandatory)] [string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-McpToolResult {
    param(
        [Parameter(Mandatory)] [object]$ToolCallResponse,
        [Parameter(Mandatory)] [string]$ToolName
    )

    if ($ToolCallResponse.result.isError -eq $true) {
        throw "Packaged server $ToolName returned an MCP tool error: $($ToolCallResponse.result | ConvertTo-Json -Compress -Depth 8)"
    }

    $toolResult = Get-JsonProperty -Object $ToolCallResponse.result -Name 'structuredContent'
    if ($null -eq $toolResult) {
        $toolText = @($ToolCallResponse.result.content | ForEach-Object { $_.text } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) | Select-Object -First 1
        if ($null -ne $toolText) {
            try {
                $toolResult = $toolText | ConvertFrom-Json
            }
            catch {
                throw "Packaged server $ToolName returned malformed tool content JSON: $toolText. Error: $($_.Exception.Message)"
            }
        }
    }

    if ($null -eq $toolResult -or $toolResult.success -ne $true) {
        throw "Packaged server $ToolName did not return success: $($ToolCallResponse.result | ConvertTo-Json -Compress -Depth 8)"
    }

    return $toolResult
}

function Invoke-McpTool {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)] [int]$Id,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [hashtable]$Arguments,
        [Parameter(Mandatory)] [int]$TimeoutMilliseconds
    )

    $response = Invoke-McpRequest -Process $Process -Id $Id -Method 'tools/call' -TimeoutMilliseconds $TimeoutMilliseconds -Params @{
        name = $Name
        arguments = $Arguments
    }

    return Get-McpToolResult -ToolCallResponse $response -ToolName $Name
}

$resolvedServerPath = (Resolve-Path -LiteralPath $ServerPath).Path
$serverDirectory = [System.IO.Path]::GetDirectoryName($resolvedServerPath)

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

    $response = Invoke-McpRequest -Process $process -Id 1 -Method 'initialize' -TimeoutMilliseconds $InitializeTimeoutMilliseconds -Params @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{
            name = 'packaged-runtime-smoke'
            version = '1.0.0'
        }
    }

    if ($response.result.serverInfo.name -ne 'wpf-devtools-mcp') {
        throw "Unexpected packaged server identity: $($response.result.serverInfo.name)"
    }

    Send-McpNotification -Process $process -Method 'notifications/initialized' -Params @{}

    $toolsResponse = Invoke-McpRequest -Process $process -Id 2 -Method 'tools/list' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Params @{}
    $toolNames = @($toolsResponse.result.tools | ForEach-Object { $_.name })
    if (-not ($toolNames -contains 'get_processes')) {
        throw "Packaged server tools/list did not include get_processes. Tools: $($toolNames -join ', ')"
    }

    $resourceResponse = Invoke-McpRequest -Process $process -Id 3 -Method 'resources/read' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Params @{
        uri = 'wpf://capabilities'
    }
    $resourceText = @($resourceResponse.result.contents | ForEach-Object { $_.text }) -join "`n"
    if (-not $resourceText.Contains('WPF DevTools MCP Capabilities')) {
        throw 'Packaged server resources/read did not return the expected capabilities resource content.'
    }

    Invoke-McpTool -Process $process -Id 4 -Name 'get_processes' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
        windowFilter = 'visible'
    } | Out-Null

    if ($TargetProcessId -gt 0) {
        Invoke-McpTool -Process $process -Id 5 -Name 'connect' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
            processId = $TargetProcessId
        } | Out-Null

        Invoke-McpTool -Process $process -Id 6 -Name 'get_ui_summary' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
            processId = $TargetProcessId
            depthMode = 'semantic'
        } | Out-Null
    }
}
finally {
    if ($process -ne $null) {
        try {
            Stop-PackagedServerProcess -Process $process
        }
        finally {
            $process.Dispose()
        }
    }
}
