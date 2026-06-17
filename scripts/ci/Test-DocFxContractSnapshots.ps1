function ConvertTo-DocFxComparableText {
    param([Parameter(Mandatory = $true)] [string]$Content)

    $withoutTags = [regex]::Replace($Content, '<[^>]+>', ' ')
    return [System.Net.WebUtility]::HtmlDecode($withoutTags)
}

function Get-DocFxContractSnapshotHashes {
    param([Parameter(Mandatory = $true)] [string]$Content)

    $text = ConvertTo-DocFxComparableText -Content $Content
    $hashes = @{}
    foreach ($resourceUri in @('wpf://contracts/tools', 'wpf://contracts/response')) {
        $pattern = [regex]::Escape($resourceUri) + '[^\r\n]{0,160}?SHA-256[^a-fA-F0-9]{0,32}(?<hash>[a-fA-F0-9]{64})'
        $match = [regex]::Match($text, $pattern, 'IgnoreCase, CultureInvariant')
        if ($match.Success) {
            $hashes[$resourceUri] = $match.Groups['hash'].Value.ToLowerInvariant()
        }
    }

    return $hashes
}

function Test-DocFxGeneratedContractSnapshots {
    param(
        [Parameter(Mandatory = $true)] [string]$DocfxRoot,
        [Parameter(Mandatory = $true)] [string]$SiteRoot
    )

    $toolOverviewPages = @(
        [pscustomobject]@{
            Markdown = 'reference/tools/index.md'
            Html = 'reference/tools/index.html'
        },
        [pscustomobject]@{
            Markdown = 'zh-tw/reference/tools/index.md'
            Html = 'zh-tw/reference/tools/index.html'
        }
    )

    foreach ($page in $toolOverviewPages) {
        $markdownPath = Join-RelativePath -Root $DocfxRoot -RelativePath $page.Markdown
        if (-not (Test-Path -LiteralPath $markdownPath -PathType Leaf)) {
            continue
        }

        $sourceHashes = Get-DocFxContractSnapshotHashes -Content (Get-Content -LiteralPath $markdownPath -Raw)
        if ($sourceHashes.Count -eq 0) {
            continue
        }

        $htmlPath = Join-RelativePath -Root $SiteRoot -RelativePath $page.Html
        if (-not (Test-Path -LiteralPath $htmlPath -PathType Leaf)) {
            Add-ValidationFailure "Missing generated contract snapshot page: $($page.Html)"
            continue
        }

        $generatedHashes = Get-DocFxContractSnapshotHashes -Content (Get-Content -LiteralPath $htmlPath -Raw)
        foreach ($resourceUri in $sourceHashes.Keys) {
            if (-not $generatedHashes.ContainsKey($resourceUri)) {
                Add-ValidationFailure "Missing generated contract snapshot for $resourceUri in $($page.Html)"
                continue
            }

            if ($generatedHashes[$resourceUri] -ne $sourceHashes[$resourceUri]) {
                Add-ValidationFailure "Stale generated contract snapshot for $resourceUri in $($page.Html): expected $($sourceHashes[$resourceUri]) from $($page.Markdown) but found $($generatedHashes[$resourceUri])"
            }
        }
    }
}
