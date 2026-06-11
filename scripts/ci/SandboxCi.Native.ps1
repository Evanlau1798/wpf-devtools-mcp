Set-Variable -Scope Script -Name SandboxCiVCToolsDirectory -Value $null
Set-Variable -Scope Script -Name SandboxCiVCToolsVersion -Value $null
Set-Variable -Scope Script -Name SandboxCiLinkToolDirectory -Value $null

function Resolve-MSBuildPath {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswherePath) {
        $msbuildPaths = @(& $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe')
        if ($msbuildPaths.Count -gt 0 -and (Test-Path -LiteralPath $msbuildPaths[0])) {
            return [string]$msbuildPaths[0]
        }
    }

    $visualStudioRoots = @(
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio'),
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio')
    )
    foreach ($visualStudioRoot in $visualStudioRoots) {
        if ([string]::IsNullOrWhiteSpace($visualStudioRoot) -or -not (Test-Path -LiteralPath $visualStudioRoot)) {
            continue
        }

        $candidate = Get-ChildItem -LiteralPath $visualStudioRoot -Directory -Recurse -ErrorAction SilentlyContinue |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'MSBuild\Current\Bin\MSBuild.exe') } |
            Select-Object -First 1
        if ($null -ne $candidate) {
            return (Join-Path $candidate.FullName 'MSBuild\Current\Bin\MSBuild.exe')
        }
    }

    $msbuildCommand = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
    if ($null -ne $msbuildCommand -and (Test-Path -LiteralPath $msbuildCommand.Source)) {
        return [string]$msbuildCommand.Source
    }

    throw 'MSBuild.exe was not found. NativeFull requires Visual Studio Build Tools inside the sandbox or a Hyper-V VM image that already contains them.'
}

function Test-VCToolsDirectorySupportsNativeX64 {
    param([Parameter(Mandatory = $true)] [System.IO.DirectoryInfo]$ToolDirectory)

    return (Test-Path -LiteralPath (Join-Path $ToolDirectory.FullName 'bin\HostX64\x64\CL.exe')) -and
        (Test-Path -LiteralPath (Join-Path $ToolDirectory.FullName 'bin\HostX64\x64\link.exe')) -and
        (Test-Path -LiteralPath (Join-Path $ToolDirectory.FullName 'bin\HostX64\x64\lib.exe')) -and
        (Test-Path -LiteralPath (Join-Path $ToolDirectory.FullName 'bin\HostX64\x64\cvtres.exe')) -and
        (Test-Path -LiteralPath (Join-Path $ToolDirectory.FullName 'include')) -and
        (Test-Path -LiteralPath (Join-Path $ToolDirectory.FullName 'lib\x64\LIBCMT.lib'))
}

