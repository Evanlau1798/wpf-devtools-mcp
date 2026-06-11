function Resolve-HcsDiagPath {
    $command = Get-Command 'hcsdiag.exe' -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }

    return [string]$command.Source
}

function Initialize-ProcessPowerThrottlingApi {
    if ($null -ne ('SandboxCiProcessPowerThrottling' -as [type])) {
        return
    }

    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class SandboxCiProcessPowerThrottling
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetProcessInformation(
        IntPtr hProcess,
        int processInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE processInformation,
        int processInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(
        uint dwDesiredAccess,
        bool bInheritHandle,
        int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    public const int ProcessPowerThrottling = 4;
    public const uint PROCESS_SET_INFORMATION = 0x0200;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    public const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
}
'@
}

function Disable-ProcessPowerThrottling {
    param([Parameter(Mandatory = $true)] [System.Diagnostics.Process]$Process)

    try {
        Initialize-ProcessPowerThrottlingApi
        if ($Process.HasExited) {
            return
        }

        $state = New-Object 'SandboxCiProcessPowerThrottling+PROCESS_POWER_THROTTLING_STATE'
        $state.Version = [SandboxCiProcessPowerThrottling]::PROCESS_POWER_THROTTLING_CURRENT_VERSION
        $state.ControlMask = [SandboxCiProcessPowerThrottling]::PROCESS_POWER_THROTTLING_EXECUTION_SPEED
        $state.StateMask = 0

        $size = [System.Runtime.InteropServices.Marshal]::SizeOf($state)
        $access = [SandboxCiProcessPowerThrottling]::PROCESS_SET_INFORMATION -bor [SandboxCiProcessPowerThrottling]::PROCESS_QUERY_LIMITED_INFORMATION
        $processHandle = [SandboxCiProcessPowerThrottling]::OpenProcess($access, $false, $Process.Id)
        if ($processHandle -eq [IntPtr]::Zero) {
            $errorCode = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
            Write-Host "Could not open process handle for power throttling on $($Process.ProcessName)[$($Process.Id)]: Win32 $errorCode"
            return
        }

        try {
            $ok = [SandboxCiProcessPowerThrottling]::SetProcessInformation(
                $processHandle,
                [SandboxCiProcessPowerThrottling]::ProcessPowerThrottling,
                [ref]$state,
                $size)

            if (-not $ok) {
                $errorCode = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
                Write-Host "Could not disable power throttling for $($Process.ProcessName)[$($Process.Id)]: Win32 $errorCode"
            }
        }
        finally {
            [void][SandboxCiProcessPowerThrottling]::CloseHandle($processHandle)
        }
    }
    catch {
        Write-Host "Could not disable power throttling for $($Process.ProcessName)[$($Process.Id)]: $($_.Exception.Message)"
    }
}

function Get-WindowsSandboxComputeSystemIdsForScheduling {
    $hcsDiagPath = Resolve-HcsDiagPath
    if ([string]::IsNullOrWhiteSpace($hcsDiagPath)) {
        return @()
    }

    try {
        $lines = @(& $hcsDiagPath list 2>$null)
    }
    catch {
        Write-Host "hcsdiag list was unavailable while applying sandbox host scheduling: $($_.Exception.Message)"
        return @()
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host "hcsdiag list was unavailable while applying sandbox host scheduling: exit code $LASTEXITCODE"
        return @()
    }

    $ids = New-Object System.Collections.Generic.List[string]
    $lastId = ''
    foreach ($line in $lines) {
        $text = [string]$line
        if ($text -match '^[0-9a-fA-F-]{36}') {
            $lastId = $Matches[0]
            continue
        }

        if ([string]::IsNullOrWhiteSpace($lastId)) {
            continue
        }

        $fields = @($text -split ',' | ForEach-Object { $_.Trim() })
        if ($fields.Count -ge 4 -and [string]::Equals($fields[$fields.Count - 1], 'WindowsSandbox', [System.StringComparison]::OrdinalIgnoreCase)) {
            $ids.Add($lastId)
        }

        $lastId = ''
    }

    return $ids.ToArray()
}

function Get-SandboxVmWorkerProcesses {
    $computeSystemIds = @(Get-WindowsSandboxComputeSystemIdsForScheduling)
    if ($computeSystemIds.Count -eq 0) {
        return @()
    }

    try {
        $workers = @(Get-CimInstance Win32_Process -Filter "Name = 'vmwp.exe'" -ErrorAction Stop)
    }
    catch {
        Write-Host "Could not inspect vmwp.exe command lines: $($_.Exception.Message)"
        return @()
    }

    $matched = New-Object System.Collections.Generic.List[System.Diagnostics.Process]
    foreach ($worker in $workers) {
        $commandLine = [string]$worker.CommandLine
        foreach ($computeSystemId in $computeSystemIds) {
            if ($commandLine.IndexOf($computeSystemId, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
                continue
            }

            try {
                $matched.Add((Get-Process -Id ([int]$worker.ProcessId) -ErrorAction Stop))
            }
            catch {
                Write-Host "Could not open vmwp.exe[$($worker.ProcessId)]: $($_.Exception.Message)"
            }

            break
        }
    }

    return $matched.ToArray()
}

function Get-SandboxHostSchedulingProcesses {
    $byId = @{}
    $sandboxProcessNames = @(
        'WindowsSandbox',
        'WindowsSandboxClient',
        'WindowsSandboxRemoteSession',
        'WindowsSandboxServer'
    )

    foreach ($name in $sandboxProcessNames) {
        foreach ($process in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
            if (-not $byId.ContainsKey($process.Id)) {
                $byId[$process.Id] = $process
            }
        }
    }

    foreach ($process in @(Get-SandboxVmWorkerProcesses)) {
        if (-not $byId.ContainsKey($process.Id)) {
            $byId[$process.Id] = $process
        }
    }

    return @($byId.Values)
}

function Set-SandboxHostProcessScheduling {
    param(
        [Parameter(Mandatory = $true)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)] [System.Diagnostics.ProcessPriorityClass]$PriorityClass,
        [string]$ProcessorAffinityHex = ''
    )

    try {
        if (-not $Process.HasExited) {
            $Process.PriorityClass = $PriorityClass
            Write-Host "Set $($Process.ProcessName)[$($Process.Id)] priority to $PriorityClass."
        }
    }
    catch {
        Write-Host "Could not set priority for $($Process.ProcessName)[$($Process.Id)]: $($_.Exception.Message)"
    }

    if (-not [string]::IsNullOrWhiteSpace($ProcessorAffinityHex)) {
        try {
            $maskText = $ProcessorAffinityHex -replace '^0x', ''
            $mask = [Convert]::ToInt64($maskText, 16)
            if (-not $Process.HasExited) {
                $Process.ProcessorAffinity = [IntPtr]$mask
                Write-Host "Set $($Process.ProcessName)[$($Process.Id)] ProcessorAffinity to 0x$maskText."
            }
        }
        catch {
            Write-Host "Could not set affinity for $($Process.ProcessName)[$($Process.Id)]: $($_.Exception.Message)"
        }
    }

    Disable-ProcessPowerThrottling -Process $Process
}

function Set-SandboxHostScheduling {
    param(
        [Parameter(Mandatory = $true)] [string]$PriorityClass,
        [string]$ProcessorAffinityHex = ''
    )

    $priority = [System.Enum]::Parse([System.Diagnostics.ProcessPriorityClass], $PriorityClass)
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $scheduled = @{}
    Write-Host "Applying Windows Sandbox host scheduling: priority=$PriorityClass, powerThrottling=disabled, affinity=$ProcessorAffinityHex"

    do {
        foreach ($process in @(Get-SandboxHostSchedulingProcesses)) {
            if ($scheduled.ContainsKey($process.Id)) {
                continue
            }

            Set-SandboxHostProcessScheduling -Process $process -PriorityClass $priority -ProcessorAffinityHex $ProcessorAffinityHex
            $scheduled[$process.Id] = $true
        }

        Start-Sleep -Seconds 1
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($scheduled.Count -eq 0) {
        Write-Host 'No Windows Sandbox host processes were available for scheduling tuning.'
    }
    else {
        Write-Host "Applied Windows Sandbox host scheduling to $($scheduled.Count) process(es)."
    }
}
