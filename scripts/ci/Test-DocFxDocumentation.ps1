[CmdletBinding()]
param(
    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $scriptRoot = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
        $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    }

    $RepoRoot = (Resolve-Path (Join-Path $scriptRoot '..\..')).Path
}

$script:Failures = New-Object System.Collections.Generic.List[string]

function Add-ValidationFailure {
    param([Parameter(Mandatory = $true)] [string]$Message)

    [void]$script:Failures.Add($Message)
}

function Join-PathMany {
    param([Parameter(Mandatory = $true)] [string[]]$Parts)

    $path = $Parts[0]
    for ($i = 1; $i -lt $Parts.Count; $i++) {
        $path = Join-Path $path $Parts[$i]
    }

    return $path
}

function Join-RelativePath {
    param(
        [Parameter(Mandatory = $true)] [string]$Root,
        [Parameter(Mandatory = $true)] [string]$RelativePath
    )

    $segments = @($RelativePath -split '[\\/]' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    return Join-PathMany -Parts (@($Root) + $segments)
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)] [string]$BasePath,
        [Parameter(Mandatory = $true)] [string]$Path
    )

    $resolvedBase = [System.IO.Path]::GetFullPath($BasePath)
    if (-not $resolvedBase.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $resolvedBase += [System.IO.Path]::DirectorySeparatorChar
    }

    $baseUri = [System.Uri]::new($resolvedBase)
    $pathUri = [System.Uri]::new([System.IO.Path]::GetFullPath($Path))
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString())
}

