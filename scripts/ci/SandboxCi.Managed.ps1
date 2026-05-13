function Install-DotNetSdk {
    param([Parameter(Mandatory = $true)] [string]$RepoRoot)

    $globalJson = Get-Content -LiteralPath (Join-Path $RepoRoot 'global.json') -Raw | ConvertFrom-Json
    $sdkVersion = [string]$globalJson.sdk.version
    if ([string]::IsNullOrWhiteSpace($sdkVersion)) {
        throw 'global.json does not declare sdk.version.'
    }

    $dotnetRoot = Join-Path $MappedWorkRoot '.dotnet'
    $dotnetPath = Join-Path $dotnetRoot 'dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetPath)) {
        New-Item -ItemType Directory -Force -Path $dotnetRoot | Out-Null
        $downloadRoot = Join-Path $MappedWorkRoot 'downloads'
        New-Item -ItemType Directory -Force -Path $downloadRoot | Out-Null
        $installScript = Join-Path $downloadRoot 'dotnet-install.ps1'
        Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript -UseBasicParsing
        Invoke-External "Install .NET SDK $sdkVersion" 'powershell.exe' @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $installScript,
            '-Version',
            $sdkVersion,
            '-InstallDir',
            $dotnetRoot,
            '-NoPath'
        )
    }

    if (-not (Test-Path -LiteralPath $dotnetPath)) {
        throw "dotnet.exe was not installed at expected path: $dotnetPath"
    }

    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = "$dotnetRoot;$env:PATH"
    return $dotnetPath
}

function Invoke-DotNetRestore {
    param([Parameter(Mandatory = $true)] [string]$DotNetPath)

    Invoke-External 'dotnet restore --locked-mode' $DotNetPath @('restore', '--locked-mode')
}

function Invoke-UnitDebugBuild {
    param([Parameter(Mandatory = $true)] [string]$DotNetPath)

    Invoke-External 'Build unit tests Debug' $DotNetPath @(
        'build',
        'tests\WpfDevTools.Tests.Unit\WpfDevTools.Tests.Unit.csproj',
        '--configuration',
        'Debug',
        '--verbosity',
        'minimal',
        '-nodeReuse:false',
        '-p:UseSharedCompilation=false'
    )
}

function Invoke-ReleaseUnitBuild {
    param([Parameter(Mandatory = $true)] [string]$DotNetPath)

    Invoke-External 'Build release unit tests Debug' $DotNetPath @(
        'build',
        'tests\WpfDevTools.Tests.Unit.Release\WpfDevTools.Tests.Unit.Release.csproj',
        '--configuration',
        'Debug',
        '--verbosity',
        'minimal',
        '-nodeReuse:false',
        '-p:UseSharedCompilation=false'
    )
}

function Invoke-McpServerDebugBuild {
    param([Parameter(Mandatory = $true)] [string]$DotNetPath)

    Invoke-External 'Build MCP server Debug' $DotNetPath @(
        'build',
        'src\WpfDevTools.Mcp.Server\WpfDevTools.Mcp.Server.csproj',
        '--configuration',
        'Debug',
        '--verbosity',
        'minimal',
        '-nodeReuse:false',
        '-p:UseSharedCompilation=false'
    )
}

function Invoke-FocusedFlakeTests {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$ResultsRoot
    )

    $filter = 'FullyQualifiedName~InspectorHostConcurrencyTests.Start_WhenStopWinsBeforeRunningState_ShouldFail|FullyQualifiedName~EventAnalyzerTests.TraceRoutedEvents_AfterAutoStop_ShouldUnregisterWindowHandlers'
    for ($attempt = 1; $attempt -le $Repeat; $attempt++) {
        Invoke-ExternalWithTimeout "Run focused GitLab #12 flake tests attempt $attempt" $DotNetPath @(
            'test',
            'tests\WpfDevTools.Tests.Unit\WpfDevTools.Tests.Unit.csproj',
            '--configuration',
            'Debug',
            '--no-build',
            '--no-restore',
            '--verbosity',
            'normal',
            '--filter',
            $filter,
            '--blame-hang-timeout',
            '10m',
            '--logger',
            "trx;LogFileName=focused-flakes-$attempt.trx",
            '--results-directory',
            (Join-Path $ResultsRoot 'focused-flakes')
        ) -TimeoutSeconds 720 -OutputRoot $MappedOutputRoot -Timestamp $timestamp
    }
}

function New-UnitDebugTestCommand {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$ResultsRoot
    )

    return [pscustomobject]@{
        Name           = 'Run unit tests Debug'
        FilePath       = $DotNetPath
        Arguments      = @(
            'test',
            'tests\WpfDevTools.Tests.Unit\WpfDevTools.Tests.Unit.csproj',
            '--configuration',
            'Debug',
            '--no-build',
            '--no-restore',
            '--verbosity',
            'normal',
            '--blame-hang-timeout',
            '10m',
            '--logger',
            'trx;LogFileName=unit-debug.trx',
            '--results-directory',
            (Join-Path $ResultsRoot 'Debug\unit')
        )
        TimeoutSeconds = 3600
    }
}

