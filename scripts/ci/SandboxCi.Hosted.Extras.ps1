function Invoke-HostedArm64Build {
    param([Parameter(Mandatory = $true)] [string]$DotNetPath)

    Invoke-External 'Build ARM64 Release cross-compile' $DotNetPath @(
        'build',
        '--configuration',
        'Release',
        '-p:Platform=ARM64',
        '-m:1',
        '-nodeReuse:false',
        '-p:UseSharedCompilation=false'
    )
}

function Invoke-HostedDocsPagesBuild {
    param([Parameter(Mandatory = $true)] [string]$DotNetPath)

    Invoke-External 'Restore local tools for docs' $DotNetPath @(
        'tool', 'restore'
    )

    Invoke-External 'Restore docs project dependencies' $DotNetPath @(
        'restore', 'WpfDevTools.sln', '--locked-mode'
    )

    Invoke-External 'Build shared assembly for API docs' $DotNetPath @(
        'build',
        'src\WpfDevTools.Shared\WpfDevTools.Shared.csproj',
        '-c',
        'Debug',
        '-f',
        'net8.0'
    )

    Invoke-External 'Build SDK assembly for API docs' $DotNetPath @(
        'build',
        'src\WpfDevTools.Inspector.Sdk\WpfDevTools.Inspector.Sdk.csproj',
        '-c',
        'Debug',
        '-f',
        'net8.0-windows'
    )

    Invoke-External 'Build DocFX site' $DotNetPath @(
        'tool', 'run', 'docfx', 'docfx/docfx.json'
    )

    Invoke-External 'Validate DocFX links and parity' 'powershell.exe' @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        'scripts\ci\Test-DocFxDocumentation.ps1',
        '-RepoRoot',
        (Get-Location).ProviderPath
    )
}

function ConvertTo-HostedSingleQuotedPowerShellLiteral {
    param([Parameter(Mandatory = $true)] [string]$Value)

    return "'" + $Value.Replace("'", "''") + "'"
}

function Invoke-HostedPowerShellCommand {
    param(
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [string]$Command,
        [Parameter(Mandatory = $true)] [string]$OutputRoot,
        [Parameter(Mandatory = $true)] [string]$Timestamp,
        [hashtable]$Environment = @{},
        [int]$TimeoutSeconds = 1800
    )

    $environmentPrefix = ''
    foreach ($entry in $Environment.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.Value)) {
            continue
        }

        $literal = ConvertTo-HostedSingleQuotedPowerShellLiteral -Value ([string]$entry.Value)
        $environmentPrefix += "`$env:$($entry.Key) = $literal; "
    }

    Invoke-ExternalWithTimeout $Name 'powershell.exe' @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-Command',
        ($environmentPrefix + $Command)
    ) -TimeoutSeconds $TimeoutSeconds -OutputRoot $OutputRoot -Timestamp $Timestamp
}

function Invoke-HostedSdkPackageSmoke {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$PackagePath,
        [Parameter(Mandatory = $true)] [string]$PackageRoot,
        [Parameter(Mandatory = $true)] [string]$OutputRoot,
        [Parameter(Mandatory = $true)] [string]$Timestamp
    )

    Write-Host ""
    Write-Host ">>> Inspect SDK package contents"

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entries = $archive.Entries | ForEach-Object { $_.FullName }
        $requiredEntries = @(
            'lib/net8.0-windows7.0/WpfDevTools.Inspector.dll',
            'lib/net8.0-windows7.0/WpfDevTools.Shared.dll',
            'lib/net8.0-windows7.0/WpfDevTools.Inspector.Sdk.dll'
        )

        foreach ($requiredEntry in $requiredEntries) {
            if ($entries -notcontains $requiredEntry) {
                throw "SDK package is missing required entry: $requiredEntry"
            }
        }

        $nuspecEntry = $archive.Entries |
            Where-Object { $_.FullName.EndsWith('.nuspec', [System.StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1
        if ($null -eq $nuspecEntry) {
            throw 'SDK package is missing nuspec metadata.'
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
        try {
            $nuspecContent = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        foreach ($internalPackageId in @('WpfDevTools.Inspector', 'WpfDevTools.Shared')) {
            if ($nuspecContent.IndexOf("dependency id=`"$internalPackageId`"", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "SDK package must not depend on unpublished sibling package $internalPackageId."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $packageVersionMatch = [regex]::Match(
        (Split-Path $PackagePath -Leaf),
        '^WpfDevTools\.Inspector\.Sdk\.(?<version>.+)\.nupkg$')
    if (-not $packageVersionMatch.Success) {
        throw "Could not parse SDK package version from $PackagePath."
    }

    $consumerBaseRoot = if ([string]::IsNullOrWhiteSpace($env:TEMP)) { $OutputRoot } else { $env:TEMP }
    $consumerRoot = Join-Path $consumerBaseRoot "wpf-devtools-sdk-consumer-$Timestamp"
    Remove-Item -LiteralPath $consumerRoot -Recurse -Force -ErrorAction SilentlyContinue
    try {
        Invoke-External 'Create SDK package consumer smoke app' $DotNetPath @(
            'new', 'wpf',
            '-n', 'SdkConsumerSmoke',
            '-o', $consumerRoot,
            '--framework', 'net8.0'
        )

        $consumerProject = Join-Path $consumerRoot 'SdkConsumerSmoke.csproj'
        Invoke-External 'Install SDK package into clean consumer' $DotNetPath @(
            'add', $consumerProject,
            'package', 'WpfDevTools.Inspector.Sdk',
            '--version', $packageVersionMatch.Groups['version'].Value,
            '--source', $PackageRoot
        )

        Invoke-External 'Build SDK package clean consumer' $DotNetPath @(
            'build', $consumerProject,
            '-c', 'Release',
            '-m:1',
            '-p:BuildInParallel=false'
        )
    }
    finally {
        Remove-Item -LiteralPath $consumerRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
