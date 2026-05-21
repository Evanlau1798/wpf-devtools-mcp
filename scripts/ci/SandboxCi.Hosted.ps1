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
        [Parameter(Mandatory = $true)] [ValidateSet('x64', 'Win32')] [string]$Platform
    )

    $runtimeId = if ($Platform -eq 'Win32') { 'win-x86' } else { 'win-x64' }
    $nativeHostDirectory = Resolve-DotNetNativeHostDirectory -RuntimeId $runtimeId
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
        [int]$TimeoutSeconds = 1800
    )

    Invoke-ExternalWithTimeout $Name 'powershell.exe' @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-Command',
        $Command
    ) -TimeoutSeconds $TimeoutSeconds -OutputRoot $OutputRoot -Timestamp $Timestamp
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
        '-c',
        'Debug',
        '--no-restore',
        '-m:1',
        '-nodeReuse:false',
        '-p:UseSharedCompilation=false'
    )

    Invoke-ExternalWithTimeout 'Run tests with coverage' $DotNetPath @(
        'test',
        'tests\WpfDevTools.Tests.Unit\WpfDevTools.Tests.Unit.csproj',
        '-c',
        'Debug',
        '--no-build',
        '--settings', 'coverlet.runsettings',
        '--collect',
        'XPlat Code Coverage',
        '--results-directory',
        (Join-Path $ResultsRoot 'coverage'),
        '-nodeReuse:false',
        '-p:UseSharedCompilation=false'
    ) -TimeoutSeconds 3600 -OutputRoot $OutputRoot -Timestamp $Timestamp
}

function Invoke-HostedReleasePackagingSmoke {
    param(
        [Parameter(Mandatory = $true)] [string]$OutputRoot,
        [Parameter(Mandatory = $true)] [string]$Timestamp,
        [Parameter(Mandatory = $true)] [ValidateSet('x64', 'x86')] [string]$Architecture
    )

    $releaseRoot = Join-Path $OutputRoot "artifacts\release-$Timestamp-$Architecture"
    $installSmokeRoot = Join-Path $OutputRoot "tmp-release-install-smoke-$Architecture"
    $bootstrapSmokeRoot = Join-Path $OutputRoot "tmp-release-bootstrap-smoke-$Architecture"
    Remove-Item -LiteralPath $releaseRoot, $installSmokeRoot, $bootstrapSmokeRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

    Invoke-ExternalWithTimeout "Run release packaging smoke test $Architecture" 'powershell.exe' @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        'scripts\tools\packaging\Publish-Release.ps1',
        '-Architectures',
        $Architecture,
        '-OutputRoot',
        $releaseRoot
    ) -TimeoutSeconds 3600 -OutputRoot $OutputRoot -Timestamp $Timestamp

    $packageArchive = Get-ChildItem -LiteralPath $releaseRoot -File -Filter "release_*_win-$Architecture.zip" |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if ($null -eq $packageArchive) {
        throw "Could not locate packaged release archive for architecture $Architecture"
    }

    $packageDir = Join-Path $releaseRoot $packageArchive.BaseName
    Remove-Item -LiteralPath $packageDir -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive -LiteralPath $packageArchive.FullName -DestinationPath $packageDir -Force

    $installScript = Join-Path $packageDir 'bin\install.ps1'
    $installRootLiteral = ConvertTo-HostedSingleQuotedPowerShellLiteral -Value $installSmokeRoot
    $installScriptLiteral = ConvertTo-HostedSingleQuotedPowerShellLiteral -Value $installScript
    Invoke-HostedPowerShellCommand "Install published package smoke test $Architecture" "`$script:WpfDevToolsInstallerTestModeHarnessEnabled = `$true; `$script:WpfDevToolsInstallerTestModeEnabled = `$true; . $installScriptLiteral -InstallRoot $installRootLiteral -Client other -NonInteractive -Force -OutputJson" $OutputRoot $Timestamp

    if ($Architecture -eq 'x64') {
        $serverPath = Join-Path $installSmokeRoot "$Architecture\current\bin\wpf-devtools-$Architecture.exe"
        Invoke-ExternalWithTimeout "Start installed package runtime smoke test $Architecture" 'powershell.exe' @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            'scripts\tools\packaging\Test-PackagedServerRuntime.ps1',
            '-ServerPath',
            $serverPath
        ) -TimeoutSeconds 300 -OutputRoot $OutputRoot -Timestamp $Timestamp
    }
    else {
        Write-Host "Skipping packaged server runtime smoke test for x86; GitHub's hosted x64 lane only install/uninstall smokes the x86 package layout."
    }

    $archiveLiteral = ConvertTo-HostedSingleQuotedPowerShellLiteral -Value $packageArchive.FullName
    $releaseRootLiteral = ConvertTo-HostedSingleQuotedPowerShellLiteral -Value $releaseRoot
    $bootstrapRootLiteral = ConvertTo-HostedSingleQuotedPowerShellLiteral -Value $bootstrapSmokeRoot
    Invoke-HostedPowerShellCommand "Online installer smoke test $Architecture" "`$script:WpfDevToolsInstallerTestModeHarnessEnabled = `$true; `$script:WpfDevToolsInstallerTestModeEnabled = `$true; . .\scripts\online-installer.ps1 -PackageArchivePath $archiveLiteral -TrustedReleaseMetadataDirectory $releaseRootLiteral -InstallRoot $bootstrapRootLiteral -Client other -NonInteractive -Force -OutputJson" $OutputRoot $Timestamp

    if ($Architecture -eq 'x64') {
        $bootstrapServerPath = Join-Path $bootstrapSmokeRoot "$Architecture\current\bin\wpf-devtools-$Architecture.exe"
        Invoke-ExternalWithTimeout "Start online-installed runtime smoke test $Architecture" 'powershell.exe' @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            'scripts\tools\packaging\Test-PackagedServerRuntime.ps1',
            '-ServerPath',
            $bootstrapServerPath
        ) -TimeoutSeconds 300 -OutputRoot $OutputRoot -Timestamp $Timestamp
    }
    else {
        Write-Host "Skipping online-installed runtime smoke test for x86; GitHub's hosted x64 lane only install/uninstall smokes the x86 package layout."
    }

    $installedScript = Join-Path $installSmokeRoot "$Architecture\current\bin\install.ps1"
    $installedScriptLiteral = ConvertTo-HostedSingleQuotedPowerShellLiteral -Value $installedScript
    Invoke-HostedPowerShellCommand "Uninstall published package smoke test $Architecture" "`$script:WpfDevToolsInstallerTestModeHarnessEnabled = `$true; `$script:WpfDevToolsInstallerTestModeEnabled = `$true; . $installedScriptLiteral -Action uninstall -InstallRoot $installRootLiteral -Architecture '$Architecture' -Client other -NonInteractive -OutputJson" $OutputRoot $Timestamp

    $bootstrapScript = Join-Path $bootstrapSmokeRoot "$Architecture\current\bin\install.ps1"
    $bootstrapScriptLiteral = ConvertTo-HostedSingleQuotedPowerShellLiteral -Value $bootstrapScript
    Invoke-HostedPowerShellCommand "Uninstall online installer smoke test $Architecture" "`$script:WpfDevToolsInstallerTestModeHarnessEnabled = `$true; `$script:WpfDevToolsInstallerTestModeEnabled = `$true; . $bootstrapScriptLiteral -Action uninstall -InstallRoot $bootstrapRootLiteral -Architecture '$Architecture' -Client other -NonInteractive -OutputJson" $OutputRoot $Timestamp
}