function Resolve-VCToolsDirectory {
    param([Parameter(Mandatory = $true)] [string]$VisualStudioRoot)

    $vcToolsRoot = Join-Path $VisualStudioRoot 'VC\Tools\MSVC'
    $vcTools = @(Get-ChildItem -LiteralPath $vcToolsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending)
    if ($vcTools.Count -eq 0) {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_SANDBOX_VCTOOLS_VERSION)) {
        $requested = $env:WPFDEVTOOLS_SANDBOX_VCTOOLS_VERSION
        $requestedTool = $vcTools | Where-Object { [string]::Equals($_.Name, $requested, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if ($null -eq $requestedTool) {
            throw "Requested VCToolsVersion was not found: $requested"
        }
        if (-not (Test-VCToolsDirectorySupportsNativeX64 -ToolDirectory $requestedTool)) {
            throw "Requested VCToolsVersion does not contain the native x64 compiler, linker, resource converter, headers, and runtime libraries: $requested"
        }

        return $requestedTool
    }

    $completeTools = @($vcTools | Where-Object { Test-VCToolsDirectorySupportsNativeX64 -ToolDirectory $_ })
    if ($completeTools.Count -eq 0) {
        throw "No Visual C++ tools directory contains the native x64 compiler, linker, resource converter, headers, and runtime libraries under $vcToolsRoot."
    }

    return $completeTools[0]
}

function Resolve-LinkToolOverrideDirectory {
    param(
        [Parameter(Mandatory = $true)] [string]$VisualStudioRoot,
        [Parameter(Mandatory = $true)] [string]$VCToolsDirectory
    )

    $vcToolsRoot = Join-Path $VisualStudioRoot 'VC\Tools\MSVC'
    $vcTools = @(Get-ChildItem -LiteralPath $vcToolsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending)

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_SANDBOX_LINK_TOOL_VERSION)) {
        $requested = $env:WPFDEVTOOLS_SANDBOX_LINK_TOOL_VERSION
        $requestedTool = $vcTools | Where-Object { [string]::Equals($_.Name, $requested, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if ($null -eq $requestedTool) {
            throw "Requested LinkTool version was not found: $requested"
        }

        $requestedLinkToolDirectory = Join-Path $requestedTool.FullName 'bin\HostX64\x64'
        if (-not (Test-Path -LiteralPath (Join-Path $requestedLinkToolDirectory 'link.exe'))) {
            throw "Requested LinkTool version does not contain native x64 link.exe: $requested"
        }

        return $requestedLinkToolDirectory
    }

    return Resolve-CompatibleLinkToolDirectory -VisualStudioRoot $VisualStudioRoot -VCToolsDirectory $VCToolsDirectory
}

function Resolve-CompatibleLinkToolDirectory {
    param(
        [Parameter(Mandatory = $true)] [string]$VisualStudioRoot,
        [Parameter(Mandatory = $true)] [string]$VCToolsDirectory
    )

    $currentVersionText = Split-Path $VCToolsDirectory -Leaf
    $currentVersion = $null
    if (-not [System.Version]::TryParse($currentVersionText, [ref]$currentVersion)) {
        return $null
    }

    $vcToolsRoot = Join-Path $VisualStudioRoot 'VC\Tools\MSVC'
    $candidates = Get-ChildItem -LiteralPath $vcToolsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object {
            $candidateVersion = $null
            [System.Version]::TryParse($_.Name, [ref]$candidateVersion) -and
            $candidateVersion -lt $currentVersion -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'bin\HostX64\x64\link.exe')) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'bin\HostX64\x64\cvtres.exe'))
        } |
        Sort-Object { [System.Version]$_.Name } -Descending

    $candidate = @($candidates | Select-Object -First 1)
    if ($candidate.Count -eq 0) {
        return $null
    }

    return (Join-Path $candidate[0].FullName 'bin\HostX64\x64')
}

