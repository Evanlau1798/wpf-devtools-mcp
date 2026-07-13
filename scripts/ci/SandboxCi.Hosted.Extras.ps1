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

function Invoke-HostedComposerCapabilityTests {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$ResultsRoot,
        [Parameter(Mandatory = $true)] [string]$OutputRoot,
        [Parameter(Mandatory = $true)] [string]$Timestamp,
        [Parameter(Mandatory = $true)] [ValidateSet('Debug', 'Release')] [string]$Configuration
    )

    Invoke-ExternalWithTimeout 'Run Composer compile and runtime tests' $DotNetPath @(
        'test',
        'tests\WpfDevTools.Tests.Unit\WpfDevTools.Tests.Unit.csproj',
        '--configuration', $Configuration,
        '--no-build',
        '--filter', 'Category=ComposerCompile|Category=ComposerRuntime',
        '--verbosity', 'normal',
        '--blame-hang-timeout', '10m',
        '--logger', "trx;LogFileName=composer-$($Configuration.ToLowerInvariant()).trx",
        '--results-directory', (Join-Path $ResultsRoot "$Configuration\composer")
    ) -TimeoutSeconds 600 -OutputRoot $OutputRoot -Timestamp $Timestamp
}

function Invoke-HostedCoverageVerification {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$ResultsRoot,
        [Parameter(Mandatory = $true)] [string]$OutputRoot,
        [Parameter(Mandatory = $true)] [string]$Timestamp
    )

    Invoke-External 'Build unit tests for coverage' $DotNetPath @(
        'build',
        'tests\WpfDevTools.Tests.Unit\WpfDevTools.Tests.Unit.csproj',
        '-c', 'Debug',
        '--no-restore',
        '-m:1',
        '-nodeReuse:false',
        '-p:UseSharedCompilation=false'
    )

    Invoke-ExternalWithTimeout 'Run tests with coverage' $DotNetPath @(
        'test',
        'tests\WpfDevTools.Tests.Unit\WpfDevTools.Tests.Unit.csproj',
        '-c', 'Debug',
        '--no-build',
        '--settings', 'coverlet.runsettings',
        '--collect', 'XPlat Code Coverage',
        '--blame-hang-timeout', '10m',
        '--logger', 'trx;LogFileName=coverage-debug.trx',
        '--results-directory', (Join-Path $ResultsRoot 'coverage'),
        '--filter', 'FullyQualifiedName!~WpfDevTools.Tests.Unit.Release&FullyQualifiedName!~WpfDevTools.Tests.Unit.Documentation&Category!=ComposerCompile&Category!=ComposerRuntime&Category!=ComposerAcceptance',
        '-nodeReuse:false',
        '-p:UseSharedCompilation=false'
    ) -TimeoutSeconds 3600 -OutputRoot $OutputRoot -Timestamp $Timestamp
}

