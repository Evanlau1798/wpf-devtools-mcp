function Resolve-PackagingRepoRoot {
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
}

function Get-PackagedServerExpectedToolNames {
    param([Parameter(Mandatory)] [string]$RepoRoot)

    $resolverPath = Join-Path $RepoRoot 'scripts\ci\Get-McpToolNames.ps1'
    if (-not (Test-Path -LiteralPath $resolverPath -PathType Leaf)) {
        throw "MCP tool-name resolver not found: $resolverPath"
    }

    . $resolverPath
    $names = @(Get-McpToolNames -RepoRoot $RepoRoot)
    if ($names.Count -eq 0) {
        throw "MCP tool-name resolver returned no expected tools for repo root: $RepoRoot"
    }

    return $names
}

function Test-McpToolListContract {
    param(
        [Parameter(Mandatory)] [object]$ToolsResponse,
        [Parameter(Mandatory)] [string[]]$ExpectedToolNames,
        [int]$ExpectedToolCount = 64,
        [string[]]$RepresentativeToolNames = @(
            'connect',
            'get_processes',
            'get_ui_summary',
            'get_element_snapshot',
            'get_bindings',
            'capture_state_snapshot',
            'get_state_diff',
            'restore_state_snapshot'
        )
    )

    $expectedNames = @($ExpectedToolNames |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique)
    if ($expectedNames.Count -ne $ExpectedToolCount) {
        throw "Expected source MCP tool contract to contain expected $ExpectedToolCount tools, but found $($expectedNames.Count)."
    }

    $toolNames = @($ToolsResponse.result.tools |
        ForEach-Object { [string]$_.name } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $actualNames = @($toolNames | Sort-Object -Unique)
    if ($actualNames.Count -ne $toolNames.Count) {
        throw "Packaged server tools/list returned duplicate tool names. Tools: $($toolNames -join ', ')"
    }

    $missing = @($expectedNames | Where-Object { $actualNames -notcontains $_ })
    $unexpected = @($actualNames | Where-Object { $expectedNames -notcontains $_ })
    if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
        throw "Packaged server tools/list contract mismatch. Missing: $($missing -join ', '); Unexpected: $($unexpected -join ', ')"
    }

    $representativeMissing = @($RepresentativeToolNames | Where-Object { $actualNames -notcontains $_ })
    if ($representativeMissing.Count -gt 0) {
        throw "Packaged server tools/list omitted representative tools: $($representativeMissing -join ', ')"
    }
}
