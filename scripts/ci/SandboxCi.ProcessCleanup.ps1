$ErrorActionPreference = 'Stop'
$script:SmokeTargetKnownSnapshotsByKey = @{}
$script:SmokeTargetRootSnapshotsByProcessId = @{}

function Start-SmokeTarget {
    if ([string]::IsNullOrWhiteSpace($SmokeTargetPath)) {
        return $null
    }

    $script:resolvedSmokeTargetPath = (Resolve-Path -LiteralPath $SmokeTargetPath).Path
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $script:resolvedSmokeTargetPath
    $startInfo.WorkingDirectory = [System.IO.Path]::GetDirectoryName($script:resolvedSmokeTargetPath)
    $startInfo.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Failed to start smoke target: $script:resolvedSmokeTargetPath"
    }

    $rootSnapshot = New-ProcessSnapshotFromProcess -Process $process
    Set-SmokeTargetRootSnapshot -ProcessId $process.Id -RootSnapshot $rootSnapshot
    try {
        $startupSnapshots = @()
        $deadline = [DateTime]::UtcNow.AddSeconds($SmokeTargetStartupTimeoutSeconds)
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($process.HasExited) {
                throw "Smoke target exited before its main window was ready: $script:resolvedSmokeTargetPath"
            }

            $process.Refresh()
            if ($null -eq $rootSnapshot) {
                $rootSnapshot = New-ProcessSnapshot -ProcessId $process.Id
            }

            $startupSnapshots = @(Merge-ProcessSnapshots -Snapshots @(
                $startupSnapshots + @(Get-DescendantProcessSnapshots -ParentProcessId $process.Id)))
            if ($null -ne $rootSnapshot) {
                $startupSnapshots = @(Expand-ProcessSnapshots -Snapshots $startupSnapshots -ScanRoots @($rootSnapshot))
            }
            else {
                $startupSnapshots = @(Expand-ProcessSnapshots -Snapshots $startupSnapshots)
            }

            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                Set-SmokeTargetRootSnapshot -ProcessId $process.Id -RootSnapshot $rootSnapshot
                Set-SmokeTargetKnownSnapshots -RootSnapshot $rootSnapshot -Snapshots $startupSnapshots
                return $process
            }

            Start-Sleep -Milliseconds 250
        }

        throw "Timed out waiting for smoke target main window: $script:resolvedSmokeTargetPath"
    }
    catch {
        $startupFailure = $_.Exception.Message
        try {
            Stop-SmokeTarget -Process $process -KnownSnapshots $startupSnapshots
        }
        catch {
            throw "Smoke target startup failed and cleanup failed. Startup failure: $startupFailure Cleanup failure: $($_.Exception.Message)"
        }

        throw
    }
}

function Get-SmokeTargetProcessId {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process) {
        return 0
    }

    try {
        $processId = [int]$Process.Id
        if ($processId -le 0) {
            return 0
        }

        return $processId
    }
    catch {
        return 0
    }
}

function Set-SmokeTargetRootSnapshot {
    param([int]$ProcessId, [object]$RootSnapshot)

    if ($ProcessId -le 0 -or $null -eq $RootSnapshot) {
        return
    }

    if ($null -eq $script:SmokeTargetRootSnapshotsByProcessId) {
        $script:SmokeTargetRootSnapshotsByProcessId = @{}
    }

    $script:SmokeTargetRootSnapshotsByProcessId[$ProcessId] = $RootSnapshot
}

function Get-SmokeTargetRootSnapshot {
    param([int]$ProcessId)

    if ($null -eq $script:SmokeTargetRootSnapshotsByProcessId) {
        $script:SmokeTargetRootSnapshotsByProcessId = @{}
    }

    if ($ProcessId -gt 0 -and $script:SmokeTargetRootSnapshotsByProcessId.ContainsKey($ProcessId)) {
        return $script:SmokeTargetRootSnapshotsByProcessId[$ProcessId]
    }

    return $null
}

function Remove-SmokeTargetRootSnapshot {
    param([int]$ProcessId)

    if ($null -eq $script:SmokeTargetRootSnapshotsByProcessId) {
        $script:SmokeTargetRootSnapshotsByProcessId = @{}
    }

    if ($ProcessId -gt 0) {
        $script:SmokeTargetRootSnapshotsByProcessId.Remove($ProcessId)
    }
}