function Invoke-HostedNuGetPack {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$OutputRoot,
        [Parameter(Mandatory = $true)] [string]$Timestamp
    )

    $packageRoot = Join-Path $OutputRoot "nupkg-$Timestamp"
    Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null

    Invoke-External 'dotnet pack src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj' $DotNetPath @(
        'pack',
        'src\WpfDevTools.Inspector.Sdk\WpfDevTools.Inspector.Sdk.csproj',
        '--configuration',
        'Release',
        '--output',
        $packageRoot,
        '-p:GeneratePackageOnBuild=false'
    )

    $package = Get-ChildItem -LiteralPath $packageRoot -File -Filter '*.nupkg' |
        Select-Object -First 1
    if ($null -eq $package) {
        throw "NuGet package was not produced under $packageRoot."
    }
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

        foreach ($configuration in @('Debug', 'Release')) {
            foreach ($platform in @('x64', 'x86')) {
                $nativePlatform = if ($platform -eq 'x86') { 'Win32' } else { 'x64' }
                Invoke-HostedNativeBootstrapperBuild -Configuration $configuration -Platform $nativePlatform

                Invoke-External "Build solution $configuration $platform" $DotNetPath @(
                    'build',
                    '--configuration', $configuration,
                    '--no-restore',
                    '-m:1',
                    "-p:Platform=$platform",
                    '-nodeReuse:false',
                    '-p:UseSharedCompilation=false'
                )

                if ($platform -ne 'x64') {
                    continue
                }

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

        Invoke-HostedCoverageVerification -DotNetPath $DotNetPath -ResultsRoot $ResultsRoot -OutputRoot $OutputRoot -Timestamp $Timestamp
        Invoke-HostedReleasePackagingSmoke -Architecture 'x64' -OutputRoot $OutputRoot -Timestamp $Timestamp
        Invoke-HostedReleasePackagingSmoke -Architecture 'x86' -OutputRoot $OutputRoot -Timestamp $Timestamp
        Invoke-HostedNuGetPack -DotNetPath $DotNetPath -OutputRoot $OutputRoot -Timestamp $Timestamp
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
