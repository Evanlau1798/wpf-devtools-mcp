function Invoke-HostedPowerShellScriptAnalyzerGate {
    $module = Get-Module -ListAvailable PSScriptAnalyzer |
        Where-Object { $_.Version -ge [version]'1.25.0' } |
        Select-Object -First 1
    if ($null -eq $module) {
        Invoke-External 'Install PowerShell ScriptAnalyzer' 'powershell.exe' @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-Command',
            "Set-PSRepository -Name PSGallery -InstallationPolicy Trusted; Install-Module PSScriptAnalyzer -Scope CurrentUser -Force -RequiredVersion 1.25.0 -Repository PSGallery"
        )
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

    Invoke-External 'Run .NET analyzer gate' $DotNetPath @(
        'format',
        'WpfDevTools.sln',
        'analyzers',
        '--verify-no-changes',
        '--severity',
        'error',
        '--no-restore'
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
