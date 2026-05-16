[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$OutputRoot = '',

    [ValidateScript({
        -not [string]::IsNullOrWhiteSpace($_) -and
            $_ -eq [System.IO.Path]::GetFileName($_) -and
            $_ -notmatch '[\\/]'
    })]
    [string]$LogFileName = 'hcsdiag-kill.txt',

    [string]$HcsDiagPath = '',

    [ValidateRange(1, 120)]
    [int]$ShutdownTimeoutSeconds = 30,

    [switch]$SkipProcessTableWait
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $PSBoundParameters.ContainsKey('Confirm')) {
    $ConfirmPreference = 'None'
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $OutputRoot = Join-Path $repoRoot 'tmp\sandbox-ci\output'
}

function Write-CleanupLog {
    param(
        [Parameter(Mandatory = $true)] [string]$Path,
        [Parameter(Mandatory = $true)] [AllowEmptyString()] [string]$Message
    )

    Add-Content -LiteralPath $Path -Value $Message -Encoding UTF8 -WhatIf:$false -Confirm:$false
}

function Invoke-HcsDiag {
    param(
        [Parameter(Mandatory = $true)] [string]$Path,
        [Parameter(Mandatory = $true)] [string[]]$Arguments,
        [Parameter(Mandatory = $true)] [string]$LogPath
    )

    $output = @(& $Path @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    foreach ($line in $output) {
        Write-CleanupLog -Path $LogPath -Message ([string]$line)
    }

    if ($exitCode -ne 0) {
        throw "hcsdiag.exe $($Arguments -join ' ') failed with exit code $exitCode."
    }

    return $output
}

function Test-WindowsSandboxDetailLine {
    param(
        [Parameter(Mandatory = $true)] [string]$Line,
        [Parameter(Mandatory = $true)] [string]$ExpectedId
    )

    $fields = @($Line -split ',' | ForEach-Object { $_.Trim() })
    if ($fields.Count -lt 4) {
        return $false
    }

    $idMatches = @($fields | Where-Object {
        $_ -match '^[0-9a-fA-F-]{36}$' -and
        [string]::Equals($_, $ExpectedId, [System.StringComparison]::OrdinalIgnoreCase)
    })

    return [string]::Equals($fields[0], 'VM', [System.StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals($fields[$fields.Count - 1], 'WindowsSandbox', [System.StringComparison]::OrdinalIgnoreCase) -and
        $idMatches.Count -eq 1
}

function Get-WindowsSandboxComputeSystems {
    param(
        [Parameter(Mandatory = $true)] [string]$HcsDiagPath,
        [Parameter(Mandatory = $true)] [string]$LogPath
    )

    $systems = New-Object System.Collections.Generic.List[object]
    $lastId = $null

    Write-CleanupLog -Path $LogPath -Message '--- hcsdiag list ---'
    $lines = Invoke-HcsDiag -Path $HcsDiagPath -Arguments @('list') -LogPath $LogPath
    foreach ($line in $lines) {
        $text = [string]$line
        if ([string]::IsNullOrWhiteSpace($text)) {
            continue
        }

        if ($text -match '^[0-9a-fA-F-]{36}') {
            $lastId = $Matches[0]
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($lastId) -and (Test-WindowsSandboxDetailLine -Line $text -ExpectedId $lastId)) {
            $systems.Add([pscustomobject]@{
                Id = $lastId
                Details = $text.Trim()
            })
        }

        $lastId = $null
    }

    return $systems
}

function Wait-WindowsSandboxShutdown {
    param(
        [Parameter(Mandatory = $true)] [string]$HcsDiagPath,
        [Parameter(Mandatory = $true)] [string]$LogPath,
        [Parameter(Mandatory = $true)] [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $remaining = @(Get-WindowsSandboxComputeSystems -HcsDiagPath $HcsDiagPath -LogPath $LogPath)
        if ($remaining.Count -eq 0) {
            Write-CleanupLog -Path $LogPath -Message 'All Windows Sandbox HCS compute systems are closed.'
            return
        }

        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    $ids = ($remaining | ForEach-Object { $_.Id }) -join ', '
    throw "Windows Sandbox HCS compute systems did not close within $TimeoutSeconds seconds: $ids"
}

function Get-WindowsSandboxProcesses {
    $sandboxProcessNames = @(
        'WindowsSandbox',
        'WindowsSandboxClient',
        'WindowsSandboxRemoteSession',
        'WindowsSandboxServer'
    )

    return @(Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $sandboxProcessNames -contains $_.ProcessName })
}

function Wait-WindowsSandboxProcessesExit {
    param(
        [Parameter(Mandatory = $true)] [string]$LogPath,
        [Parameter(Mandatory = $true)] [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $processes = @(Get-WindowsSandboxProcesses)
        if ($processes.Count -eq 0) {
            Write-CleanupLog -Path $LogPath -Message 'All Windows Sandbox processes are closed.'
            return
        }

        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    $ids = ($processes | ForEach-Object { "$($_.ProcessName):$($_.Id)" }) -join ', '
    throw "Windows Sandbox processes did not close within $TimeoutSeconds seconds: $ids"
}

if ([string]::IsNullOrWhiteSpace($HcsDiagPath)) {
    $hcsdiagCommand = Get-Command 'hcsdiag.exe' -ErrorAction SilentlyContinue
    if ($null -eq $hcsdiagCommand) {
        throw 'hcsdiag.exe was not found. This script requires Windows Sandbox / HCS tooling.'
    }

    $HcsDiagPath = [string]$hcsdiagCommand.Source
}

New-Item -ItemType Directory -Force -Path $OutputRoot -WhatIf:$false -Confirm:$false | Out-Null
$logPath = Join-Path $OutputRoot $LogFileName
Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue -WhatIf:$false -Confirm:$false

Write-CleanupLog -Path $logPath -Message "HCS cleanup started: $(Get-Date -Format o)"
Write-CleanupLog -Path $logPath -Message "hcsdiag path: $HcsDiagPath"

$sandboxSystems = @(Get-WindowsSandboxComputeSystems -HcsDiagPath $HcsDiagPath -LogPath $logPath)
if ($sandboxSystems.Count -eq 0) {
    Write-CleanupLog -Path $logPath -Message 'No matching Windows Sandbox HCS compute systems were found.'
}

foreach ($sandboxSystem in $sandboxSystems) {
    $sandboxId = [string]$sandboxSystem.Id
    $target = "Windows Sandbox HCS compute system $sandboxId ($($sandboxSystem.Details))"
    if ($PSCmdlet.ShouldProcess($target, 'hcsdiag kill')) {
        Write-CleanupLog -Path $logPath -Message "--- hcsdiag kill $sandboxId ---"
        Invoke-HcsDiag -Path $HcsDiagPath -Arguments @('kill', $sandboxId) -LogPath $logPath | Out-Null
    }
    else {
        Write-CleanupLog -Path $logPath -Message "Skipped by ShouldProcess: $sandboxId"
    }
}

if (-not $WhatIfPreference) {
    if ($sandboxSystems.Count -gt 0) {
        Wait-WindowsSandboxShutdown -HcsDiagPath $HcsDiagPath -LogPath $logPath -TimeoutSeconds $ShutdownTimeoutSeconds
    }

    if ($SkipProcessTableWait) {
        Write-CleanupLog -Path $logPath -Message 'Skipped Windows Sandbox process-table wait because SkipProcessTableWait was provided.'
    }
    else {
        Wait-WindowsSandboxProcessesExit -LogPath $logPath -TimeoutSeconds $ShutdownTimeoutSeconds
    }
}

Write-CleanupLog -Path $logPath -Message '--- remaining sandbox processes ---'
Get-WindowsSandboxProcesses |
    Select-Object ProcessName, Id, StartTime, CPU |
    Format-Table -AutoSize |
    Out-String |
    Add-Content -LiteralPath $logPath -Encoding UTF8 -WhatIf:$false -Confirm:$false

Write-CleanupLog -Path $logPath -Message "HCS cleanup ended: $(Get-Date -Format o)"
Write-Host "Windows Sandbox HCS cleanup log: $logPath"
