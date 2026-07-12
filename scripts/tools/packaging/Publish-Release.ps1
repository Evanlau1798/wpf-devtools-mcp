param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string[]]$Architectures = @('x64'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\..\artifacts\release'),
    [string]$ExpectedReleaseTag,
    [string]$SigningCertificatePath,
    [string]$SigningCertificateThumbprint,
    [string]$SigningPasswordEnvironmentVariable = 'WPFDEVTOOLS_PFX_PASSWORD',
    [string]$SigningTimestampServer = 'https://timestamp.digicert.com',
    [ValidateSet('Signed', 'ReleaseChecksumOnly')]
    [string]$ReleaseTrustMode = 'Signed',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$supportedArchitectures = @('x64', 'x86', 'arm64')

$publishReleaseHelperLeafNames = @(
    'Publish-Release.Core.ps1'
    'Publish-Release.Signing.ps1'
    'Publish-Release.Native.ps1'
)

foreach ($helperLeafName in $publishReleaseHelperLeafNames) {
    $helperPath = Join-Path $PSScriptRoot $helperLeafName
    if (-not (Test-Path -LiteralPath $helperPath)) {
        throw "Publish release helper script was not found: $helperPath"
    }

    . $helperPath
}

$signaturePolicy = Get-SignaturePolicy -BuildConfiguration $Configuration -ReleaseTrustMode $ReleaseTrustMode
Assert-ReleaseSignatureOverridePreconditions -SignaturePolicy $signaturePolicy

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$serverProject = Join-Path $repoRoot 'src\WpfDevTools.Mcp.Server\WpfDevTools.Mcp.Server.csproj'
$inspectorProject = Join-Path $repoRoot 'src\WpfDevTools.Inspector\WpfDevTools.Inspector.csproj'
$inspectorSdkProject = Join-Path $repoRoot 'src\WpfDevTools.Inspector.Sdk\WpfDevTools.Inspector.Sdk.csproj'
$bootstrapperProject = Join-Path $repoRoot 'src\WpfDevTools.Bootstrapper\WpfDevTools.Bootstrapper.vcxproj'
$installScript = Join-Path $repoRoot 'scripts\online-installer.ps1'
$installBatchTemplate = Join-Path $repoRoot 'scripts\tools\packaging\run-template.bat'
$builtinComposerPackSource = Join-Path $repoRoot 'packs\builtin'
$composerPackBaselineSource = Join-Path $repoRoot 'packs\baselines'
$outputRootFullPath = (Resolve-Path (New-Item -ItemType Directory -Force -Path $OutputRoot)).Path
$msbuildPath = Resolve-MSBuildPath
$windowsSdkDirectory = Resolve-WindowsSdkDirectory
$windowsSdkVersion = Resolve-WindowsSdkVersion -WindowsSdkDirectory $windowsSdkDirectory

[xml]$serverProjectXml = Get-Content -Path $serverProject
$version = $serverProjectXml.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = '0.0.0-dev'
}

Assert-ExpectedReleaseTagMatchesVersion -Version $version -ExpectedReleaseTag $ExpectedReleaseTag -ProjectPath $serverProject
$nativeResourceVersion = ConvertTo-NativeResourceVersion -Version $version
$nativeResourceNumericVersion = ConvertTo-MSBuildPropertyValue -Value $nativeResourceVersion.Numeric

$resolvedArchitectures = Resolve-ArchitectureList -InputArchitectures $Architectures
Assert-ArchitectureToolchainAvailable `
    -ResolvedArchitectures $resolvedArchitectures `
    -ResolvedMsBuildPath $msbuildPath `
    -WindowsSdkDirectory $windowsSdkDirectory `
    -WindowsSdkVersion $windowsSdkVersion