function New-ProcessSnapshot {
    param([Parameter(Mandatory = $true)] [int]$ProcessId)

    if ($ProcessId -le 0) {
        return $null
    }

    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return $null
    }

    $creationTicks = ([datetime]$process.CreationDate).ToUniversalTime().Ticks
    return [pscustomobject]@{
        ProcessId = [int]$process.ProcessId
        CreationDateUtcTicks = $creationTicks
        DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks
    }
}

function New-ProcessSnapshotFromProcess {
    param([System.Diagnostics.Process]$Process)
    $processId = Get-SmokeTargetProcessId -Process $Process
    if ($processId -le 0) {
        return $null
    }

    try {
        $cutoffTicks = [DateTime]::UtcNow.Ticks
        if ($Process.HasExited) {
            $exitTicks = $Process.ExitTime.ToUniversalTime().Ticks
            if ($exitTicks -gt $cutoffTicks) { $cutoffTicks = $exitTicks }
        }

        return [pscustomobject]@{
            ProcessId = $processId
            CreationDateUtcTicks = $Process.StartTime.ToUniversalTime().Ticks
            DescendantCutoffUtcTicks = $cutoffTicks
        }
    }
    catch { return $null }
}

function Get-ProcessSnapshotKey {
    param([object]$Snapshot)

    if ($null -eq $Snapshot) {
        return ''
    }

    return "$($Snapshot.ProcessId):$($Snapshot.CreationDateUtcTicks)"
}

function Set-SmokeTargetKnownSnapshots {
    param([object]$RootSnapshot, [object[]]$Snapshots)

    if ($null -eq $script:SmokeTargetKnownSnapshotsByKey) {
        $script:SmokeTargetKnownSnapshotsByKey = @{}
    }

    $key = Get-ProcessSnapshotKey -Snapshot $RootSnapshot
    if ([string]::IsNullOrWhiteSpace($key)) {
        return
    }

    $script:SmokeTargetKnownSnapshotsByKey[$key] = @(Merge-ProcessSnapshots -Snapshots $Snapshots)
}

function Get-SmokeTargetKnownSnapshots {
    param([object]$RootSnapshot)

    if ($null -eq $script:SmokeTargetKnownSnapshotsByKey) {
        $script:SmokeTargetKnownSnapshotsByKey = @{}
    }

    $key = Get-ProcessSnapshotKey -Snapshot $RootSnapshot
    if ([string]::IsNullOrWhiteSpace($key) -or -not $script:SmokeTargetKnownSnapshotsByKey.ContainsKey($key)) {
        return @()
    }

    return @($script:SmokeTargetKnownSnapshotsByKey[$key])
}

function Remove-SmokeTargetKnownSnapshots {
    param([object]$RootSnapshot)

    if ($null -eq $script:SmokeTargetKnownSnapshotsByKey) {
        $script:SmokeTargetKnownSnapshotsByKey = @{}
    }

    $key = Get-ProcessSnapshotKey -Snapshot $RootSnapshot
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        $script:SmokeTargetKnownSnapshotsByKey.Remove($key)
    }
}

function Get-DescendantProcessSnapshots {
    param(
        [Parameter(Mandatory = $true)] [int]$ParentProcessId,
        [long]$CreationCutoffUtcTicks = [long]::MaxValue,
        [int[]]$VisitedProcessIds = @()
    )

    if ($ParentProcessId -le 0) {
        return @()
    }

    if ($VisitedProcessIds -contains $ParentProcessId) {
        return @()
    }

    $visited = @($VisitedProcessIds + $ParentProcessId)
    $snapshots = @()
    foreach ($child in @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $ParentProcessId" -ErrorAction SilentlyContinue)) {
        $childId = [int]$child.ProcessId
        $childTicks = ([datetime]$child.CreationDate).ToUniversalTime().Ticks
        if ($childTicks -gt $CreationCutoffUtcTicks) {
            continue
        }

        $snapshots += Get-DescendantProcessSnapshots -ParentProcessId $childId -CreationCutoffUtcTicks $CreationCutoffUtcTicks -VisitedProcessIds $visited
        $snapshots += [pscustomobject]@{
            ProcessId = $childId
            CreationDateUtcTicks = $childTicks
            DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks
        }
    }

    return $snapshots
}