function Test-IsUnderDirectory {
    param(
        [Parameter(Mandatory = $true)] [string]$Root,
        [Parameter(Mandatory = $true)] [string]$Path
    )

    $rootFull = [System.IO.Path]::GetFullPath($Root)
    if (-not $rootFull.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootFull += [System.IO.Path]::DirectorySeparatorChar
    }

    $pathFull = [System.IO.Path]::GetFullPath($Path)
    return $pathFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-MarkdownFiles {
    param([Parameter(Mandatory = $true)] [string]$DocfxRoot)

    Get-ChildItem -LiteralPath $DocfxRoot -Recurse -File -Filter '*.md' |
        Where-Object {
            $relative = Get-RelativePath -BasePath $DocfxRoot -Path $_.FullName
            $parts = $relative -split '[\\/]'
            $parts -notcontains '_site' -and
                $parts -notcontains 'api' -and
                $parts -notcontains 'obj'
        }
}

function Test-GeneratedPages {
    param(
        [Parameter(Mandatory = $true)] [string]$DocfxRoot,
        [Parameter(Mandatory = $true)] [string]$SiteRoot
    )

    foreach ($file in Get-MarkdownFiles -DocfxRoot $DocfxRoot) {
        $relative = Get-RelativePath -BasePath $DocfxRoot -Path $file.FullName
        $expectedRelative = [System.IO.Path]::ChangeExtension($relative, '.html')
        $expectedHtml = Join-RelativePath -Root $SiteRoot -RelativePath $expectedRelative
        if (-not (Test-Path -LiteralPath $expectedHtml -PathType Leaf)) {
            Add-ValidationFailure "Missing generated DocFX page: $expectedRelative"
        }
    }
}

function Test-ZhTwParity {
    param([Parameter(Mandatory = $true)] [string]$DocfxRoot)

    $files = @(Get-MarkdownFiles -DocfxRoot $DocfxRoot)
    foreach ($file in $files) {
        $relative = Get-RelativePath -BasePath $DocfxRoot -Path $file.FullName
        if ($relative.StartsWith('zh-tw/', [System.StringComparison]::OrdinalIgnoreCase)) {
            $englishRelative = $relative.Substring('zh-tw/'.Length)
            $englishPath = Join-RelativePath -Root $DocfxRoot -RelativePath $englishRelative
            if (-not (Test-Path -LiteralPath $englishPath -PathType Leaf)) {
                Add-ValidationFailure "Missing English DocFX page for zh-TW page: $englishRelative"
            }
        }
        else {
            $zhTwPath = Join-RelativePath -Root $DocfxRoot -RelativePath "zh-tw/$relative"
            if (-not (Test-Path -LiteralPath $zhTwPath -PathType Leaf)) {
                Add-ValidationFailure "Missing zh-TW DocFX page: $relative"
            }
        }
    }
}

function Split-Href {
    param([Parameter(Mandatory = $true)] [string]$Href)

    $withoutQuery = $Href
    $queryIndex = $withoutQuery.IndexOf('?')
    if ($queryIndex -ge 0) {
        $withoutQuery = $withoutQuery.Substring(0, $queryIndex)
    }

    $fragment = ''
    $fragmentIndex = $withoutQuery.IndexOf('#')
    if ($fragmentIndex -ge 0) {
        $fragment = $withoutQuery.Substring($fragmentIndex + 1)
        $withoutQuery = $withoutQuery.Substring(0, $fragmentIndex)
    }

    return [pscustomobject]@{
        Path = $withoutQuery
        Fragment = $fragment
    }
}

function Test-ExternalHref {
    param([Parameter(Mandatory = $true)] [string]$Href)

    return $Href.StartsWith('http:', [System.StringComparison]::OrdinalIgnoreCase) -or
        $Href.StartsWith('https:', [System.StringComparison]::OrdinalIgnoreCase) -or
        $Href.StartsWith('mailto:', [System.StringComparison]::OrdinalIgnoreCase) -or
        $Href.StartsWith('tel:', [System.StringComparison]::OrdinalIgnoreCase) -or
        $Href.StartsWith('javascript:', [System.StringComparison]::OrdinalIgnoreCase) -or
        $Href.StartsWith('data:', [System.StringComparison]::OrdinalIgnoreCase) -or
        $Href.StartsWith('//', [System.StringComparison]::Ordinal)
}

function Test-HtmlAnchorExists {
    param(
        [Parameter(Mandatory = $true)] [string]$Html,
        [Parameter(Mandatory = $true)] [string]$Fragment
    )

    $decoded = [System.Uri]::UnescapeDataString($Fragment)
    $candidates = @($Fragment, $decoded, $decoded.ToLowerInvariant()) | Select-Object -Unique
    foreach ($candidate in $candidates) {
        $escaped = [regex]::Escape($candidate)
        if ($Html -match "\s(id|name)\s*=\s*['""]$escaped['""]") {
            return $true
        }
    }

    return $false
}

function Test-GeneratedLinks {
    param([Parameter(Mandatory = $true)] [string]$SiteRoot)

    $htmlFiles = @(Get-ChildItem -LiteralPath $SiteRoot -Recurse -File -Filter '*.html' |
        Where-Object {
            $relative = Get-RelativePath -BasePath $SiteRoot -Path $_.FullName
            -not $relative.StartsWith('api/', [System.StringComparison]::OrdinalIgnoreCase)
        })
    foreach ($htmlFile in $htmlFiles) {
        $html = Get-Content -LiteralPath $htmlFile.FullName -Raw
        $matches = [regex]::Matches($html, "href\s*=\s*['""](?<href>[^'""]+)['""]", 'IgnoreCase')
        foreach ($match in $matches) {
            $href = $match.Groups['href'].Value.Trim()
            if ([string]::IsNullOrWhiteSpace($href) -or (Test-ExternalHref -Href $href)) {
                continue
            }

            $split = Split-Href -Href $href
            if ([string]::IsNullOrWhiteSpace($split.Path) -and
                $split.Fragment.Equals('top', [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $targetPath = if ([string]::IsNullOrWhiteSpace($split.Path)) {
                $htmlFile.FullName
            }
            else {
                $decodedPath = [System.Uri]::UnescapeDataString($split.Path)
                $basePath = $htmlFile.DirectoryName
                if ($decodedPath.StartsWith('/', [System.StringComparison]::Ordinal) -or
                    $decodedPath.StartsWith('\', [System.StringComparison]::Ordinal)) {
                    $basePath = $SiteRoot
                    $decodedPath = $decodedPath -replace '^[\\/]+', ''
                }

                if ($decodedPath.EndsWith('/', [System.StringComparison]::Ordinal) -or
                    $decodedPath.EndsWith('\', [System.StringComparison]::Ordinal)) {
                    $decodedPath = "${decodedPath}index.html"
                }

                [System.IO.Path]::GetFullPath((Join-RelativePath -Root $basePath -RelativePath $decodedPath))
            }

            if (-not (Test-IsUnderDirectory -Root $SiteRoot -Path $targetPath)) {
                Add-ValidationFailure "Broken internal link escapes DocFX site root: $href in $($htmlFile.FullName)"
                continue
            }

            if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
                Add-ValidationFailure "Broken internal link target missing: $href in $($htmlFile.FullName)"
                continue
            }

            if (-not [string]::IsNullOrWhiteSpace($split.Fragment) -and
                [System.IO.Path]::GetExtension($targetPath).Equals('.html', [System.StringComparison]::OrdinalIgnoreCase)) {
                $targetHtml = Get-Content -LiteralPath $targetPath -Raw
                if (-not (Test-HtmlAnchorExists -Html $targetHtml -Fragment $split.Fragment)) {
                    Add-ValidationFailure "Broken internal link anchor missing: $href in $($htmlFile.FullName)"
                }
            }
        }
    }
}

function Get-RegisteredToolNames {
    param([Parameter(Mandatory = $true)] [string]$RepoRoot)

    $toolsRoot = Join-PathMany @($RepoRoot, 'src', 'WpfDevTools.Mcp.Server', 'McpTools')
    if (-not (Test-Path -LiteralPath $toolsRoot -PathType Container)) {
        return @()
    }

    $names = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem -LiteralPath $toolsRoot -Recurse -File -Filter '*.cs') {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($match in [regex]::Matches($content, 'McpServerTool\s*\(\s*Name\s*=\s*"(?<name>[a-z0-9_]+)"')) {
            [void]$names.Add($match.Groups['name'].Value)
        }
    }

    return @($names | Sort-Object)
}

function Test-ToolReferenceCoverage {
    param(
        [Parameter(Mandatory = $true)] [string]$DocfxRoot,
        [Parameter(Mandatory = $true)] [string]$SiteRoot,
        [Parameter(Mandatory = $true)] [string[]]$ToolNames
    )

    if ($ToolNames.Count -eq 0) {
        Add-ValidationFailure 'No MCP tool names were discovered for DocFX tool coverage validation.'
        return
    }

    $englishToolHtml = Get-ChildItem -LiteralPath (Join-PathMany @($SiteRoot, 'reference', 'tools')) -File -Filter '*.html' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
    $zhTwToolHtml = Get-ChildItem -LiteralPath (Join-PathMany @($SiteRoot, 'zh-tw', 'reference', 'tools')) -File -Filter '*.html' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
    $englishText = [string]::Join("`n", @($englishToolHtml))
    $zhTwText = [string]::Join("`n", @($zhTwToolHtml))

    foreach ($toolName in $ToolNames) {
        if ($englishText.IndexOf($toolName, [System.StringComparison]::Ordinal) -lt 0) {
            Add-ValidationFailure "Missing generated English tool reference: $toolName"
        }

        if ($zhTwText.IndexOf($toolName, [System.StringComparison]::Ordinal) -lt 0) {
            Add-ValidationFailure "Missing generated zh-TW tool reference: $toolName"
        }
    }

    $sourceSet = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::Ordinal)
    foreach ($toolName in $ToolNames) {
        [void]$sourceSet.Add($toolName)
    }

    foreach ($referenceRoot in @((Join-PathMany @($DocfxRoot, 'reference', 'tools')), (Join-PathMany @($DocfxRoot, 'zh-tw', 'reference', 'tools')))) {
        foreach ($file in Get-ChildItem -LiteralPath $referenceRoot -File -Filter '*.md' -ErrorAction SilentlyContinue) {
            $content = Get-Content -LiteralPath $file.FullName -Raw
            foreach ($match in [regex]::Matches($content, '(?m)^\s*-\s*`(?<name>[a-z][a-z0-9_]*)`\s*$')) {
                $listedName = $match.Groups['name'].Value
                if (-not $sourceSet.Contains($listedName)) {
                    Add-ValidationFailure "Stale tool reference in $($file.FullName): $listedName"
                }
            }
        }
    }
}

$repoRootFull = [System.IO.Path]::GetFullPath($RepoRoot)
$docfxRoot = Join-Path $repoRootFull 'docfx'
$siteRoot = Join-Path $docfxRoot '_site'

if (-not (Test-Path -LiteralPath $docfxRoot -PathType Container)) {
    Add-ValidationFailure "DocFX root does not exist: $docfxRoot"
}
if (-not (Test-Path -LiteralPath $siteRoot -PathType Container)) {
    Add-ValidationFailure "DocFX site output does not exist: $siteRoot"
}

if ($script:Failures.Count -eq 0) {
    Test-ZhTwParity -DocfxRoot $docfxRoot
    Test-GeneratedPages -DocfxRoot $docfxRoot -SiteRoot $siteRoot
    Test-GeneratedLinks -SiteRoot $siteRoot
    Test-ToolReferenceCoverage -DocfxRoot $docfxRoot -SiteRoot $siteRoot -ToolNames (Get-RegisteredToolNames -RepoRoot $repoRootFull)
}

if ($script:Failures.Count -gt 0) {
    foreach ($failure in $script:Failures) {
        Write-Host "ERROR: $failure"
    }

    exit 1
}

Write-Host 'DocFX documentation validation passed.'
