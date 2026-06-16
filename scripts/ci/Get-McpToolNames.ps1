function Get-McpToolNames {
    param([Parameter(Mandatory = $true)] [string]$RepoRoot)

    $manifestNames = @(Get-McpToolNamesFromRuntimeManifest -RepoRoot $RepoRoot)
    if ($manifestNames.Count -gt 0) {
        return $manifestNames
    }

    return Get-McpToolNamesFromSourceAttributes -RepoRoot $RepoRoot
}

function Get-McpToolNamesFromRuntimeManifest {
    param([Parameter(Mandatory = $true)] [string]$RepoRoot)

    $serverBinRoot = Join-Path $RepoRoot 'src\WpfDevTools.Mcp.Server\bin'
    if (-not (Test-Path -LiteralPath $serverBinRoot -PathType Container)) {
        return @()
    }

    $candidates = @(
        Get-ChildItem -LiteralPath $serverBinRoot -Recurse -File -Filter 'WpfDevTools.Mcp.Server.dll' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending
    )
    foreach ($candidate in $candidates) {
        try {
            $assembly = [System.Reflection.Assembly]::LoadFrom($candidate.FullName)
            $type = $assembly.GetType('WpfDevTools.Mcp.Server.McpResources.CapabilityResources', $false)
            if ($null -eq $type) { continue }

            $method = $type.GetMethod('GetToolManifest', [System.Reflection.BindingFlags]'Public, Static')
            if ($null -eq $method) { continue }

            $manifestJson = [string]$method.Invoke($null, @())
            $manifest = $manifestJson | ConvertFrom-Json
            $names = @(
                $manifest.tools |
                    ForEach-Object { [string]$_.name } |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                    Sort-Object -Unique
            )

            if ($names.Count -gt 0) {
                return $names
            }
        }
        catch {
            continue
        }
    }

    return @()
}

function Get-McpToolNamesFromSourceAttributes {
    param([Parameter(Mandatory = $true)] [string]$RepoRoot)

    $toolsRoot = Join-Path $RepoRoot 'src\WpfDevTools.Mcp.Server\McpTools'
    if (-not (Test-Path -LiteralPath $toolsRoot -PathType Container)) {
        return @()
    }

    $names = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem -LiteralPath $toolsRoot -Recurse -File -Filter '*.cs') {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($block in Get-McpServerToolAttributeBlocks -Content $content) {
            $name = Get-McpServerToolNameFromAttributeBlock -Block $block
            if (-not [string]::IsNullOrWhiteSpace($name)) {
                [void]$names.Add($name)
            }
        }
    }

    return @($names | Sort-Object)
}

function Get-McpServerToolAttributeBlocks {
    param([Parameter(Mandatory = $true)] [string]$Content)

    $blocks = New-Object System.Collections.Generic.List[string]
    $start = 0
    while ($start -lt $Content.Length) {
        $index = $Content.IndexOf('[McpServerTool', $start, [System.StringComparison]::Ordinal)
        if ($index -lt 0) { break }

        $depth = 0
        $inString = $false
        $escape = $false
        for ($i = $index; $i -lt $Content.Length; $i++) {
            $ch = $Content[$i]
            if ($inString) {
                if ($escape) {
                    $escape = $false
                    continue
                }

                if ($ch -eq '\') {
                    $escape = $true
                    continue
                }

                if ($ch -eq '"') {
                    $inString = $false
                }

                continue
            }

            if ($ch -eq '"') {
                $inString = $true
                continue
            }

            if ($ch -eq '[') {
                $depth++
            }
            elseif ($ch -eq ']') {
                $depth--
                if ($depth -eq 0) {
                    [void]$blocks.Add($Content.Substring($index, $i - $index + 1))
                    $start = $i + 1
                    break
                }
            }
        }

        if ($i -ge $Content.Length) { break }
    }

    return $blocks
}

function Get-McpServerToolNameFromAttributeBlock {
    param([Parameter(Mandatory = $true)] [string]$Block)

    $search = 0
    while ($search -lt $Block.Length) {
        $nameIndex = $Block.IndexOf('Name', $search, [System.StringComparison]::Ordinal)
        if ($nameIndex -lt 0) { break }

        $search = $nameIndex + 4
        if (-not (Test-CSharpIdentifierBoundary -Text $Block -Index $nameIndex -Length 4)) {
            continue
        }

        $cursor = Skip-CSharpWhitespace -Text $Block -Index $search
        if ($cursor -ge $Block.Length -or $Block[$cursor] -ne '=') {
            continue
        }

        $cursor = Skip-CSharpWhitespace -Text $Block -Index ($cursor + 1)
        if ($cursor -ge $Block.Length -or $Block[$cursor] -ne '"') {
            continue
        }

        return Read-CSharpStringLiteral -Text $Block -QuoteIndex $cursor
    }

    return $null
}

function Test-CSharpIdentifierBoundary {
    param(
        [Parameter(Mandatory = $true)] [string]$Text,
        [Parameter(Mandatory = $true)] [int]$Index,
        [Parameter(Mandatory = $true)] [int]$Length
    )

    $before = $Index -eq 0 -or -not (Test-CSharpIdentifierChar -Char $Text[$Index - 1])
    $afterIndex = $Index + $Length
    $after = $afterIndex -ge $Text.Length -or -not (Test-CSharpIdentifierChar -Char $Text[$afterIndex])
    return $before -and $after
}

function Test-CSharpIdentifierChar {
    param([Parameter(Mandatory = $true)] [char]$Char)

    return [char]::IsLetterOrDigit($Char) -or $Char -eq '_'
}

function Skip-CSharpWhitespace {
    param(
        [Parameter(Mandatory = $true)] [string]$Text,
        [Parameter(Mandatory = $true)] [int]$Index
    )

    while ($Index -lt $Text.Length -and [char]::IsWhiteSpace($Text[$Index])) {
        $Index++
    }

    return $Index
}

function Read-CSharpStringLiteral {
    param(
        [Parameter(Mandatory = $true)] [string]$Text,
        [Parameter(Mandatory = $true)] [int]$QuoteIndex
    )

    $builder = [System.Text.StringBuilder]::new()
    $escape = $false
    for ($i = $QuoteIndex + 1; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($escape) {
            [void]$builder.Append($ch)
            $escape = $false
            continue
        }

        if ($ch -eq '\') {
            $escape = $true
            continue
        }

        if ($ch -eq '"') {
            return $builder.ToString()
        }

        [void]$builder.Append($ch)
    }

    return $null
}
