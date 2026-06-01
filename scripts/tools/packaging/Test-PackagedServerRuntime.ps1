param(
    [Parameter(Mandatory)] [string]$ServerPath,
    [ValidateSet('x64', 'x86', 'arm64')] [string]$Architecture = 'x64',
    [int]$TargetProcessId = 0,
    [string]$TargetProcessPath = '',
    [string]$EvidenceOutputPath = '',
    [int]$InitializeTimeoutMilliseconds = 10000,
    [int]$RequestTimeoutMilliseconds = 10000,
    [switch]$SkipExistingHostReuse
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
        [Parameter(Mandatory)] [int]$TimeoutMilliseconds,
        [bool]$AllowError = $false
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
        Stop-PackagedServerProcess -Process $Process
        $stderr = Get-ProcessDiagnostics -Process $Process
        throw "Packaged server $OperationName returned stdout contamination before JSON-RPC response: $responseLine. Error: $($_.Exception.Message). Stderr: $stderr"
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
    if ($null -ne $errorPayload -and -not $AllowError) {
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
        [Parameter(Mandatory)] [int]$TimeoutMilliseconds,
        [switch]$AllowError
    )

    $request = @{
        jsonrpc = '2.0'
        id = $Id
        method = $Method
        params = $Params
    } | ConvertTo-Json -Compress -Depth 12

    $Process.StandardInput.WriteLine($request)
    $Process.StandardInput.Flush()

    return Read-McpResponse -Process $Process -OperationName $Method -ExpectedResponseId $Id -TimeoutMilliseconds $TimeoutMilliseconds -AllowError:$AllowError
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

function Get-Sha256Hex {
    param([Parameter(Mandatory)] [string]$Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return (($sha256.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '')
    }
    finally {
        $sha256.Dispose()
    }
}

function Write-RuntimeEvidence {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [object]$ToolsResponse,
        [Parameter(Mandatory)] [bool]$TargetAwareLiveSmokePassed
    )

    $toolNames = @($ToolsResponse.result.tools | ForEach-Object { [string]$_.name } | Sort-Object)
    $nameSetHash = Get-Sha256Hex -Value ($toolNames -join "`n")
    $schemaJson = $ToolsResponse.result.tools | ConvertTo-Json -Compress -Depth 64
    $schemaSnapshotHash = Get-Sha256Hex -Value $schemaJson
    $packageSmokeStatus = if ($TargetAwareLiveSmokePassed) { 'passed' } else { 'not-run' }
    $packageSmoke = [ordered]@{
        x64PackageLocal = 'passed-or-not-public'
        x64OnlineInstaller = 'passed-or-not-public'
        x86PackageLocal = 'passed-or-not-public'
        x86OnlineInstaller = 'passed-or-not-public'
        arm64PackageLocal = 'passed-or-not-public'
        arm64OnlineInstaller = 'passed-or-not-public'
    }
    $packageSmoke["$($Architecture)PackageLocal"] = $packageSmokeStatus
    $packageSmoke["$($Architecture)OnlineInstaller"] = $packageSmokeStatus
    $runtimeEvidence = [ordered]@{
        generatedUtc = (Get-Date).ToUniversalTime().ToString('O')
        mode = $smokeMode
        serverPathRedacted = $true
        toolsList = [ordered]@{
            count = $toolNames.Count
            nameSetHash = $nameSetHash
            schemaSnapshotHash = $schemaSnapshotHash
        }
        security = [ordered]@{
            mitmMatrixPassed = $false
            stdoutPurityPassed = $true
            screenshotIntegrityPassed = $false
        }
        packageSmoke = $packageSmoke
        liveSmoke = [ordered]@{
            connect = $TargetAwareLiveSmokePassed
            ping = $TargetAwareLiveSmokePassed
            getUiSummary = $TargetAwareLiveSmokePassed
            safeRead = $TargetAwareLiveSmokePassed
            mutationRestore = $TargetAwareLiveSmokePassed
            uninstallResidue = $false
        }
    }

    $directory = Split-Path -Parent ([System.IO.Path]::GetFullPath($Path))
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $runtimeEvidence | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding UTF8
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

function Invoke-FailingMcpTool {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)] [int]$Id,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [hashtable]$Arguments,
        [Parameter(Mandatory)] [int]$TimeoutMilliseconds
    )

    $response = Invoke-McpRequest -Process $Process -Id $Id -Method 'tools/call' -TimeoutMilliseconds $TimeoutMilliseconds -AllowError -Params @{
        name = $Name
        arguments = $Arguments
    }

    $jsonRpcError = Get-JsonProperty -Object $response -Name 'error'
    if ($null -ne $jsonRpcError) {
        return
    }

    $result = Get-JsonProperty -Object $response -Name 'result'
    if ($null -ne $result -and $result.isError -eq $true) {
        return
    }

    throw "Packaged server $Name failure probe unexpectedly succeeded: $($response | ConvertTo-Json -Compress -Depth 8)"
}

