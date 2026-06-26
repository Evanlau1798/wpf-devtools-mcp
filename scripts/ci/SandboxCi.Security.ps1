function Resolve-SandboxSecurityOutputRoot {
    $mappedOutputRootVariable = Get-Variable -Name MappedOutputRoot -ErrorAction SilentlyContinue
    if (($null -ne $mappedOutputRootVariable) -and
        -not [string]::IsNullOrWhiteSpace([string]$mappedOutputRootVariable.Value)) {
        return [string]$mappedOutputRootVariable.Value
    }

    return Join-Path (Get-Location).ProviderPath 'tmp\sandbox-ci\output'
}

function Resolve-SandboxSecurityTimestamp {
    $timestampVariable = Get-Variable -Name timestamp -ErrorAction SilentlyContinue
    if (($null -ne $timestampVariable) -and
        -not [string]::IsNullOrWhiteSpace([string]$timestampVariable.Value)) {
        return [string]$timestampVariable.Value
    }

    return Get-Date -Format 'yyyyMMdd-HHmmss'
}

function Invoke-HostedPowerShellScriptAnalyzerGate {
    $module = Get-Module -ListAvailable PSScriptAnalyzer |
        Where-Object { $_.Version -ge [version]'1.25.0' } |
        Select-Object -First 1
    if ($null -eq $module) {
        Invoke-ExternalWithTimeout 'Install PowerShell ScriptAnalyzer' 'powershell.exe' @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            'scripts\tools\security\Install-PowerShellScriptAnalyzer.ps1',
            '-RequiredVersion',
            '1.25.0'
        ) -TimeoutSeconds 600 -OutputRoot (Resolve-SandboxSecurityOutputRoot) -Timestamp (Resolve-SandboxSecurityTimestamp)
    }

    Invoke-External 'Run PowerShell ScriptAnalyzer' 'powershell.exe' @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        'scripts\tools\security\Invoke-PowerShellScriptAnalyzerGate.ps1',
        '-Path',
        'scripts',
        '-Severity',
        'Error'
    )
}

function Invoke-HostedNativeBootstrapperSecurityAnalysis {
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
        throw 'Windows SDK version was not found. HostedWindowsX64 requires Windows SDK headers for native security analysis.'
    }

    $arguments = @(
        'src\WpfDevTools.Bootstrapper\WpfDevTools.Bootstrapper.vcxproj',
        '/m',
        '/nologo',
        '/p:Configuration=Release',
        '/p:Platform=x64',
        '/p:PreferredToolArchitecture=x64',
        '/p:RunCodeAnalysis=true',
        '/p:TreatWarningsAsErrors=true'
    )
    $arguments += Get-HostedNativeBuildProperties `
        -Platform 'x64' `
        -WindowsSdkDirectory $windowsSdkDirectory `
        -WindowsSdkVersion $windowsSdkVersion `
        -NativeHostDirectory $nativeHostDirectory

    Invoke-External 'Run native bootstrapper security analysis' $msbuildPath $arguments
}

function Invoke-HostedSecurityScanEquivalence {
    param([Parameter(Mandatory = $true)] [string]$DotNetPath)

    Invoke-External 'Run .NET analyzer gate' 'powershell.exe' @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        'scripts\tools\security\Invoke-DotNetAnalyzerGate.ps1',
        '-DotNetPath',
        $DotNetPath
    )
    Invoke-HostedPowerShellScriptAnalyzerGate
    Invoke-External 'Run repository secret pattern scan' 'powershell.exe' @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        'scripts\tools\security\Invoke-RepositorySecretScan.ps1'
    )
    Invoke-HostedNativeBootstrapperSecurityAnalysis
}