foreach ($architecture in $resolvedArchitectures) {
    $runtimeId = Get-RuntimeId -Architecture $architecture
    $bootstrapperPlatform = Get-BootstrapperPlatform -Architecture $architecture
    $channel = Get-PackageChannel -BuildConfiguration $Configuration
    $packageDir = Join-Path $outputRootFullPath "release_${version}_win-$architecture"
    $packageArchiveName = "release_${version}_win-$architecture.zip"
    $packageArchivePath = Join-Path $outputRootFullPath $packageArchiveName
    $binDir = Join-Path $packageDir 'bin'
    $serverBuildSource = Resolve-ServerOutputSource -RepositoryRoot $repoRoot -BuildConfiguration $Configuration -RuntimeId $runtimeId -UseExistingBuildOutput $SkipBuild.IsPresent

    Remove-PathIfExists -Path $packageDir
    Remove-PathIfExists -Path $packageArchivePath

    New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
    $inspectorNet8Dir = Join-Path $binDir 'inspectors\net8.0-windows'
    $inspectorNet48Dir = Join-Path $binDir 'inspectors\net48'
    $bootstrapperDir = Join-Path $binDir (Join-Path 'bootstrapper' $architecture)
    New-Item -ItemType Directory -Force -Path $binDir, $inspectorNet8Dir, $inspectorNet48Dir, $bootstrapperDir | Out-Null

    try {
        if ($SkipBuild) {
            Copy-ServerBuildOutput -SourceInfo $serverBuildSource -Destination $binDir
        }
        else {
            Invoke-Step -FilePath 'dotnet' -Arguments @(
                'publish', $serverProject,
                '-c', $Configuration,
                '-r', $runtimeId,
                '--self-contained', 'false',
                '-o', $binDir
            )
        }

        if (-not $SkipBuild) {
            Invoke-Step -FilePath 'dotnet' -Arguments @(
                'build', $inspectorProject,
                '-c', $Configuration,
                '-f', 'net8.0-windows'
            )
            Invoke-Step -FilePath 'dotnet' -Arguments @(
                'build', $inspectorSdkProject,
                '-c', $Configuration,
                '-f', 'net8.0-windows',
                '-p:GeneratePackageOnBuild=false'
            )
        }

        Invoke-Step -FilePath 'dotnet' -Arguments @(
            'build', $inspectorProject,
            '-c', $Configuration,
            '-f', 'net48'
        )

        $bootstrapperBuildArguments = @(
            $bootstrapperProject,
            "/p:Configuration=$Configuration",
            "/p:Platform=$bootstrapperPlatform",
            "/p:BootstrapperFileVersion=$nativeResourceNumericVersion",
            "/p:BootstrapperProductVersion=$nativeResourceNumericVersion",
            "/p:BootstrapperFileVersionString=$($nativeResourceVersion.FileVersionString)",
            "/p:BootstrapperProductVersionString=$($nativeResourceVersion.ProductVersionString)",
            '/p:LinkIncremental=false'
        )
        $nativeBootstrapperBuildProperties = Get-NativeBootstrapperBuildProperties `
            -BootstrapperPlatform $bootstrapperPlatform `
            -ResolvedMsBuildPath $msbuildPath `
            -WindowsSdkDirectory $windowsSdkDirectory `
            -WindowsSdkVersion $windowsSdkVersion
        $includePath = $nativeBootstrapperBuildProperties.IncludePath
        $libraryPath = $nativeBootstrapperBuildProperties.LibraryPath
        $executablePath = $nativeBootstrapperBuildProperties.ExecutablePath
        if (-not [string]::IsNullOrWhiteSpace($windowsSdkDirectory)) {
            $bootstrapperBuildArguments += "/p:WindowsSDKDir=$windowsSdkDirectory"
        }
        if (-not [string]::IsNullOrWhiteSpace($windowsSdkVersion)) {
            $bootstrapperBuildArguments += "/p:WindowsTargetPlatformVersion=$windowsSdkVersion"
        }
        if (-not [string]::IsNullOrWhiteSpace($includePath)) {
            $bootstrapperBuildArguments += "/p:IncludePath=$includePath"
        }
        if (-not [string]::IsNullOrWhiteSpace($libraryPath)) {
            $bootstrapperBuildArguments += "/p:LibraryPath=$libraryPath"
        }
        if (-not [string]::IsNullOrWhiteSpace($executablePath)) {
            $bootstrapperBuildArguments += "/p:ExecutablePath=$executablePath"
        }

        Invoke-Step -FilePath $msbuildPath -Arguments $bootstrapperBuildArguments

        $inspectorNet8BuildDir = Join-Path $repoRoot "src\WpfDevTools.Inspector\bin\$Configuration\net8.0-windows"
        $inspectorNet48BuildDir = Join-Path $repoRoot "src\WpfDevTools.Inspector\bin\$Configuration\net48"
        $inspectorSdkBuildDir = Join-Path $repoRoot "src\WpfDevTools.Inspector.Sdk\bin\$Configuration\net8.0-windows"
        $bootstrapperSource = Join-Path $repoRoot "artifacts\bootstrapper\$Configuration\$bootstrapperPlatform\WpfDevTools.Bootstrapper.$architecture.dll"

        $packagedExecutableName = "wpf-devtools-$architecture.exe"
        $serverExecutablePath = Join-Path $binDir 'WpfDevTools.Mcp.Server.exe'
        if (Test-Path $serverExecutablePath) {
            Rename-Item -Path $serverExecutablePath -NewName $packagedExecutableName -Force
        }

        Copy-DirectoryFilesOnly -Source $inspectorNet8BuildDir -Destination $inspectorNet8Dir
        Copy-DirectoryContents -Source $inspectorNet48BuildDir -Destination $inspectorNet48Dir
        $inspectorSdkDestination = Join-Path $binDir 'WpfDevTools.Inspector.Sdk.dll'
        Copy-Item -Path (Join-Path $inspectorSdkBuildDir 'WpfDevTools.Inspector.Sdk.dll') -Destination $inspectorSdkDestination -Force
        $bootstrapperDestination = Join-Path $bootstrapperDir (Split-Path $bootstrapperSource -Leaf)
        Copy-Item -Path $bootstrapperSource -Destination $bootstrapperDestination -Force
        Copy-Item -Path $installBatchTemplate -Destination (Join-Path $packageDir 'run.bat') -Force
        Copy-Item -Path $installScript -Destination (Join-Path $binDir 'install.ps1') -Force
        Copy-InstallerHelperFiles -RepositoryRoot $repoRoot -Destination (Join-Path $binDir 'installer')
        Copy-DirectoryContents -Source $builtinComposerPackSource -Destination (Join-Path $packageDir 'packs\builtin')
        Copy-ComposerReadinessReports -Source $composerPackBaselineSource -Destination (Join-Path $packageDir 'packs\baselines')

        $payloadPaths = @(
            (Join-Path $binDir $packagedExecutableName)
            $inspectorSdkDestination
            (Join-Path $inspectorNet8Dir 'WpfDevTools.Inspector.dll')
            (Join-Path $inspectorNet48Dir 'WpfDevTools.Inspector.dll')
            $bootstrapperDestination
        )

        Invoke-ReleasePayloadSigning `
            -SignaturePolicy $signaturePolicy `
            -PayloadPaths $payloadPaths `
            -CertificatePathParameter $SigningCertificatePath `
            -CertificateThumbprintParameter $SigningCertificateThumbprint `
            -PasswordEnvironmentVariableParameter $SigningPasswordEnvironmentVariable `
            -TimestampServerParameter $SigningTimestampServer

        $payloadSigner = Assert-ReleasePayloadSignaturePolicy -SignaturePolicy $signaturePolicy -PayloadPaths $payloadPaths

        $manifest = [ordered]@{
            name = 'wpf-devtools'
            version = $version
            architecture = $architecture
            runtimeId = $runtimeId
            channel = $channel
            buildConfiguration = $Configuration
            signaturePolicy = $signaturePolicy
            entryExecutable = "bin/$packagedExecutableName"
            runBatch = 'run.bat'
            installScript = 'bin\install.ps1'
            composerPacks = 'packs/builtin'
            inspectorSdk = 'bin/WpfDevTools.Inspector.Sdk.dll'
            inspector = [ordered]@{
                net8 = 'bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll'
                net48 = 'bin/inspectors/net48/WpfDevTools.Inspector.dll'
            }
            bootstrapper = "bin/bootstrapper/$architecture/WpfDevTools.Bootstrapper.$architecture.dll"
        }

        if ($null -ne $payloadSigner -and -not [string]::IsNullOrWhiteSpace([string]$payloadSigner.Thumbprint)) {
            $manifest.signerThumbprint = [string]$payloadSigner.Thumbprint
        }

        if ($null -ne $payloadSigner -and -not [string]::IsNullOrWhiteSpace([string]$payloadSigner.Subject)) {
            $manifest.signerSubject = [string]$payloadSigner.Subject
        }

        $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $binDir 'manifest.json') -Encoding UTF8
        Invoke-ArchiveCreation `
            -PackageDirectory $packageDir `
            -ArchivePath $packageArchivePath
        Remove-PathIfExists -Path $packageDir
        Write-Host "Created archive: $packageArchivePath"
    }
    catch {
        $packagingError = $_.Exception
        $cleanupFailures = [System.Collections.Generic.List[string]]::new()

        try {
            Remove-PathIfExists -Path $packageArchivePath
        }
        catch {
            $cleanupFailures.Add($_.Exception.Message)
        }

        try {
            Remove-PathIfExists -Path $packageDir
        }
        catch {
            $cleanupFailures.Add($_.Exception.Message)
        }

        $failureMessage = "Failed to package architecture $architecture. $($packagingError.Message)"
        if ($cleanupFailures.Count -gt 0) {
            $failureMessage += " Cleanup also failed: $($cleanupFailures -join ' | ')"
        }

        throw $failureMessage
    }
}

Write-ReleaseSidecars -PackagingScriptRoot $PSScriptRoot -ArchiveRoot $outputRootFullPath -Version $version