function Resolve-SmokeElementId {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)] [int]$Id,
        [Parameter(Mandatory)] [int]$TargetProcessId,
        [Parameter(Mandatory)] [string]$ElementName,
        [Parameter(Mandatory)] [int]$TimeoutMilliseconds
    )

    $namescope = Invoke-McpTool -Process $Process -Id $Id -Name 'get_namescope' -TimeoutMilliseconds $TimeoutMilliseconds -Arguments @{
        processId = $TargetProcessId
    }

    $namedElements = @(Get-JsonProperty -Object $namescope -Name 'namedElements')
    foreach ($element in $namedElements) {
        if ((Get-JsonProperty -Object $element -Name 'name') -eq $ElementName) {
            $elementId = Get-JsonProperty -Object $element -Name 'elementId'
            if (-not [string]::IsNullOrWhiteSpace([string]$elementId)) {
                return [string]$elementId
            }
        }
    }

    throw "Packaged runtime smoke could not resolve TestApp element '$ElementName' from get_namescope."
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

$smokeMode = if ($TargetProcessId -gt 0) { 'target-aware live-injection' } else { 'targetless protocol-only' }
$clientName = if ($TargetProcessId -gt 0) { 'packaged-runtime-live-injection-smoke' } else { 'packaged-runtime-protocol-only-smoke' }
Write-Host "Running $smokeMode packaged runtime smoke for $resolvedServerPath."

