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

function Invoke-UnitDebugTests {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$ResultsRoot
    )

    Invoke-ExternalWithTimeout 'Run unit tests Debug' $DotNetPath @(
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
    ) -TimeoutSeconds 3600 -OutputRoot $MappedOutputRoot -Timestamp $timestamp
}

function Invoke-ReleaseUnitTests {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$ResultsRoot
    )

    Invoke-ExternalWithTimeout 'Run release unit tests Debug' $DotNetPath @(
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
        'trx;LogFileName=release-unit-debug.trx',
        '--results-directory',
        (Join-Path $ResultsRoot 'Debug\release-unit')
    ) -TimeoutSeconds 3600 -OutputRoot $MappedOutputRoot -Timestamp $timestamp
}