function Invoke-HostedWindowsX64FastVerification {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$ResultsRoot,
        [Parameter(Mandatory = $true)] [string]$OutputRoot,
        [Parameter(Mandatory = $true)] [string]$Timestamp,
        [ValidateRange(1, 8)] [int]$MaxParallelLanes = 4,
        [ValidateScript({
            if ($_ -eq 1 -or $_ -eq 4) {
                return $true
            }

            throw 'UnitDebugShardCount currently supports 1 or 4.'
        })]
        [int]$UnitDebugShardCount = 1,
        [ValidateScript({
            if ($_ -eq 1 -or $_ -eq 4 -or $_ -eq 8) {
                return $true
            }

            throw 'ReleaseUnitShardCount currently supports 1, 4, or 8.'
        })]
        [int]$ReleaseUnitShardCount = 8
    )

    $previousTimeoutScale = $env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE
    $hostedManagedMaxParallelLanes = [Math]::Min($MaxParallelLanes, 4)
    $composerCapabilityExclusionFilter = 'Category!=ComposerCompile&Category!=ComposerRuntime&Category!=ComposerAcceptance'
    $env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE = '4'
    try {
        Invoke-External 'dotnet restore --locked-mode' $DotNetPath @('restore', '--locked-mode', '-p:NuGetAudit=true')
        Invoke-External 'Restore server runtime dependencies win-x64' $DotNetPath @(
            'restore',
            'src\WpfDevTools.Mcp.Server\WpfDevTools.Mcp.Server.csproj',
            '--locked-mode',
            '-r',
            'win-x64',
            '-p:NuGetAudit=true'
        )
        Invoke-HostedSecurityScanEquivalence -DotNetPath $DotNetPath
        Invoke-HostedNativeBootstrapperBuild -Configuration 'Debug' -Platform 'x64'
        Invoke-External 'Build solution Debug x64' $DotNetPath @(
            'build',
            '--configuration', 'Debug',
            '--no-restore',
            '-m:1',
            '-p:Platform=x64',
            '-nodeReuse:false',
            '-p:UseSharedCompilation=false'
        )
        Invoke-UnitDebugTests -DotNetPath $DotNetPath -ResultsRoot $ResultsRoot -Configuration 'Debug' -MaxParallelLanes $hostedManagedMaxParallelLanes -UnitDebugShardCount $UnitDebugShardCount -Filter $composerCapabilityExclusionFilter
        Invoke-HostedComposerCapabilityTests -DotNetPath $DotNetPath -ResultsRoot $ResultsRoot -OutputRoot $OutputRoot -Timestamp $Timestamp -Configuration 'Debug'
        Invoke-ManagedTestLanes -DotNetPath $DotNetPath -ResultsRoot $ResultsRoot -Configuration 'Debug' -MaxParallelLanes $hostedManagedMaxParallelLanes -ReleaseUnitShardCount $ReleaseUnitShardCount -IncludeReleaseUnit
        Invoke-HostedServerRuntimeBuild -DotNetPath $DotNetPath -Configuration 'Debug'
        Invoke-External 'Run integration tests Debug' $DotNetPath @(
            'test',
            'tests\WpfDevTools.Tests.Integration\WpfDevTools.Tests.Integration.csproj',
            '--configuration', 'Debug',
            '--no-build',
            '--verbosity', 'normal',
            '--blame-hang-timeout', '10m',
            '--logger', 'trx;LogFileName=integration-debug.trx',
            '--results-directory', (Join-Path $ResultsRoot 'Debug\integration')
        )
    }
    finally {
        if ($null -eq $previousTimeoutScale) {
            Remove-Item Env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE -ErrorAction SilentlyContinue
        }
        else {
            $env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE = $previousTimeoutScale
        }
    }
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

function Invoke-HostedPackagedRuntimeLiveSmoke {
    param(
        [Parameter(Mandatory = $true)] [ValidateSet('installed', 'online-installed')] [string]$InstallKind,
        [Parameter(Mandatory = $true)] [string]$ServerPath,
        [Parameter(Mandatory = $true)] [ValidateSet('x64', 'x86', 'arm64')] [string]$Architecture,
        [Parameter(Mandatory = $true)] [string]$OutputRoot,
        [Parameter(Mandatory = $true)] [string]$Timestamp
    )

    $smokeName = switch ($InstallKind) {
        'installed' { "Start target-aware live-injection installed package runtime smoke test $Architecture" }
        'online-installed' { "Start target-aware live-injection online-installed runtime smoke test $Architecture" }
    }

    $environment = @{}
    if ($Architecture -eq 'x86') {
        $x86DotNetRoot = $env:WPFDEVTOOLS_HOSTED_X86_DOTNET_ROOT
        if ([string]::IsNullOrWhiteSpace($x86DotNetRoot) -or -not (Test-Path -LiteralPath (Join-Path $x86DotNetRoot 'dotnet.exe'))) {
            throw 'x86 package runtime smoke requires WPFDEVTOOLS_HOSTED_X86_DOTNET_ROOT to point at an x86 dotnet installation.'
        }

        $environment = @{
            DOTNET_ROOT = $x86DotNetRoot
            PATH = "$x86DotNetRoot;$env:PATH"
        }
    }

    $scriptLiteral = ConvertTo-HostedSingleQuotedPowerShellLiteral -Value 'scripts\tools\packaging\Invoke-PackagedRuntimeLiveSmoke.ps1'
    $serverPathLiteral = ConvertTo-HostedSingleQuotedPowerShellLiteral -Value $ServerPath
    Invoke-HostedPowerShellCommand `
        -Name $smokeName `
        -Command "& $scriptLiteral -ServerPath $serverPathLiteral -Architecture '$Architecture'" `
        -OutputRoot $OutputRoot `
        -Timestamp $Timestamp `
        -Environment $environment `
        -TimeoutSeconds 600
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
