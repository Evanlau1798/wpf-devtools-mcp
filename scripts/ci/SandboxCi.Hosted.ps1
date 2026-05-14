function Resolve-DotNetNativeHostDirectory {
    param([Parameter(Mandatory = $true)] [string]$RuntimeId)

    $dotnetCommand = Get-Command 'dotnet.exe' -ErrorAction SilentlyContinue
    $candidateRoots = @(
        [string]$env:DOTNET_ROOT
        $(if ($null -ne $dotnetCommand) { Split-Path $dotnetCommand.Source -Parent })
        $(Join-Path $env:ProgramFiles 'dotnet')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($dotnetRoot in $candidateRoots) {
        $hostPackRoot = Join-Path (Join-Path $dotnetRoot 'packs') "Microsoft.NETCore.App.Host.$RuntimeId"
        if (-not (Test-Path -LiteralPath $hostPackRoot)) {
            continue
        }

        $packDir = Get-ChildItem -LiteralPath $hostPackRoot -Directory |
            Sort-Object { [version]$_.Name } -Descending |
            Select-Object -First 1

        if ($null -eq $packDir) {
            continue
        }

        $nativeDirectory = Join-Path $packDir.FullName "runtimes\$RuntimeId\native"
        if (Test-Path -LiteralPath $nativeDirectory) {
            return [string]$nativeDirectory
        }
    }

    throw "Could not locate Microsoft.NETCore.App.Host native directory for $RuntimeId under: $($candidateRoots -join '; ')"
}

function ConvertTo-MSBuildPropertyValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ''
    }

    return $Value.TrimEnd(';') -replace ';', '%3B'
}

function Invoke-HostedNativeBootstrapperBuild {
    param(
        [Parameter(Mandatory = $true)] [ValidateSet('Debug', 'Release')] [string]$Configuration,
        [Parameter(Mandatory = $true)] [ValidateSet('x64')] [string]$Platform
    )

    $nativeHostDirectory = Resolve-DotNetNativeHostDirectory -RuntimeId 'win-x64'
    $msbuildPath = Resolve-MSBuildPath
    $windowsSdkVersion = ''
    $windowsSdkDirectory = ''
    if (-not [string]::IsNullOrWhiteSpace($env:WindowsSDKDir)) {
        $windowsSdkDirectory = $env:WindowsSDKDir.TrimEnd('\')
        $includeRoot = Join-Path $windowsSdkDirectory 'Include'
        $windowsSdkVersion = Get-ChildItem -LiteralPath $includeRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            Select-Object -ExpandProperty Name -First 1
    }

    if ([string]::IsNullOrWhiteSpace($windowsSdkVersion)) {
        throw 'Windows SDK version was not found. HostedWindowsX64 requires Windows SDK headers for native bootstrapper MSBuild.'
    }

    $includePath = ConvertTo-MSBuildPropertyValue -Value $env:INCLUDE
    $libraryPath = ConvertTo-MSBuildPropertyValue -Value $env:LIB
    $executablePath = ConvertTo-MSBuildPropertyValue -Value $env:PATH

    Invoke-External "Build native bootstrapper $Configuration $Platform" $msbuildPath @(
        'src\WpfDevTools.Bootstrapper\WpfDevTools.Bootstrapper.vcxproj',
        '/m',
        '/nologo',
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        '/p:LinkIncremental=false',
        "/p:WindowsSDKDir=$windowsSdkDirectory",
        "/p:WindowsTargetPlatformVersion=$windowsSdkVersion",
        "/p:IncludePath=$includePath",
        "/p:LibraryPath=$libraryPath",
        "/p:ExecutablePath=$executablePath",
        "/p:NetHostIncludeDir=$nativeHostDirectory",
        "/p:NetHostLibDir=$nativeHostDirectory"
    )
}

function Invoke-HostedServerRuntimeBuild {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [ValidateSet('Debug', 'Release')] [string]$Configuration
    )

    Invoke-External "Prepare server runtime output $Configuration win-x64" $DotNetPath @(
        'build',
        'src\WpfDevTools.Mcp.Server\WpfDevTools.Mcp.Server.csproj',
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', 'false',
        '--no-restore',
        '-nodeReuse:false',
        '-p:UseSharedCompilation=false'
    )
}

function Invoke-HostedWindowsX64Verification {
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
        [int]$UnitDebugShardCount = 4,
        [ValidateScript({
            if ($_ -eq 1 -or $_ -eq 4 -or $_ -eq 8) {
                return $true
            }

            throw 'ReleaseUnitShardCount currently supports 1, 4, or 8.'
        })]
        [int]$ReleaseUnitShardCount = 8
    )

    $previousTimeoutScale = $env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE
    $env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE = '1'
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

        foreach ($configuration in @('Debug', 'Release')) {
            Invoke-HostedNativeBootstrapperBuild -Configuration $configuration -Platform 'x64'

            Invoke-External "Build solution $configuration x64" $DotNetPath @(
                'build',
                '--configuration', $configuration,
                '--no-restore',
                '-m:1',
                '-p:Platform=x64',
                '-nodeReuse:false',
                '-p:UseSharedCompilation=false'
            )

            try {
                Invoke-UnitDebugTests -DotNetPath $DotNetPath -ResultsRoot $ResultsRoot -Configuration $configuration -MaxParallelLanes 1 -UnitDebugShardCount $UnitDebugShardCount
                Invoke-ManagedTestLanes -DotNetPath $DotNetPath -ResultsRoot $ResultsRoot -Configuration $configuration -MaxParallelLanes $MaxParallelLanes -ReleaseUnitShardCount $ReleaseUnitShardCount -IncludeReleaseUnit
            }
            catch {
                throw "Managed test lanes $configuration failed: $($_.Exception.Message)"
            }

            if ($configuration -eq 'Debug') {
                Invoke-HostedServerRuntimeBuild -DotNetPath $DotNetPath -Configuration $configuration

                Invoke-External 'Run integration tests Debug' $DotNetPath @(
                    'test',
                    'tests\WpfDevTools.Tests.Integration\WpfDevTools.Tests.Integration.csproj',
                    '--configuration', $configuration,
                    '--no-build',
                    '--verbosity', 'normal',
                    '--blame-hang-timeout', '10m',
                    '--logger', 'trx;LogFileName=integration-debug.trx',
                    '--results-directory', (Join-Path $ResultsRoot 'Debug\integration')
                )
            }
        }
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
