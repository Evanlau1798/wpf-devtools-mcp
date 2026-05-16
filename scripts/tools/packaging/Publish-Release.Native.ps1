function Invoke-ArchiveCreation {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [string]$ArchivePath,
        [string[]]$RequiredRelativePaths = @()
    )

    $retryDelayMilliseconds = 250
    $maxAttempts = 40

    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        try {
            $packageFileRelativePaths = @(Get-PackageFileRelativePaths -PackageDirectory $PackageDirectory)
            $entryNames = @($packageFileRelativePaths + $RequiredRelativePaths |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                ForEach-Object { ConvertTo-ArchiveEntryName -RelativePath $_ } |
                Sort-Object -Unique)
            New-ReleaseArchive `
                -PackageDirectory $PackageDirectory `
                -ArchivePath $ArchivePath `
                -EntryNames $entryNames
            return
        }
        catch {
            if ($attempt -eq $maxAttempts) {
                throw
            }

            Start-Sleep -Milliseconds $retryDelayMilliseconds
        }
    }
}

function New-ReleaseArchive {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [string]$ArchivePath,
        [string[]]$EntryNames = @()
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archiveDirectory = [System.IO.Path]::GetDirectoryName($ArchivePath)
    if (-not [string]::IsNullOrWhiteSpace($archiveDirectory)) {
        New-Item -ItemType Directory -Force -Path $archiveDirectory | Out-Null
    }

    $tempArchivePath = "$ArchivePath.tmp"
    if (Test-Path -LiteralPath $tempArchivePath) {
        Remove-Item -LiteralPath $tempArchivePath -Force
    }

    try {
        $archive = [System.IO.Compression.ZipFile]::Open($tempArchivePath, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($entryName in @($EntryNames)) {
                $sourcePath = ConvertTo-PackageFilePath -PackageDirectory $PackageDirectory -ArchiveEntryName $entryName
                if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                    throw "Required archive entry source was not found: $sourcePath"
                }

                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive,
                    $sourcePath,
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally {
            $archive.Dispose()
        }

        if (Test-Path -LiteralPath $ArchivePath) {
            Remove-Item -LiteralPath $ArchivePath -Force
        }

        Move-Item -LiteralPath $tempArchivePath -Destination $ArchivePath -Force
    }
    finally {
        if (Test-Path -LiteralPath $tempArchivePath) {
            Remove-Item -LiteralPath $tempArchivePath -Force
        }
    }
}

function ConvertTo-ArchiveEntryName {
    param([Parameter(Mandatory)] [string]$RelativePath)

    $candidate = $RelativePath.TrimStart([char[]]@('\', '/'))
    $segments = @($candidate -split '[\\/]' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ([string]::IsNullOrWhiteSpace($candidate) -or
        [System.IO.Path]::IsPathRooted($candidate) -or
        $segments -contains '..') {
        throw "Invalid required archive entry path: $RelativePath"
    }

    return ($segments -join '/')
}

function ConvertTo-PackageFilePath {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [string]$ArchiveEntryName
    )

    return (Join-Path $PackageDirectory $ArchiveEntryName.Replace('/', '\'))
}

function Get-PackageFileRelativePaths {
    param([Parameter(Mandatory)] [string]$PackageDirectory)

    $packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path.TrimEnd([char[]]@('\', '/'))
    $packageRootWithSeparator = $packageRoot + [System.IO.Path]::DirectorySeparatorChar
    Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
        if (-not $_.FullName.StartsWith($packageRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Package file is outside package directory: $($_.FullName)"
        }

        $_.FullName.Substring($packageRootWithSeparator.Length)
    }
}

function Resolve-MSBuildPath {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH)) {
        return $env:WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH
    }

    $command = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vsWhere) {
        $resolved = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
            Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            return $resolved
        }
    }

    throw 'MSBuild.exe was not found. Install Visual Studio Build Tools or add MSBuild.exe to PATH.'
}

function Get-VisualStudioInstallationRoot {
    param([Parameter(Mandatory)] [string]$ResolvedMsBuildPath)

    $candidateDirectory = Split-Path -Parent $ResolvedMsBuildPath
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        if ([string]::IsNullOrWhiteSpace($candidateDirectory)) {
            return $null
        }

        if ((Split-Path $candidateDirectory -Leaf) -eq 'Current') {
            $msbuildRoot = Split-Path -Parent $candidateDirectory
            if (-not [string]::IsNullOrWhiteSpace($msbuildRoot) -and
                (Split-Path $msbuildRoot -Leaf) -eq 'MSBuild') {
                return Split-Path -Parent $msbuildRoot
            }
        }

        $candidateDirectory = Split-Path -Parent $candidateDirectory
    }

    return $null
}

function Resolve-VCToolsDirectory {
    param([Parameter(Mandatory)] [string]$ResolvedMsBuildPath)

    if (-not [string]::IsNullOrWhiteSpace($env:VCToolsInstallDir) -and
        (Test-Path -LiteralPath $env:VCToolsInstallDir)) {
        return $env:VCToolsInstallDir.TrimEnd('\')
    }

    $visualStudioRoot = Get-VisualStudioInstallationRoot -ResolvedMsBuildPath $ResolvedMsBuildPath
    if ([string]::IsNullOrWhiteSpace($visualStudioRoot)) {
        return ''
    }

    $msvcRoot = Join-Path $visualStudioRoot 'VC\Tools\MSVC'
    if (-not (Test-Path -LiteralPath $msvcRoot)) {
        return ''
    }

    $toolDirectory = Get-ChildItem -LiteralPath $msvcRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+(\.\d+){0,2}$' } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1

    if ($null -eq $toolDirectory) {
        return ''
    }

    return $toolDirectory.FullName.TrimEnd('\')
}

function Get-NativeBootstrapperTargetArchitecture {
    param([Parameter(Mandatory)] [string]$BootstrapperPlatform)

    switch ($BootstrapperPlatform) {
        'x64' { return 'x64' }
        'Win32' { return 'x86' }
        'ARM64' { return 'arm64' }
        default { return '' }
    }
}

function Select-ExistingPathSegments {
    param([string[]]$Candidates)

    $segments = New-Object System.Collections.Generic.List[string]
    foreach ($candidate in @($Candidates)) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path -LiteralPath $candidate) {
            $segments.Add($candidate.TrimEnd('\'))
        }
    }

    return @($segments)
}

function ConvertTo-NativeBuildPathProperty {
    param(
        [string[]]$PathSegments,
        [string]$ExistingValue
    )

    $values = New-Object System.Collections.Generic.List[string]
    foreach ($segment in @(Select-ExistingPathSegments -Candidates $PathSegments)) {
        if (-not $values.Contains($segment)) {
            $values.Add($segment)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ExistingValue)) {
        $values.Add($ExistingValue.TrimEnd(';'))
    }

    if ($values.Count -eq 0) {
        return ''
    }

    return ConvertTo-MSBuildPropertyValue -Value ($values -join ';')
}

function Assert-NativeBuildDirectory {
    param(
        [string]$Path,
        [Parameter(Mandatory)] [string]$Description,
        [Parameter(Mandatory)] [string]$BootstrapperPlatform
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or
        -not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Could not resolve $Description for native bootstrapper platform '$BootstrapperPlatform'. Expected directory: $Path"
    }

    return $Path.TrimEnd('\')
}

function Assert-NativeBuildToolDirectory {
    param(
        [string[]]$Directories,
        [Parameter(Mandatory)] [string]$ToolName,
        [Parameter(Mandatory)] [string]$Description,
        [Parameter(Mandatory)] [string]$BootstrapperPlatform
    )

    foreach ($directory in @(Select-ExistingPathSegments -Candidates $Directories)) {
        $toolPath = Join-Path $directory $ToolName
        if (Test-Path -LiteralPath $toolPath -PathType Leaf) {
            return $directory
        }
    }

    throw "Could not resolve $Description for native bootstrapper platform '$BootstrapperPlatform'. Expected tool '$ToolName' under: $($Directories -join '; ')"
}

function Get-NativeBootstrapperBuildProperties {
    param(
        [Parameter(Mandatory)] [string]$BootstrapperPlatform,
        [Parameter(Mandatory)] [string]$ResolvedMsBuildPath,
        [string]$WindowsSdkDirectory,
        [string]$WindowsSdkVersion
    )

    $targetArchitecture = Get-NativeBootstrapperTargetArchitecture -BootstrapperPlatform $BootstrapperPlatform
    if ([string]::IsNullOrWhiteSpace($targetArchitecture)) {
        return [ordered]@{
            IncludePath = ''
            LibraryPath = ''
            ExecutablePath = ''
        }
    }

    $vcToolsDirectory = Resolve-VCToolsDirectory -ResolvedMsBuildPath $ResolvedMsBuildPath
    $vcIncludeDirectory = if ([string]::IsNullOrWhiteSpace($vcToolsDirectory)) {
        ''
    }
    else {
        Join-Path $vcToolsDirectory 'include'
    }

    $vcLibraryDirectory = if ([string]::IsNullOrWhiteSpace($vcToolsDirectory)) {
        ''
    }
    else {
        Join-Path $vcToolsDirectory (Join-Path 'lib' $targetArchitecture)
    }

    $vcExecutableDirectories = if ([string]::IsNullOrWhiteSpace($vcToolsDirectory)) {
        @()
    }
    else {
        @(
            (Join-Path $vcToolsDirectory (Join-Path 'bin\HostX64' $targetArchitecture)),
            (Join-Path $vcToolsDirectory (Join-Path 'bin\HostX86' $targetArchitecture))
        )
    }

    $sdkIncludeDirectories = @()
    $sdkLibraryDirectories = @()
    $sdkExecutableDirectories = @()
    $sdkUcrtIncludeDirectory = ''
    $sdkSharedIncludeDirectory = ''
    $sdkUmIncludeDirectory = ''
    $sdkUcrtLibraryDirectory = ''
    $sdkUmLibraryDirectory = ''
    if (-not [string]::IsNullOrWhiteSpace($WindowsSdkDirectory) -and
        -not [string]::IsNullOrWhiteSpace($WindowsSdkVersion)) {
        $sdkIncludeRoot = Join-Path $WindowsSdkDirectory (Join-Path 'Include' $WindowsSdkVersion)
        $sdkUcrtIncludeDirectory = Join-Path $sdkIncludeRoot 'ucrt'
        $sdkSharedIncludeDirectory = Join-Path $sdkIncludeRoot 'shared'
        $sdkUmIncludeDirectory = Join-Path $sdkIncludeRoot 'um'
        $sdkIncludeDirectories = @(
            $sdkUcrtIncludeDirectory,
            $sdkSharedIncludeDirectory,
            $sdkUmIncludeDirectory,
            (Join-Path $sdkIncludeRoot 'winrt'),
            (Join-Path $sdkIncludeRoot 'cppwinrt')
        )

        $sdkLibraryRoot = Join-Path $WindowsSdkDirectory (Join-Path 'Lib' $WindowsSdkVersion)
        $sdkUcrtLibraryDirectory = Join-Path $sdkLibraryRoot (Join-Path 'ucrt' $targetArchitecture)
        $sdkUmLibraryDirectory = Join-Path $sdkLibraryRoot (Join-Path 'um' $targetArchitecture)
        $sdkLibraryDirectories = @(
            $sdkUcrtLibraryDirectory,
            $sdkUmLibraryDirectory
        )

        $sdkExecutableRoot = Join-Path $WindowsSdkDirectory (Join-Path 'bin' $WindowsSdkVersion)
        $sdkExecutableDirectories = @(
            (Join-Path $sdkExecutableRoot 'x64'),
            (Join-Path $sdkExecutableRoot $targetArchitecture)
        )
    }

    $requiredVcIncludeDirectory = Assert-NativeBuildDirectory `
        -Path $vcIncludeDirectory `
        -Description 'VC include path' `
        -BootstrapperPlatform $BootstrapperPlatform
    $requiredVcLibraryDirectory = Assert-NativeBuildDirectory `
        -Path $vcLibraryDirectory `
        -Description 'VC library path' `
        -BootstrapperPlatform $BootstrapperPlatform
    $requiredVcExecutableDirectory = Assert-NativeBuildToolDirectory `
        -Directories $vcExecutableDirectories `
        -ToolName 'cl.exe' `
        -Description 'VC compiler path' `
        -BootstrapperPlatform $BootstrapperPlatform
    $requiredVcLinkerDirectory = Assert-NativeBuildToolDirectory `
        -Directories $vcExecutableDirectories `
        -ToolName 'link.exe' `
        -Description 'VC linker path' `
        -BootstrapperPlatform $BootstrapperPlatform
    $requiredSdkExecutableDirectory = Assert-NativeBuildToolDirectory `
        -Directories $sdkExecutableDirectories `
        -ToolName 'rc.exe' `
        -Description 'Windows SDK resource compiler path' `
        -BootstrapperPlatform $BootstrapperPlatform
    $requiredSdkIncludeDirectories = @(
        (Assert-NativeBuildDirectory `
            -Path $sdkUcrtIncludeDirectory `
            -Description 'Windows SDK UCRT include path' `
            -BootstrapperPlatform $BootstrapperPlatform),
        (Assert-NativeBuildDirectory `
            -Path $sdkSharedIncludeDirectory `
            -Description 'Windows SDK shared include path' `
            -BootstrapperPlatform $BootstrapperPlatform),
        (Assert-NativeBuildDirectory `
            -Path $sdkUmIncludeDirectory `
            -Description 'Windows SDK UM include path' `
            -BootstrapperPlatform $BootstrapperPlatform)
    )
    $requiredSdkLibraryDirectories = @(
        (Assert-NativeBuildDirectory `
            -Path $sdkUcrtLibraryDirectory `
            -Description 'Windows SDK UCRT library path' `
            -BootstrapperPlatform $BootstrapperPlatform),
        (Assert-NativeBuildDirectory `
            -Path $sdkUmLibraryDirectory `
            -Description 'Windows SDK UM library path' `
            -BootstrapperPlatform $BootstrapperPlatform)
    )

    $includePath = ConvertTo-NativeBuildPathProperty `
        -PathSegments (@($requiredVcIncludeDirectory) + $requiredSdkIncludeDirectories + $sdkIncludeDirectories) `
        -ExistingValue $env:INCLUDE
    $libraryPath = ConvertTo-NativeBuildPathProperty `
        -PathSegments (@($requiredVcLibraryDirectory) + $requiredSdkLibraryDirectories + $sdkLibraryDirectories) `
        -ExistingValue $env:LIB
    $executablePath = ConvertTo-NativeBuildPathProperty `
        -PathSegments (@($requiredVcExecutableDirectory, $requiredVcLinkerDirectory) + $vcExecutableDirectories + @($requiredSdkExecutableDirectory) + $sdkExecutableDirectories) `
        -ExistingValue $env:PATH

    if ([string]::IsNullOrWhiteSpace($includePath) -or
        [string]::IsNullOrWhiteSpace($libraryPath) -or
        [string]::IsNullOrWhiteSpace($executablePath)) {
        throw "Could not resolve native bootstrapper toolchain paths for platform '$BootstrapperPlatform'. Run from a Visual Studio Developer shell or install the Windows SDK and MSVC C++ toolchain."
    }

    return [ordered]@{
        IncludePath = $includePath
        LibraryPath = $libraryPath
        ExecutablePath = $executablePath
    }
}

function Assert-ArchitectureToolchainAvailable {
    param(
        [Parameter(Mandatory)] [string[]]$ResolvedArchitectures,
        [Parameter(Mandatory)] [string]$ResolvedMsBuildPath,
        [string]$WindowsSdkDirectory,
        [string]$WindowsSdkVersion
    )

    if ($ResolvedArchitectures -notcontains 'arm64') {
        return
    }

    try {
        Get-NativeBootstrapperBuildProperties `
            -BootstrapperPlatform 'ARM64' `
            -ResolvedMsBuildPath $ResolvedMsBuildPath `
            -WindowsSdkDirectory $WindowsSdkDirectory `
            -WindowsSdkVersion $WindowsSdkVersion | Out-Null
    }
    catch {
        throw "Release architecture 'arm64' bootstrapper platform 'ARM64' requires the Visual Studio v143 ARM64 C++ toolchain and Windows SDK. Install component Microsoft.VisualStudio.Component.VC.Tools.ARM64 and rerun scripts/tools/build-release.ps1. Missing dependency: $($_.Exception.Message)"
    }
}