function New-ReleaseUnitTestCommand {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$ResultsRoot,
        [string]$Name = 'Run release unit tests Debug',
        [string]$LogFileName = 'release-unit-debug.trx',
        [string]$ResultsSubdirectory = 'Debug\release-unit',
        [string]$Filter
    )

    $arguments = @(
        'test',
        'tests\WpfDevTools.Tests.Unit.Release\WpfDevTools.Tests.Unit.Release.csproj',
        '--configuration',
        'Debug',
        '--no-build',
        '--no-restore',
        '--verbosity',
        'normal',
        '--blame-hang-timeout',
        '10m',
        '--logger',
        "trx;LogFileName=$LogFileName",
        '--results-directory',
        (Join-Path $ResultsRoot $ResultsSubdirectory)
    )

    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $arguments += @('--filter', $Filter)
    }

    return [pscustomobject]@{
        Name           = $Name
        FilePath       = $DotNetPath
        Arguments      = $arguments
        TimeoutSeconds = 3600
    }
}

function Get-ReleaseUnitShardFilters {
    return @(
        'FullyQualifiedName~InstallerTui|FullyQualifiedName~InstallerInteractiveUiScriptTests',
        'FullyQualifiedName~InstallerCursor|FullyQualifiedName~InstallerIdeRegistrationShimBackedTests|FullyQualifiedName~InstallerFullUninstallTests|FullyQualifiedName~StandaloneInstallerRegressionBootstrapTests',
        'FullyQualifiedName~InstallerScriptTests|FullyQualifiedName~InstallerBootstrapTests|FullyQualifiedName~InstallerPathSafety|FullyQualifiedName~InstallerProcessLifecycleTests',
        'FullyQualifiedName!~InstallerTui&FullyQualifiedName!~InstallerInteractiveUiScriptTests&FullyQualifiedName!~InstallerCursor&FullyQualifiedName!~InstallerIdeRegistrationShimBackedTests&FullyQualifiedName!~InstallerFullUninstallTests&FullyQualifiedName!~StandaloneInstallerRegressionBootstrapTests&FullyQualifiedName!~InstallerScriptTests&FullyQualifiedName!~InstallerBootstrapTests&FullyQualifiedName!~InstallerPathSafety&FullyQualifiedName!~InstallerProcessLifecycleTests'
    )
}

function New-ReleaseUnitShardCommands {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$ResultsRoot,
        [Parameter(Mandatory = $true)] [int]$ReleaseUnitShardCount
    )

    if ($ReleaseUnitShardCount -eq 1) {
        return @(New-ReleaseUnitTestCommand -DotNetPath $DotNetPath -ResultsRoot $ResultsRoot)
    }

    if ($ReleaseUnitShardCount -ne 4) {
        throw 'ReleaseUnitShardCount currently supports 1 or 4.'
    }

    $filters = Get-ReleaseUnitShardFilters
    $commands = @()
    for ($index = 0; $index -lt $filters.Count; $index++) {
        $shardNumber = $index + 1
        $commands += New-ReleaseUnitTestCommand `
            -DotNetPath $DotNetPath `
            -ResultsRoot $ResultsRoot `
            -Name "Run release unit tests Debug shard $shardNumber" `
            -LogFileName "release-unit-debug-shard-$shardNumber.trx" `
            -ResultsSubdirectory "Debug\release-unit\shard-$shardNumber" `
            -Filter $filters[$index]
    }

    return $commands
}

function Invoke-UnitDebugTests {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$ResultsRoot
    )

    $command = New-UnitDebugTestCommand -DotNetPath $DotNetPath -ResultsRoot $ResultsRoot
    Invoke-ExternalWithTimeout $command.Name $command.FilePath $command.Arguments -TimeoutSeconds $command.TimeoutSeconds -OutputRoot $MappedOutputRoot -Timestamp $timestamp
}

function Invoke-ReleaseUnitTests {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$ResultsRoot
    )

    $command = New-ReleaseUnitTestCommand -DotNetPath $DotNetPath -ResultsRoot $ResultsRoot
    Invoke-ExternalWithTimeout $command.Name $command.FilePath $command.Arguments -TimeoutSeconds $command.TimeoutSeconds -OutputRoot $MappedOutputRoot -Timestamp $timestamp
}

function Invoke-ManagedTestLanes {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$ResultsRoot,
        [ValidateRange(1, 8)] [int]$MaxParallelLanes = 2,
        [ValidateScript({
            if ($_ -eq 1 -or $_ -eq 4) {
                return $true
            }

            throw 'ReleaseUnitShardCount currently supports 1 or 4.'
        })]
        [int]$ReleaseUnitShardCount = 1,
        [switch]$IncludeUnitDebug,
        [switch]$IncludeReleaseUnit
    )

    $commands = @()
    if ($IncludeUnitDebug) {
        $commands += New-UnitDebugTestCommand -DotNetPath $DotNetPath -ResultsRoot $ResultsRoot
    }

    if ($IncludeReleaseUnit) {
        $commands += New-ReleaseUnitShardCommands -DotNetPath $DotNetPath -ResultsRoot $ResultsRoot -ReleaseUnitShardCount $ReleaseUnitShardCount
    }

    if ($commands.Count -eq 0) {
        return
    }

    if (($MaxParallelLanes -le 1) -or ($commands.Count -eq 1)) {
        foreach ($command in $commands) {
            Invoke-ExternalWithTimeout $command.Name $command.FilePath $command.Arguments -TimeoutSeconds $command.TimeoutSeconds -OutputRoot $MappedOutputRoot -Timestamp $timestamp
        }

        return
    }

    $laneCount = [Math]::Min($MaxParallelLanes, $commands.Count)
    Invoke-ExternalBatchWithTimeout `
        -Name 'Run managed test lanes' `
        -Commands $commands `
        -MaxParallelLanes $laneCount `
        -OutputRoot $MappedOutputRoot `
        -Timestamp $timestamp
}
