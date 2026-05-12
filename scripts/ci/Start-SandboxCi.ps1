param(
    [ValidateSet('FocusedFlakes', 'UnitDebug', 'UnitRelease', 'FullManaged', 'NativeSmoke', 'NativeFull')]
    [string]$Mode = 'FocusedFlakes',

    [ValidateRange(1, 100)]
    [int]$Repeat = 1,

    [string]$MappedRepoRoot = 'C:\Users\WDAGUtilityAccount\Desktop\wpf-devtools-mcp',

    [string]$MappedWorkRoot = 'C:\Users\WDAGUtilityAccount\Desktop\work',

    [string]$MappedOutputRoot = 'C:\Users\WDAGUtilityAccount\Desktop\output',

    [ValidatePattern('^[A-Za-z0-9_.-]+$')]
    [string]$RunId = (Get-Date -Format 'yyyyMMdd-HHmmss')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SandboxCi.Process.ps1')
. (Join-Path $PSScriptRoot 'SandboxCi.Native.ps1')
. (Join-Path $PSScriptRoot 'SandboxCi.Managed.ps1')

function Assert-SandboxWorkDestination {
    param(
        [Parameter(Mandatory = $true)] [string]$DestinationRoot,
        [Parameter(Mandatory = $true)] [string]$WorkRoot
    )

    $fullDestination = [System.IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\')
    $fullWorkRoot = [System.IO.Path]::GetFullPath($WorkRoot).TrimEnd('\')
    $destinationRoot = [System.IO.Path]::GetPathRoot($fullDestination).TrimEnd('\')
    if ([string]::Equals($fullDestination, $destinationRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "DestinationRoot must not be a drive root: $fullDestination"
    }

    $workPrefix = $fullWorkRoot + '\'
    if (-not $fullDestination.StartsWith($workPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "DestinationRoot must be inside the sandbox work root. DestinationRoot: $fullDestination; WorkRoot: $fullWorkRoot"
    }
}

function Clear-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)] [string]$DestinationRoot,
        [Parameter(Mandatory = $true)] [string]$WorkRoot
    )

    Assert-SandboxWorkDestination -DestinationRoot $DestinationRoot -WorkRoot $WorkRoot

    if (-not (Test-Path -LiteralPath $DestinationRoot)) {
        return
    }

    $emptyRoot = Join-Path $WorkRoot ("empty-{0}" -f [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $emptyRoot | Out-Null
    try {
        $robocopyArguments = @(
            $emptyRoot,
            $DestinationRoot,
            '/MIR',
            '/R:2',
            '/W:1',
            '/NFL',
            '/NDL',
            '/NP'
        )

        Write-Host ""
        Write-Host ">>> Clear existing isolated sandbox work root"
        & robocopy @robocopyArguments | Out-Host
        if ($LASTEXITCODE -gt 7) {
            throw "robocopy cleanup failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Remove-Item -LiteralPath $emptyRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Copy-RepositoryToWorkRoot {
    param(
        [Parameter(Mandatory = $true)] [string]$SourceRoot,
        [Parameter(Mandatory = $true)] [string]$DestinationRoot,
        [Parameter(Mandatory = $true)] [bool]$IncludeGitMetadata
    )

    $fullSource = [System.IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
    $fullDestination = [System.IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\')
    if ([string]::Equals($fullSource, $fullDestination, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "DestinationRoot must not equal SourceRoot: $fullDestination"
    }

    Assert-SandboxWorkDestination -DestinationRoot $DestinationRoot -WorkRoot $MappedWorkRoot
    Clear-DirectoryContents -DestinationRoot $DestinationRoot -WorkRoot $MappedWorkRoot

    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null

    $baseRootExcludedDirectories = @(
        (Join-Path $SourceRoot 'docs'),
        (Join-Path $SourceRoot 'plan'),
        (Join-Path $SourceRoot 'todo'),
        (Join-Path $SourceRoot '.claude'),
        (Join-Path $SourceRoot '.worktrees'),
        (Join-Path $SourceRoot '.vs'),
        (Join-Path $SourceRoot '.vscode'),
        (Join-Path $SourceRoot '.idea'),
        (Join-Path $SourceRoot '.dotnet-home'),
        (Join-Path $SourceRoot 'artifacts'),
        (Join-Path $SourceRoot 'release'),
        (Join-Path $SourceRoot 'TestResults'),
        (Join-Path $SourceRoot 'coverage'),
        (Join-Path $SourceRoot 'coverage-report'),
        (Join-Path $SourceRoot 'cert'),
        (Join-Path $SourceRoot 'secrets'),
        (Join-Path $SourceRoot 'tmp'),
        (Join-Path $SourceRoot 'Microsoft'),
        (Join-Path $SourceRoot 'NuGet'),
        (Join-Path $SourceRoot 'docfx\_site'),
        (Join-Path $SourceRoot 'docfx\api'),
        (Join-Path $SourceRoot 'docfx\obj')
    )
    $rootExcludedDirectories = if ($IncludeGitMetadata) {
        $baseRootExcludedDirectories
    }
    else {
        @((Join-Path $SourceRoot '.git')) + $baseRootExcludedDirectories
    }
    $rootExcludedFiles = if ($IncludeGitMetadata) {
        @()
    }
    else {
        @('.git')
    }

    $robocopyArguments = @(
        $SourceRoot,
        $DestinationRoot,
        '/MIR',
        '/R:2',
        '/W:1',
        '/NFL',
        '/NDL',
        '/NP',
        '/XJ',
        '/XD'
    ) + $rootExcludedDirectories + @(
        'bin',
        'obj',
        '/XF'
    ) + $rootExcludedFiles + @(
        '*.binlog',
        '*.log',
        '*.tmp',
        'build-output.txt',
        'test-output.txt',
        'unit-test-output.txt',
        'coverage-report.md',
        'AGENTS.md',
        'CHANGELOG.md',
        '.env',
        '.env.*',
        '*.pfx',
        '*.pwd',
        '*.key',
        '*.secret'
    )

    Write-Host ""
    Write-Host ">>> Copy repository to isolated sandbox work root"
    & robocopy @robocopyArguments | Out-Host
    if ($LASTEXITCODE -gt 7) {
        throw "robocopy failed with exit code $LASTEXITCODE."
    }
}

function Enable-MappedGit {
    $mappedGitCommand = Join-Path $MappedOutputRoot '..\Git\cmd\git.exe'
    $mappedGitCommand = [System.IO.Path]::GetFullPath($mappedGitCommand)
    if (-not (Test-Path -LiteralPath $mappedGitCommand)) {
        Write-Host 'Mapped Git was not found in Windows Sandbox.'
        return
    }

    $mappedGitCmd = Split-Path $mappedGitCommand -Parent
    $mappedGitBin = Join-Path (Split-Path $mappedGitCmd -Parent) 'bin'
    $env:PATH = "$mappedGitCmd;$mappedGitBin;$env:PATH"
    Write-Host "Mapped Git enabled: $mappedGitCommand"
}

function Test-PortableGitMetadata {
    param([Parameter(Mandatory = $true)] [string]$SourceRoot)

    return Test-Path -LiteralPath (Join-Path $SourceRoot '.git') -PathType Container
}

function Initialize-EphemeralGitRepository {
    param(
        [Parameter(Mandatory = $true)] [string]$RepoRoot,
        [Parameter(Mandatory = $true)] [string]$TrackedFilesPath
    )

    if (-not (Test-Path -LiteralPath $TrackedFilesPath)) {
        throw "Tracked-file manifest was not found: $TrackedFilesPath"
    }

    $gitCommand = Get-Command 'git.exe' -ErrorAction SilentlyContinue
    if ($null -eq $gitCommand) {
        Write-Host 'Git was not found; skipping ephemeral sandbox repository initialization.'
        return
    }

    $gitPath = [string]$gitCommand.Source
    if ([string]::IsNullOrWhiteSpace($env:HOME)) {
        $env:HOME = $env:USERPROFILE
    }
    $env:GIT_CONFIG_GLOBAL = Join-Path $MappedWorkRoot 'gitconfig'

    Invoke-External 'Initialize ephemeral sandbox git repository' $gitPath @(
        '-C',
        $RepoRoot,
        'init'
    )
    $normalizedRepoRoot = $RepoRoot -replace '\\', '/'
    Invoke-External 'Configure ephemeral git safe.directory' $gitPath @(
        'config',
        '--global',
        '--add',
        'safe.directory',
        $RepoRoot
    )
    Invoke-External 'Configure normalized ephemeral git safe.directory' $gitPath @(
        'config',
        '--global',
        '--add',
        'safe.directory',
        $normalizedRepoRoot
    )
    Invoke-External 'Configure wildcard ephemeral git safe.directory' $gitPath @(
        'config',
        '--global',
        '--add',
        'safe.directory',
        '*'
    )
    Invoke-External 'Index copied sandbox repository files' $gitPath @(
        '-C',
        $RepoRoot,
        'add',
        '--force',
        '--pathspec-from-file',
        $TrackedFilesPath
    )
}

function Configure-GitForSandbox {
    param([Parameter(Mandatory = $true)] [string]$RepoRoot)

    $gitCommand = Get-Command 'git.exe' -ErrorAction SilentlyContinue
    if ($null -eq $gitCommand) {
        throw 'Git is required for non-focused sandbox modes, but git.exe was not found in PATH.'
    }

    $gitPath = [string]$gitCommand.Source
    if ([string]::IsNullOrWhiteSpace($env:HOME)) {
        $env:HOME = $env:USERPROFILE
    }

    $env:GIT_CONFIG_GLOBAL = Join-Path $MappedWorkRoot 'gitconfig'
    $normalizedRepoRoot = $RepoRoot -replace '\\', '/'

    Invoke-External 'git --version' $gitPath @('--version')
    Invoke-External 'Configure git safe.directory' $gitPath @(
        'config',
        '--global',
        '--add',
        'safe.directory',
        $RepoRoot
    )
    Invoke-External 'Configure normalized git safe.directory' $gitPath @(
        'config',
        '--global',
        '--add',
        'safe.directory',
        $normalizedRepoRoot
    )
    Invoke-External 'Configure sandbox git safe.directory wildcard' $gitPath @(
        'config',
        '--global',
        '--add',
        'safe.directory',
        '*'
    )
    Invoke-External 'List git safe.directory values' $gitPath @(
        'config',
        '--global',
        '--get-all',
        'safe.directory'
    )
    Invoke-External 'git check-ignore smoke' $gitPath @(
        '-C',
        $RepoRoot,
        'check-ignore',
        '--quiet',
        'release/release_0.0.0_win-x64.zip'
    )
}

function Write-SandboxResult {
    param(
        [Parameter(Mandatory = $true)] [string]$Value,
        [Parameter(Mandatory = $true)] [System.Text.Encoding]$Encoding
    )

    $resultPath = Join-Path $MappedOutputRoot 'last-result.txt'
    $tempResultPath = Join-Path $MappedOutputRoot ("last-result.{0}.tmp" -f $RunId)
    [System.IO.File]::WriteAllText($tempResultPath, $Value, $Encoding)
    Move-Item -LiteralPath $tempResultPath -Destination $resultPath -Force
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logRoot = Join-Path $MappedOutputRoot 'logs'
$resultsRoot = Join-Path $MappedOutputRoot "TestResults\sandbox\$timestamp"
$sandboxRepoWorkRoot = Join-Path $MappedWorkRoot 'repo'

New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null

$logPath = Join-Path $logRoot "sandbox-ci-$timestamp.log"
Start-Transcript -Path $logPath -Force | Out-Null

try {
    Write-Host "Sandbox CI mode: $Mode"
    Write-Host "Run ID: $RunId"
    Write-Host "Repeat: $Repeat"
    Write-SandboxResult -Value "RUNNING $RunId $timestamp $Mode" -Encoding ([System.Text.Encoding]::ASCII)
    Write-Host "Mapped repo root: $MappedRepoRoot"
    Write-Host "Mapped work root: $MappedWorkRoot"
    Write-Host "Mapped output root: $MappedOutputRoot"

    Enable-MappedGit
    Enable-NativeBuildEnvironment
    $includeGitMetadata = ($Mode -ne 'FocusedFlakes') -and (Test-PortableGitMetadata -SourceRoot $MappedRepoRoot)
    if (($Mode -ne 'FocusedFlakes') -and -not $includeGitMetadata) {
        Write-Host 'Portable Git metadata was not available; running sandbox CI without copied Git metadata.'
    }

    Copy-RepositoryToWorkRoot -SourceRoot $MappedRepoRoot -DestinationRoot $sandboxRepoWorkRoot -IncludeGitMetadata $includeGitMetadata
    Set-Location $sandboxRepoWorkRoot
    if ($includeGitMetadata) {
        Configure-GitForSandbox -RepoRoot $sandboxRepoWorkRoot
    }
    elseif ($Mode -ne 'FocusedFlakes') {
        Initialize-EphemeralGitRepository -RepoRoot $sandboxRepoWorkRoot -TrackedFilesPath (Join-Path $MappedWorkRoot 'git-tracked-files.txt')
    }

    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:WPFDEVTOOLS_INSTALLER_TEST_MODE = '1'
    $env:WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA = '1'
    $env:WPFDEVTOOLS_TEST_SIGNATURE_STATUS = 'Valid'
    $env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED = '0'
    $env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE = '4'

    $dotnetPath = Install-DotNetSdk -RepoRoot $sandboxRepoWorkRoot
    Invoke-External 'dotnet --info' $dotnetPath @('--info')

    switch ($Mode) {
        'FocusedFlakes' {
            Invoke-UnitDebugBuild -DotNetPath $dotnetPath
            Invoke-FocusedFlakeTests -DotNetPath $dotnetPath -ResultsRoot $resultsRoot
        }
        'UnitDebug' {
            Invoke-UnitDebugBuild -DotNetPath $dotnetPath
            Invoke-UnitDebugTests -DotNetPath $dotnetPath -ResultsRoot $resultsRoot
        }
        'UnitRelease' {
            Invoke-ReleaseUnitBuild -DotNetPath $dotnetPath
            Invoke-McpServerDebugBuild -DotNetPath $dotnetPath
            Invoke-ReleaseUnitTests -DotNetPath $dotnetPath -ResultsRoot $resultsRoot
        }
        'FullManaged' {
            Invoke-UnitDebugBuild -DotNetPath $dotnetPath
            Invoke-ReleaseUnitBuild -DotNetPath $dotnetPath
            Invoke-McpServerDebugBuild -DotNetPath $dotnetPath
            Invoke-UnitDebugTests -DotNetPath $dotnetPath -ResultsRoot $resultsRoot
            Invoke-ReleaseUnitTests -DotNetPath $dotnetPath -ResultsRoot $resultsRoot
        }
        'NativeSmoke' {
            Invoke-DotNetRestore -DotNetPath $dotnetPath
            Invoke-NativeFullVerification -DotNetPath $dotnetPath -OutputRoot $MappedOutputRoot -Timestamp $timestamp -SkipDllLink
            Invoke-UnitDebugBuild -DotNetPath $dotnetPath
            Invoke-ReleaseUnitBuild -DotNetPath $dotnetPath
            Invoke-McpServerDebugBuild -DotNetPath $dotnetPath
            Invoke-UnitDebugTests -DotNetPath $dotnetPath -ResultsRoot $resultsRoot
            Invoke-ReleaseUnitTests -DotNetPath $dotnetPath -ResultsRoot $resultsRoot
        }
        'NativeFull' {
            Invoke-DotNetRestore -DotNetPath $dotnetPath
            Invoke-NativeFullVerification -DotNetPath $dotnetPath -OutputRoot $MappedOutputRoot -Timestamp $timestamp
            Invoke-UnitDebugBuild -DotNetPath $dotnetPath
            Invoke-ReleaseUnitBuild -DotNetPath $dotnetPath
            Invoke-McpServerDebugBuild -DotNetPath $dotnetPath
            Invoke-UnitDebugTests -DotNetPath $dotnetPath -ResultsRoot $resultsRoot
            Invoke-ReleaseUnitTests -DotNetPath $dotnetPath -ResultsRoot $resultsRoot
        }
    }

    Write-SandboxResult -Value "PASS $RunId $timestamp $Mode" -Encoding ([System.Text.Encoding]::ASCII)
    Write-Host "Sandbox CI completed successfully. Results: $resultsRoot"
}
catch {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    Write-SandboxResult -Value "FAIL $RunId $timestamp $Mode $($_.Exception.Message)" -Encoding $utf8NoBom
    Write-Error $_
    exit 1
}
finally {
    Stop-Transcript | Out-Null
}