function Test-ProcessSnapshotExists {
    param([Parameter(Mandatory = $true)] [object]$Snapshot)

    if ($null -eq $Snapshot) {
        return $false
    }

    $current = Get-CimInstance Win32_Process -Filter "ProcessId = $($Snapshot.ProcessId)" -ErrorAction SilentlyContinue
    if ($null -eq $current) {
        return $false
    }

    $creationDeltaTicks = [Math]::Abs(([datetime]$current.CreationDate).ToUniversalTime().Ticks - [long]$Snapshot.CreationDateUtcTicks)
    return $creationDeltaTicks -le [TimeSpan]::FromMilliseconds(1).Ticks
}

function Get-MatchingProcessFromSnapshot {
    param([object]$Snapshot)

    if ($null -eq $Snapshot) {
        return $null
    }

    $process = $null
    try {
        $process = [System.Diagnostics.Process]::GetProcessById([int]$Snapshot.ProcessId)
        $startDeltaTicks = [Math]::Abs($process.StartTime.ToUniversalTime().Ticks - [long]$Snapshot.CreationDateUtcTicks)
        if ($process.HasExited -or $startDeltaTicks -gt [TimeSpan]::FromMilliseconds(1).Ticks) {
            $process.Dispose()
            return $null
        }

        return $process
    }
    catch {
        if ($null -ne $process) {
            $process.Dispose()
        }

        return $null
    }
}

function Merge-ProcessSnapshots {
    param([object[]]$Snapshots)

    return @($Snapshots |
        Where-Object { $null -ne $_ } |
        Group-Object ProcessId, CreationDateUtcTicks |
        ForEach-Object { @($_.Group | Sort-Object DescendantCutoffUtcTicks -Descending)[0] })
}

function Get-ScanRootCutoff {
    param([Parameter(Mandatory = $true)] [object]$ScanRoot)

    Update-ProcessSnapshotCutoffIfAlive -Snapshot $ScanRoot | Out-Null

    if ($ScanRoot.PSObject.Properties.Name -notcontains 'DescendantCutoffUtcTicks') {
        return 0
    }

    return [long]$ScanRoot.DescendantCutoffUtcTicks
}

function Update-ProcessSnapshotCutoffIfAlive {
    param([object]$Snapshot)

    if ($null -eq $Snapshot) {
        return $false
    }

    if (Test-ProcessSnapshotExists -Snapshot $Snapshot) {
        $Snapshot.DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks
        return $true
    }

    return $false
}

function Update-ProcessSnapshotCutoffFromProcess {
    param([object]$Snapshot, [System.Diagnostics.Process]$Process)
    if ($null -eq $Snapshot -or $null -eq $Process) {
        return $false
    }

    try {
        if ($Process.HasExited) {
            $Snapshot.DescendantCutoffUtcTicks = [Math]::Max([long]$Snapshot.DescendantCutoffUtcTicks, [Math]::Max($Process.ExitTime.ToUniversalTime().Ticks, [DateTime]::UtcNow.Ticks))
            return $true
        }
    }
    catch {}
    return $false
}

function Expand-ProcessSnapshots {
    param([object[]]$Snapshots, [object[]]$ScanRoots = @())

    $Snapshots = @(Merge-ProcessSnapshots -Snapshots $Snapshots)
    foreach ($scanRoot in @(@($ScanRoots) + @($Snapshots) | Where-Object { $null -ne $_ })) {
        $cutoff = Get-ScanRootCutoff -ScanRoot $scanRoot
        if ($cutoff -le 0) {
            continue
        }

        $descendants = @(Get-DescendantProcessSnapshots -ParentProcessId $scanRoot.ProcessId -CreationCutoffUtcTicks $cutoff)
        $Snapshots = @(Merge-ProcessSnapshots -Snapshots @($Snapshots + $descendants))
    }

    return $Snapshots
}

