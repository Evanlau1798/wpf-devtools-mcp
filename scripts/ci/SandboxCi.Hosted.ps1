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

    Invoke-External 'dotnet restore --locked-mode' $DotNetPath @('restore', '--locked-mode', '-p:NuGetAudit=true')
    Invoke-NativeFullVerification -DotNetPath $DotNetPath -OutputRoot $OutputRoot -Timestamp $Timestamp -SkipDllLink

    foreach ($configuration in @('Debug', 'Release')) {
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
    }
}
