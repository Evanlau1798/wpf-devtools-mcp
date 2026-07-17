param(
    [string]$RootPath = (Get-Location).Path,
    [string[]]$Path = @()
)

$ErrorActionPreference = 'Stop'

$patterns = [ordered]@{
    'private-key-block' = '-----BEGIN (RSA |DSA |EC |OPENSSH )?PRIVATE KEY-----'
    'github-token' = 'gh[pousr]_[A-Za-z0-9_]{36,}'
    'aws-access-key-id' = 'AKIA[0-9A-Z]{16}'
    'slack-token' = 'xox[baprs]-[A-Za-z0-9-]{20,}'
    'openai-api-key' = 'sk-(proj-)?[A-Za-z0-9_-]{32,}'
    'azure-storage-connection-string' = 'DefaultEndpointsProtocol=https;AccountName=[^;]+;AccountKey=[A-Za-z0-9+/=]{40,}'
}

function Resolve-ScanFiles {
    param(
        [Parameter(Mandatory)] [string]$ResolvedRoot,
        [string[]]$ExplicitPaths
    )

    if ($ExplicitPaths.Count -gt 0) {
        return @($ExplicitPaths |
            ForEach-Object { Resolve-Path -LiteralPath $_ -ErrorAction Stop } |
            ForEach-Object { $_.Path })
    }

    $gitFiles = & git -C $ResolvedRoot ls-files
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed for secret scan root: $ResolvedRoot"
    }

    return @($gitFiles | ForEach-Object { Join-Path $ResolvedRoot $_ })
}

function Get-DisplayPath {
    param(
        [Parameter(Mandatory)] [string]$ResolvedRoot,
        [Parameter(Mandatory)] [string]$FullPath
    )

    $rootPath = [System.IO.Path]::GetFullPath($ResolvedRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $normalizedFullPath = [System.IO.Path]::GetFullPath($FullPath)
    if ($normalizedFullPath.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $normalizedFullPath.Substring($rootPath.Length).Replace('\', '/')
    }

    return [System.IO.Path]::GetFileName($FullPath)
}

$rootFullPath = (Resolve-Path -LiteralPath $RootPath).Path
$findings = [System.Collections.Generic.List[string]]::new()

foreach ($filePath in Resolve-ScanFiles -ResolvedRoot $rootFullPath -ExplicitPaths $Path) {
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        continue
    }

    try {
        $content = Get-Content -LiteralPath $filePath -Raw -ErrorAction Stop
    }
    catch {
        continue
    }

    foreach ($entry in $patterns.GetEnumerator()) {
        if ($content -match $entry.Value) {
            $displayPath = Get-DisplayPath -ResolvedRoot $rootFullPath -FullPath $filePath
            $findings.Add("$displayPath matched $($entry.Key)")
        }
    }
}

if ($findings.Count -gt 0) {
    $findings | Sort-Object | ForEach-Object { [Console]::Error.WriteLine($_) }
    [Console]::Error.Flush()
    exit 1
}