if ($TargetProcessId -gt 0) {
    if ([string]::IsNullOrWhiteSpace($TargetProcessPath)) {
        throw 'TargetProcessPath is required when TargetProcessId is specified so packaged smoke can set exact target allowlists.'
    }

    $resolvedTargetProcessPath = (Resolve-Path -LiteralPath $TargetProcessPath).Path
    $startInfo.Environment['WPFDEVTOOLS_MCP_ALLOWED_TARGETS'] = $resolvedTargetProcessPath
    $startInfo.Environment['WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS'] = $resolvedTargetProcessPath
    $startInfo.Environment['WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS'] = 'true'
    $startInfo.Environment['WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS'] = 'true'
    if ($SkipExistingHostReuse) {
        $startInfo.Environment['WPFDEVTOOLS_MCP_SKIP_EXISTING_HOST_REUSE'] = 'true'
    }

    if ($env:WPFDEVTOOLS_INSTALLER_TEST_MODE -eq '1' -and
        [string]::Equals($env:WPFDEVTOOLS_TEST_SIGNATURE_STATUS, 'Valid', [System.StringComparison]::OrdinalIgnoreCase)) {
        $startInfo.Environment['WPFDEVTOOLS_TEST_TRUST_LOCAL_RELEASE_SIGNATURE_SKIP'] = '1'
    }
}

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
            name = $clientName
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

    Invoke-FailingMcpTool -Process $process -Id 4 -Name 'connect' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
        processId = -1
    }

    # Any non-JSON stdout emitted after this failure is caught by the next successful request.
    Invoke-McpTool -Process $process -Id 5 -Name 'get_processes' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
        windowFilter = 'visible'
    } | Out-Null

    $targetAwareLiveSmokePassed = $false
    if ($TargetProcessId -gt 0) {
        for ($connectAttempt = 1; ; $connectAttempt++) {
            try {
                Invoke-McpTool -Process $process -Id 6 -Name 'connect' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{ processId = $TargetProcessId } | Out-Null
                break
            }
            catch {
                $targetStillStarting = $_.Exception.Message -match '(?:"|\\")errorCode(?:"|\\")\s*:\s*(?:"|\\")NotWpfApplication(?:"|\\")'
                if (-not $targetStillStarting -or $connectAttempt -ge 20) { throw }
                Write-Host "Retrying packaged server connect after transient target-readiness error ($connectAttempt/20)."
                Start-Sleep -Milliseconds 500
            }
        }

        Invoke-McpTool -Process $process -Id 7 -Name 'ping' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
            processId = $TargetProcessId
        } | Out-Null

        Invoke-McpTool -Process $process -Id 8 -Name 'get_ui_summary' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
            processId = $TargetProcessId
            depthMode = 'semantic'
        } | Out-Null

        $smokeElementId = Resolve-SmokeElementId `
            -Process $process `
            -Id 9 `
            -TargetProcessId $TargetProcessId `
            -ElementName 'FocusStatusTextBlock' `
            -TimeoutMilliseconds $RequestTimeoutMilliseconds

        $beforeValueSource = Invoke-McpTool -Process $process -Id 10 -Name 'get_dp_value_source' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
            processId = $TargetProcessId
            elementId = $smokeElementId
            propertyName = 'Text'
        }
        $baselineValue = Get-JsonProperty -Object $beforeValueSource -Name 'currentValue'

        $snapshot = Invoke-McpTool -Process $process -Id 11 -Name 'capture_state_snapshot' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
            processId = $TargetProcessId
            elementId = $smokeElementId
            propertyNames = @('Text')
        }
        $snapshotId = Get-JsonProperty -Object $snapshot -Name 'snapshotId'
        if ([string]::IsNullOrWhiteSpace([string]$snapshotId)) {
            throw "Packaged runtime smoke capture_state_snapshot did not return snapshotId: $($snapshot | ConvertTo-Json -Compress -Depth 8)"
        }

        $overrideValue = "Packaged runtime smoke $([System.Guid]::NewGuid().ToString('N'))"
        try {
            Invoke-McpTool -Process $process -Id 12 -Name 'set_dp_value' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
                processId = $TargetProcessId
                elementId = $smokeElementId
                propertyName = 'Text'
                value = $overrideValue
            } | Out-Null

            $mutatedValueSource = Invoke-McpTool -Process $process -Id 13 -Name 'get_dp_value_source' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
                processId = $TargetProcessId
                elementId = $smokeElementId
                propertyName = 'Text'
            }
            $mutatedValue = Get-JsonProperty -Object $mutatedValueSource -Name 'currentValue'
            if ($mutatedValue -ne $overrideValue) {
                throw "Packaged runtime smoke set_dp_value did not update Text. Expected '$overrideValue', got '$mutatedValue'."
            }
        }
        finally {
            Invoke-McpTool -Process $process -Id 14 -Name 'restore_state_snapshot' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
                processId = $TargetProcessId
                snapshotId = $snapshotId
            } | Out-Null
        }

        $restoredValueSource = Invoke-McpTool -Process $process -Id 15 -Name 'get_dp_value_source' -TimeoutMilliseconds $RequestTimeoutMilliseconds -Arguments @{
            processId = $TargetProcessId
            elementId = $smokeElementId
            propertyName = 'Text'
        }
        $restoredValue = Get-JsonProperty -Object $restoredValueSource -Name 'currentValue'
        if ($restoredValue -ne $baselineValue) {
            throw "Packaged runtime smoke restore_state_snapshot did not restore Text. Expected '$baselineValue', got '$restoredValue'."
        }

        $targetAwareLiveSmokePassed = $true
    }

    if (-not [string]::IsNullOrWhiteSpace($EvidenceOutputPath)) {
        Write-RuntimeEvidence `
            -Path $EvidenceOutputPath `
            -ToolsResponse $toolsResponse `
            -TargetAwareLiveSmokePassed $targetAwareLiveSmokePassed
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