function Stop-ExistingProcessSnapshots {
    param([object[]]$Snapshots)

    foreach ($snapshot in $Snapshots) {
        $process = Get-MatchingProcessFromSnapshot -Snapshot $snapshot
        if ($null -eq $process) {
            continue
        }

        try {
            if (-not $process.HasExited) {
                $process.Kill()
            }
        }
        catch {
        }
        finally {
            $process.Dispose()
        }
    }
}

function Stop-ProcessSnapshots {
    param([object[]]$Snapshots, [object[]]$ScanRoots = @())

    $Snapshots = @(Expand-ProcessSnapshots -Snapshots $Snapshots -ScanRoots $ScanRoots)
    Stop-ExistingProcessSnapshots -Snapshots $Snapshots
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $Snapshots = @(Expand-ProcessSnapshots -Snapshots $Snapshots -ScanRoots $ScanRoots)
        Stop-ExistingProcessSnapshots -Snapshots $Snapshots
        $remaining = @($Snapshots | Where-Object { Test-ProcessSnapshotExists -Snapshot $_ } | ForEach-Object { $_.ProcessId })
        if ($remaining.Count -eq 0) {
            return
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Timed out waiting for process tree cleanup. Remaining PID(s): $($remaining -join ', ')"
}

function Stop-SmokeTarget {
    param(
        [System.Diagnostics.Process]$Process,
        [object[]]$KnownSnapshots = @()
    )

    if ($null -eq $Process) {
        return
    }

    $processId = Get-SmokeTargetProcessId -Process $Process
    if ($processId -le 0) {
        return
    }

    $rootSnapshot = Get-SmokeTargetRootSnapshot -ProcessId $processId
    if ($null -eq $rootSnapshot) {
        $rootSnapshot = New-ProcessSnapshot -ProcessId $processId
    }

    $rootShutdownFailure = $null
    $descendantCleanupFailure = $null
    $descendantSnapshots = @(@($KnownSnapshots) + @(Get-SmokeTargetKnownSnapshots -RootSnapshot $rootSnapshot))
    try {
        try {
            $processIsRunning = $false
            try {
                $processIsRunning = -not $Process.HasExited
            }
            catch {
            }

            if ($processIsRunning) {
                $descendantSnapshots += @(Get-DescendantProcessSnapshots -ParentProcessId $processId)
                $Process.CloseMainWindow() | Out-Null
                $deadline = [DateTime]::UtcNow.AddSeconds(5)
                while (-not $Process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
                    $descendantSnapshots += @(Get-DescendantProcessSnapshots -ParentProcessId $processId)
                    Start-Sleep -Milliseconds 100
                    $Process.Refresh()
                }

                if (-not $Process.HasExited) {
                    $descendantSnapshots += @(Get-DescendantProcessSnapshots -ParentProcessId $processId)
                    $Process.Kill()
                    if (-not $Process.WaitForExit(5000)) {
                        throw "Smoke target did not exit after force kill: $processId"
                    }
                }
            }
        }
        catch {
            if (Test-ProcessSnapshotExists -Snapshot $rootSnapshot) {
                $rootShutdownFailure = $_.Exception.Message
            }
        }
        finally {
            if (-not (Update-ProcessSnapshotCutoffFromProcess -Snapshot $rootSnapshot -Process $Process)) {
                Update-ProcessSnapshotCutoffIfAlive -Snapshot $rootSnapshot | Out-Null
            }

            try {
                Stop-ProcessSnapshots -Snapshots @(@($rootSnapshot) + @($descendantSnapshots)) -ScanRoots @($rootSnapshot)
            }
            catch {
                $descendantCleanupFailure = $_.Exception.Message
            }
        }

        if ($null -ne $rootShutdownFailure -and $null -ne $descendantCleanupFailure) {
            throw "Smoke target cleanup failed. Root shutdown failure: $rootShutdownFailure Descendant cleanup failure: $descendantCleanupFailure"
        }

        if ($null -ne $rootShutdownFailure) {
            throw $rootShutdownFailure
        }

        if ($null -ne $descendantCleanupFailure) {
            throw $descendantCleanupFailure
        }
    }
    finally {
        Remove-SmokeTargetRootSnapshot -ProcessId $processId
        Remove-SmokeTargetKnownSnapshots -RootSnapshot $rootSnapshot
        try { $Process.Dispose() } catch {}
    }
}
