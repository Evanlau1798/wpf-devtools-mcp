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
                $startupSnapshots + @(Get-DescendantProcessSnapshots -ParentProcessId $process.Id -CreationStartUtcTicks (Get-ProcessSnapshotStartCutoff -Snapshot $rootSnapshot))))
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
        [long]$CreationStartUtcTicks = 0,
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
    $creationStartUtcTicks = $CreationStartUtcTicks - [TimeSpan]::FromMilliseconds(1).Ticks
    foreach ($child in @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $ParentProcessId" -ErrorAction SilentlyContinue)) {
        $childId = [int]$child.ProcessId
        $childTicks = ([datetime]$child.CreationDate).ToUniversalTime().Ticks
        if ($childTicks -lt $creationStartUtcTicks -or $childTicks -gt $CreationCutoffUtcTicks) {
            continue
        }

        $childSnapshot = [pscustomobject]@{
            ProcessId = $childId
            CreationDateUtcTicks = $childTicks
            DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks
        }
        if (-not (Test-ProcessSnapshotExists -Snapshot $childSnapshot)) {
            continue
        }

        $snapshots += $childSnapshot
        $snapshots += Get-DescendantProcessSnapshots -ParentProcessId $childId -CreationCutoffUtcTicks $CreationCutoffUtcTicks -CreationStartUtcTicks $childTicks -VisitedProcessIds $visited
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
        if ($process.HasExited -or -not (Test-ProcessSnapshotExists -Snapshot $Snapshot)) {
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

function Get-ProcessSnapshotStartCutoff { param([object]$Snapshot) if ($null -eq $Snapshot) { return 0 }; return [long]$Snapshot.CreationDateUtcTicks }

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
    param([object]$Snapshot, [object]$Process)
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

        $descendants = @(Get-DescendantProcessSnapshots -ParentProcessId $scanRoot.ProcessId -CreationCutoffUtcTicks $cutoff -CreationStartUtcTicks (Get-ProcessSnapshotStartCutoff -Snapshot $scanRoot))
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

        try { if (-not $process.HasExited) { Update-ProcessSnapshotCutoffIfAlive -Snapshot $snapshot | Out-Null; $process.Kill(); $process.WaitForExit(1000) | Out-Null; if (Test-ProcessSnapshotExists -Snapshot $snapshot) { $process.Kill(); $process.WaitForExit(1000) | Out-Null } } }
        catch { $script:LastProcessSnapshotStopFailure = $_.Exception.Message }
        finally { if (-not (Update-ProcessSnapshotCutoffFromProcess -Snapshot $snapshot -Process $process)) { Update-ProcessSnapshotCutoffIfAlive -Snapshot $snapshot | Out-Null }; $process.Dispose() }
    }
}

function Stop-ProcessSnapshots {
    param([object[]]$Snapshots, [object[]]$ScanRoots = @())
    $script:LastProcessSnapshotStopFailure = ''; $deadline = [DateTime]::UtcNow.AddSeconds(15); $settleSinceUtc = $null; $lastLiveKey = ''
    do {
        $Snapshots = @(Expand-ProcessSnapshots -Snapshots $Snapshots -ScanRoots $ScanRoots)
        Stop-ExistingProcessSnapshots -Snapshots $Snapshots
        $Snapshots = @(Expand-ProcessSnapshots -Snapshots $Snapshots -ScanRoots $ScanRoots)
        $liveSnapshots = @($Snapshots | Where-Object { Test-ProcessSnapshotExists -Snapshot $_ }); $liveKey = (($liveSnapshots | Sort-Object ProcessId, CreationDateUtcTicks | ForEach-Object { Get-ProcessSnapshotKey -Snapshot $_ }) -join '|')
        if ($liveSnapshots.Count -eq 0 -and @($ScanRoots).Count -eq 0) { return }
        if ($liveSnapshots.Count -eq 0) {
            if ($null -eq $settleSinceUtc) { $settleSinceUtc = [DateTime]::UtcNow } elseif ([DateTime]::UtcNow -ge $settleSinceUtc.AddMilliseconds(500)) { return }
        }
        else { $settleSinceUtc = $null; $lastLiveKey = $liveKey }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    $remaining = @($Snapshots | Where-Object { Test-ProcessSnapshotExists -Snapshot $_ } | ForEach-Object { $_.ProcessId })
    $scanRootKeys = ((@($ScanRoots) | ForEach-Object { Get-ProcessSnapshotKey -Snapshot $_ }) -join ', ')
    if ($remaining.Count -eq 0) { return }
    throw "Timed out waiting for process tree cleanup. Remaining PID(s): $($remaining -join ', ') Scan root key(s): $scanRootKeys Current live key: $liveKey Last stop failure: $script:LastProcessSnapshotStopFailure"
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
                $descendantSnapshots += @(Get-DescendantProcessSnapshots -ParentProcessId $processId -CreationStartUtcTicks (Get-ProcessSnapshotStartCutoff -Snapshot $rootSnapshot))
                $Process.CloseMainWindow() | Out-Null
                $deadline = [DateTime]::UtcNow.AddSeconds(5)
                while (-not $Process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
                    $descendantSnapshots += @(Get-DescendantProcessSnapshots -ParentProcessId $processId -CreationStartUtcTicks (Get-ProcessSnapshotStartCutoff -Snapshot $rootSnapshot))
                    Start-Sleep -Milliseconds 100
                    $Process.Refresh()
                }

                if (-not $Process.HasExited) {
                    $descendantSnapshots += @(Get-DescendantProcessSnapshots -ParentProcessId $processId -CreationStartUtcTicks (Get-ProcessSnapshotStartCutoff -Snapshot $rootSnapshot))
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