function Resolve-WindowsSdkToolPath {
    param(
        [Parameter(Mandatory = $true)] [string]$ToolName,
        [string]$WindowsSdkVersion
    )

    if (-not [string]::IsNullOrWhiteSpace($env:WindowsSDKDir) -and -not [string]::IsNullOrWhiteSpace($WindowsSdkVersion)) {
        $candidate = Join-Path $env:WindowsSDKDir "bin\$WindowsSdkVersion\x64\$ToolName"
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $command = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($null -ne $command -and (Test-Path -LiteralPath $command.Source)) {
        return [string]$command.Source
    }

    throw "$ToolName was not found in the Windows SDK tool path."
}

function Assert-NativeBuildEnvironment {
    if ([string]::IsNullOrWhiteSpace($script:SandboxCiVCToolsDirectory) -or
        -not (Test-Path -LiteralPath $script:SandboxCiVCToolsDirectory)) {
        throw 'VC tools were not found. NativeFull requires Visual Studio C++ x64 tools inside the sandbox.'
    }

    $vcTools = [System.IO.DirectoryInfo]$script:SandboxCiVCToolsDirectory
    if (-not (Test-VCToolsDirectorySupportsNativeX64 -ToolDirectory $vcTools)) {
        throw "VC tools are missing the native x64 compiler, linker, resource converter, headers, or runtime libraries: $($vcTools.FullName)"
    }

    if ([string]::IsNullOrWhiteSpace($env:NetFxSdkLibraryDir) -or -not (Test-Path -LiteralPath $env:NetFxSdkLibraryDir)) {
        throw 'NetFxSdkLibraryDir was not found. NativeFull requires the .NET Framework 4.8 SDK libraries inside the sandbox.'
    }
}

function Invoke-NativeBootstrapperBuild {
    param(
        [Parameter(Mandatory = $true)] [string]$OutputRoot,
        [Parameter(Mandatory = $true)] [string]$Timestamp,
        [string]$WindowsSdkVersion,
        [switch]$SkipDllLink
    )

    $repoRoot = $PWD.ProviderPath
    $projectRoot = Join-Path $repoRoot 'src\WpfDevTools.Bootstrapper'
    $netHostAssets = Join-Path $repoRoot 'artifacts\dotnet-host\x64\native'
    $outputDirectory = Join-Path $repoRoot 'artifacts\bootstrapper\Debug\x64'
    $intermediateDirectory = Join-Path $projectRoot 'Debug\x64'
    New-Item -ItemType Directory -Force -Path $outputDirectory, $intermediateDirectory | Out-Null

    Invoke-ExternalWithTimeout 'Prepare native host assets x64' 'powershell.exe' @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        (Join-Path $projectRoot 'resolve_nethost_assets.ps1'),
        '-Platform',
        'x64',
        '-OutputDir',
        $netHostAssets
    ) -TimeoutSeconds 300 -OutputRoot $OutputRoot -Timestamp $Timestamp

    $vcToolsBin = Join-Path $script:SandboxCiVCToolsDirectory 'bin\HostX64\x64'
    $clPath = Join-Path $vcToolsBin 'CL.exe'
    $libPath = Join-Path $vcToolsBin 'lib.exe'
    $cvtresPath = Join-Path $vcToolsBin 'cvtres.exe'
    $linkDirectory = if (-not [string]::IsNullOrWhiteSpace($script:SandboxCiLinkToolDirectory)) {
        $script:SandboxCiLinkToolDirectory
    }
    else {
        $vcToolsBin
    }
    $linkPath = Join-Path $linkDirectory 'link.exe'
    $rcPath = Resolve-WindowsSdkToolPath -ToolName 'rc.exe' -WindowsSdkVersion $WindowsSdkVersion
    $netFxSdkLibraryDirectory = $env:NetFxSdkLibraryDir

    $objectDirectory = $intermediateDirectory.TrimEnd('\') + '\'
    Invoke-ExternalWithTimeout 'Compile native bootstrapper objects x64' $clPath @(
        '/c',
        "/I$netHostAssets",
        '/nologo',
        '/W4',
        '/WX-',
        '/diagnostics:column',
        '/sdl',
        '/Od',
        '/D',
        'WIN32_LEAN_AND_MEAN',
        '/D',
        'NOMINMAX',
        '/D',
        '_CRT_SECURE_NO_WARNINGS',
        '/D',
        'NETHOST_USE_AS_STATIC',
        '/D',
        '_WINDLL',
        '/D',
        '_UNICODE',
        '/D',
        'UNICODE',
        '/EHsc',
        '/RTC1',
        '/MT',
        '/GS',
        '/fp:precise',
        '/Zc:wchar_t',
        '/Zc:forScope',
        '/Zc:inline',
        '/std:c++17',
        '/permissive-',
        "/Fo$objectDirectory",
        (Join-Path $projectRoot 'dllmain.cpp'),
        (Join-Path $projectRoot 'bootstrap_entry.cpp'),
        (Join-Path $projectRoot 'clr_hosting.cpp'),
        (Join-Path $projectRoot 'coreclr_hosting.cpp')
    ) -TimeoutSeconds 900 -OutputRoot $OutputRoot -Timestamp $Timestamp

    $resourcePath = Join-Path $intermediateDirectory 'bootstrapper.res'
    $resourceObjectPath = Join-Path $intermediateDirectory 'bootstrapper.res.obj'
    Invoke-ExternalWithTimeout 'Compile native bootstrapper resources x64' $rcPath @(
        '/D',
        '_UNICODE',
        '/D',
        'UNICODE',
        '/l',
        '0x0409',
        '/nologo',
        "/fo$resourcePath",
        (Join-Path $projectRoot 'bootstrapper.rc')
    ) -TimeoutSeconds 300 -OutputRoot $OutputRoot -Timestamp $Timestamp
    Invoke-ExternalWithTimeout 'Convert native bootstrapper resources x64' $cvtresPath @(
        '/NOLOGO',
        '/MACHINE:X64',
        "/OUT:$resourceObjectPath",
        $resourcePath
    ) -TimeoutSeconds 300 -OutputRoot $OutputRoot -Timestamp $Timestamp

    $nativeObjectPaths = @(
        (Join-Path $intermediateDirectory 'dllmain.obj'),
        (Join-Path $intermediateDirectory 'bootstrap_entry.obj'),
        (Join-Path $intermediateDirectory 'clr_hosting.obj'),
        (Join-Path $intermediateDirectory 'coreclr_hosting.obj')
    )

    $nativeSmokeLibraryPath = Join-Path $outputDirectory 'WpfDevTools.Bootstrapper.x64.native-smoke.lib'
    $archiveArguments = @(
        '/NOLOGO',
        "/OUT:$nativeSmokeLibraryPath"
    ) + $nativeObjectPaths + @($resourceObjectPath)
    Invoke-ExternalWithTimeout 'Archive native bootstrapper smoke library x64' $libPath $archiveArguments -TimeoutSeconds 300 -OutputRoot $OutputRoot -Timestamp $Timestamp

    if ($SkipDllLink) {
        Write-Host 'Native smoke completed. Skipping native DLL link because this mode targets compiler, resource, and COFF archive validation while avoiding the Windows Sandbox ProcessResFiles linker path.'
        return
    }

    Remove-Item Env:LINK, Env:_LINK_ -ErrorAction SilentlyContinue
    $linkArguments = @(
        '/ERRORREPORT:QUEUE',
        "/OUT:$(Join-Path $outputDirectory 'WpfDevTools.Bootstrapper.x64.dll')",
        '/INCREMENTAL:NO',
        '/NOLOGO',
        "/LIBPATH:$netHostAssets",
        "/LIBPATH:$netFxSdkLibraryDirectory",
        'libnethost.lib',
        'Crypt32.lib',
        'kernel32.lib',
        'user32.lib',
        'gdi32.lib',
        'winspool.lib',
        'comdlg32.lib',
        'advapi32.lib',
        'shell32.lib',
        'ole32.lib',
        'oleaut32.lib',
        'uuid.lib',
        'odbc32.lib',
        'odbccp32.lib',
        '/MANIFEST:NO',
        '/SUBSYSTEM:WINDOWS',
        '/DYNAMICBASE',
        '/NXCOMPAT',
        "/IMPLIB:$(Join-Path $outputDirectory 'WpfDevTools.Bootstrapper.x64.lib')",
        '/MACHINE:X64',
        '/ignore:4099',
        "/DEF:$(Join-Path $projectRoot 'exports.def')",
        '/DLL'
    ) + $nativeObjectPaths

    $linkResponsePath = Join-Path $intermediateDirectory 'link.rsp'
    $linkResponseArguments = $linkArguments | ForEach-Object { ConvertTo-ProcessArgument -Argument $_ }
    [System.IO.File]::WriteAllLines($linkResponsePath, $linkResponseArguments, [System.Text.Encoding]::ASCII)

    try {
        Invoke-ExternalWithTimeout 'Link native bootstrapper Debug x64' $linkPath @("@$linkResponsePath") -TimeoutSeconds 900 -OutputRoot $OutputRoot -Timestamp $Timestamp
    }
    finally {
        Remove-Item -LiteralPath $linkResponsePath -Force -ErrorAction SilentlyContinue
    }
}

function Enable-NativeBuildEnvironment {
    $sandboxDesktopRoot = Split-Path $MappedOutputRoot -Parent
    $windowsKitsCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'),
        (Join-Path $sandboxDesktopRoot '10')
    )
    $windowsKitsRoot = $windowsKitsCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($windowsKitsRoot)) {
        $env:WindowsSdkDir = $windowsKitsRoot.TrimEnd('\') + '\'
        $env:WindowsSDKDir = $env:WindowsSdkDir
        $sdkVersion = Get-ChildItem -LiteralPath (Join-Path $windowsKitsRoot 'Include') -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            Select-Object -ExpandProperty Name -First 1
        if (-not [string]::IsNullOrWhiteSpace($sdkVersion)) {
            $env:INCLUDE = @(
                (Join-Path $windowsKitsRoot "Include\$sdkVersion\um"),
                (Join-Path $windowsKitsRoot "Include\$sdkVersion\shared"),
                (Join-Path $windowsKitsRoot "Include\$sdkVersion\ucrt"),
                (Join-Path $windowsKitsRoot "Include\$sdkVersion\winrt"),
                $env:INCLUDE
            ) -join ';'
            $env:LIB = @(
                (Join-Path $windowsKitsRoot "Lib\$sdkVersion\um\x64"),
                (Join-Path $windowsKitsRoot "Lib\$sdkVersion\ucrt\x64"),
                $env:LIB
            ) -join ';'
            $env:PATH = (Join-Path $windowsKitsRoot "bin\$sdkVersion\x64") + ';' + (Join-Path $windowsKitsRoot "bin\$sdkVersion\x86") + ';' + $env:PATH
        }
        $env:PATH = (Join-Path $windowsKitsRoot 'bin\x64') + ';' + (Join-Path $windowsKitsRoot 'bin\x86') + ';' + $env:PATH
        Write-Host "Windows SDK enabled: $windowsKitsRoot"
    }

    $netFxSdkCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\NETFXSDK\4.8'),
        (Join-Path $sandboxDesktopRoot 'NETFXSDK\4.8')
    )
    $netFxSdkRoot = $netFxSdkCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($netFxSdkRoot)) {
        $env:NETFXKitsDir = $netFxSdkRoot.TrimEnd('\') + '\'
        $env:NetFxSdkLibraryDir = Join-Path $netFxSdkRoot 'Lib\um\x64'
        $env:INCLUDE = (Join-Path $netFxSdkRoot 'Include\um') + ';' + $env:INCLUDE
        $env:LIB = (Join-Path $netFxSdkRoot 'Lib\um\x64') + ';' + $env:LIB
        Write-Host "NETFX SDK enabled: $netFxSdkRoot"
    }

    $referenceAssembliesRoot = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
    if (Test-Path -LiteralPath $referenceAssembliesRoot) {
        $env:TargetFrameworkRootPath = (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework')
    }

    try {
        $msbuildPath = Resolve-MSBuildPath
        $msbuildDirectory = Split-Path $msbuildPath -Parent
        $vsRoot = Split-Path (Split-Path (Split-Path $msbuildDirectory -Parent) -Parent) -Parent
        $vcTools = Resolve-VCToolsDirectory -VisualStudioRoot $vsRoot
        if ($null -ne $vcTools) {
            $script:SandboxCiVCToolsDirectory = $vcTools.FullName
            $script:SandboxCiVCToolsVersion = $vcTools.Name
            $env:VCToolsInstallDir = $vcTools.FullName.TrimEnd('\') + '\'
            $env:VCToolsVersion = $vcTools.Name
            $env:INCLUDE = (Join-Path $vcTools.FullName 'include') + ';' + $env:INCLUDE
            $env:LIB = (Join-Path $vcTools.FullName 'lib\x64') + ';' + $env:LIB
            $env:PATH = (Join-Path $vcTools.FullName 'bin\HostX64\x64') + ';' + (Join-Path $vcTools.FullName 'bin\HostX86\x64') + ';' + $env:PATH
            Write-Host "VC tools enabled: $($vcTools.FullName)"

            $linkToolDirectory = Resolve-LinkToolOverrideDirectory -VisualStudioRoot $vsRoot -VCToolsDirectory $vcTools.FullName
            if (-not [string]::IsNullOrWhiteSpace($linkToolDirectory)) {
                $script:SandboxCiLinkToolDirectory = $linkToolDirectory
                Write-Host "VC link tool override enabled: $linkToolDirectory"
            }
        }

        $env:PATH = "$msbuildDirectory;$env:PATH"
        Write-Host "Native MSBuild enabled: $msbuildPath"
    }
    catch {
        Write-Host "Native MSBuild not enabled yet: $($_.Exception.Message)"
    }
}

function Invoke-NativeFullVerification {
    param(
        [Parameter(Mandatory = $true)] [string]$DotNetPath,
        [Parameter(Mandatory = $true)] [string]$OutputRoot,
        [Parameter(Mandatory = $true)] [string]$Timestamp,
        [switch]$SkipDllLink
    )

    $msbuildPath = Resolve-MSBuildPath
    Assert-NativeBuildEnvironment
    $windowsSdkVersion = $null
    if (-not [string]::IsNullOrWhiteSpace($env:WindowsSDKDir)) {
        $includeRoot = Join-Path $env:WindowsSDKDir 'Include'
        $windowsSdkVersion = Get-ChildItem -LiteralPath $includeRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            Select-Object -ExpandProperty Name -First 1
    }

    Invoke-NativeBootstrapperBuild -OutputRoot $OutputRoot -Timestamp $Timestamp -WindowsSdkVersion $windowsSdkVersion -SkipDllLink:$SkipDllLink
}
