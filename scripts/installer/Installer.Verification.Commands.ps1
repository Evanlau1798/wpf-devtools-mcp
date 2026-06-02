function Test-ArtifactRegistrationMatchesExecutable {
    param(
        [Parameter(Mandatory)] [string]$ArtifactPath,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    if (-not (Test-Path $ArtifactPath)) {
        return $false
    }

    $artifact = Get-Content -Path $ArtifactPath -Raw | ConvertFrom-Json
    $serverCollection = if ($null -ne $artifact.mcpServers) { $artifact.mcpServers } else { $artifact.servers }
    if ($null -eq $serverCollection -or $null -eq $serverCollection.'wpf-devtools') {
        return $false
    }

    return ([string]$serverCollection.'wpf-devtools'.command -eq $InstalledExecutable)
}

function Get-VerifiedWpfDevToolsExecutableFromText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    foreach ($rawLine in ($Text -split "`r?`n")) {
        $line = [string]$rawLine
        if ($line -notmatch '^\s*(?:[-*]\s*)?["'']?wpf-devtools["'']?(?:\s|:|=|$)') {
            continue
        }

        $match = [regex]::Match($line, '(?<path>[A-Za-z]:\\[^`"\r\n]*wpf-devtools-(x64|x86|arm64)\.exe)', 'IgnoreCase')
        if ($match.Success) {
            return [string]$match.Groups['path'].Value
        }
    }

    return $null
}

function Test-VerifiedInstallerPathEquals {
    param(
        [string]$Left,
        [string]$Right
    )

    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) {
        return $false
    }

    try {
        $leftFullPath = [System.IO.Path]::GetFullPath(([string]$Left).Trim().Trim('"')).TrimEnd('\', '/')
        $rightFullPath = [System.IO.Path]::GetFullPath(([string]$Right).Trim().Trim('"')).TrimEnd('\', '/')
        return [System.StringComparer]::OrdinalIgnoreCase.Equals($leftFullPath, $rightFullPath)
    }
    catch {
        return $false
    }
}

function Test-CliRegistrationMatchesExecutable {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    $verification = Invoke-VerificationCommand -Command $Command -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $true
    if (-not $verification.Succeeded) {
        return $verification
    }

    $registeredExecutable = Get-VerifiedWpfDevToolsExecutableFromText -Text ([string]$verification.Output)
    if ([string]::IsNullOrWhiteSpace($registeredExecutable)) {
        return [ordered]@{
            Succeeded = $false
            Output = "$Command mcp list contains wpf-devtools, but could not verify the registered executable path."
            ExitCode = 0
        }
    }

    if (-not (Test-VerifiedInstallerPathEquals -Left $registeredExecutable -Right $InstalledExecutable)) {
        return [ordered]@{
            Succeeded = $false
            Output = "$Command mcp list registered executable '$registeredExecutable' does not match '$InstalledExecutable'."
            ExitCode = 0
        }
    }

    return $verification
}

function Test-InstallerVerificationRunningElevated {
    $elevationResolver = Get-Command Test-InstallerRunningElevated -ErrorAction SilentlyContinue
    if ($null -ne $elevationResolver) {
        return [bool](Test-InstallerRunningElevated)
    }

    try {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        if ($null -eq $identity) {
            return $false
        }

        $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $false
    }
}

function Resolve-InstallerVerificationCommandPath {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [bool]$AllowPathResolution = $true
    )

    $registrationResolver = Get-Command Resolve-ExecutableCommandPath -ErrorAction SilentlyContinue
    if ($null -ne $registrationResolver) {
        return (Resolve-ExecutableCommandPath -Command $Command -AllowPathResolution:$AllowPathResolution)
    }

    if (-not $AllowPathResolution) {
        return $null
    }

    $resolvedCommands = @(Get-Command $Command -All -CommandType Application,ExternalScript -ErrorAction SilentlyContinue)
    foreach ($resolvedCommand in $resolvedCommands) {
        $candidatePath = if (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Path)) {
            [string]$resolvedCommand.Path
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Source)) {
            [string]$resolvedCommand.Source
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Definition)) {
            [string]$resolvedCommand.Definition
        }
        else {
            [string]$resolvedCommand.Name
        }

        if (-not [string]::IsNullOrWhiteSpace($candidatePath)) {
            return $candidatePath
        }
    }

    return $null
}

function Get-InstallerVerificationCommandBlockedMessage {
    param([Parameter(Mandatory)] [string]$Command)

    $messageBuilder = Get-Command Get-ElevatedCliCommandBlockMessage -ErrorAction SilentlyContinue
    if ($null -ne $messageBuilder) {
        return (Get-ElevatedCliCommandBlockMessage -Command $Command -ClientName $Command -OperationName 'verification')
    }

    return "Automatic $Command verification is blocked while the installer is elevated because resolving '$Command' from PATH is unsafe."
}

function Invoke-VerificationCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ExpectedToken,
        [Parameter(Mandatory)] [bool]$ExpectPresent
    )

    $isElevated = Test-InstallerVerificationRunningElevated
    $selectedCommandPath = $null
    try {
        $selectedCommandPath = Resolve-InstallerVerificationCommandPath -Command $Command -AllowPathResolution:(-not $isElevated)
    }
    catch {
        return [ordered]@{
            Succeeded = $false
            Output = $_.Exception.Message
            ExitCode = -1
        }
    }

    if ([string]::IsNullOrWhiteSpace($selectedCommandPath)) {
        return [ordered]@{
            Succeeded = $false
            Output = if ($isElevated) { Get-InstallerVerificationCommandBlockedMessage -Command $Command } else { "$Command is not installed." }
            ExitCode = -1
        }
    }

    $timeoutSeconds = Get-InstallerVerificationTimeoutSeconds
    $quotedArguments = @($Arguments | ForEach-Object {
            if ([string]::IsNullOrWhiteSpace([string]$_)) {
                '""'
            }
            elseif ([string]$_ -match '[\s"]') {
                '"' + ([string]$_).Replace('"', '\"') + '"'
            }
            else {
                [string]$_
            }
        })

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $filePath = $selectedCommandPath
    $argumentText = $quotedArguments -join ' '
    $selectedExtension = [System.IO.Path]::GetExtension($selectedCommandPath).ToLowerInvariant()
    if (@('.cmd', '.bat') -contains $selectedExtension) {
        $filePath = if (-not [string]::IsNullOrWhiteSpace($env:ComSpec)) { $env:ComSpec } else { 'cmd.exe' }
        $argumentText = '/c "' + $selectedCommandPath + '"'
        if (-not [string]::IsNullOrWhiteSpace($argumentText) -and $quotedArguments.Count -gt 0) {
            $argumentText += ' ' + ($quotedArguments -join ' ')
        }
    }
    elseif ($selectedExtension -eq '.ps1') {
        $filePath = (Get-Process -Id $PID).Path
        $argumentText = '-NoProfile -ExecutionPolicy Bypass -File "' + $selectedCommandPath + '"'
        if ($quotedArguments.Count -gt 0) {
            $argumentText += ' ' + ($quotedArguments -join ' ')
        }
    }

    $process = $null
    $exitCode = -3
    try {
        $startInfo.FileName = $filePath
        $startInfo.Arguments = $argumentText
        $process = New-Object System.Diagnostics.Process
        $process.StartInfo = $startInfo
        $null = $process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($timeoutSeconds * 1000)) {
            try {
                & taskkill.exe /PID $process.Id /T /F *> $null
            }
            catch {
            }

            try {
                $process.Refresh()
                if (-not $process.HasExited) {
                    $process.Kill($true)
                }
            }
            catch {
                try {
                    $process.Kill()
                }
                catch {
                }
            }

            $timeoutDrainMs = 1000
            try {
                $null = $process.WaitForExit($timeoutDrainMs)
            }
            catch {
            }

            $timeoutOutput = @()
            if ($stdoutTask.IsCompleted) {
                $timeoutOutput += $stdoutTask.GetAwaiter().GetResult()
            }
            if ($stderrTask.IsCompleted) {
                $timeoutOutput += $stderrTask.GetAwaiter().GetResult()
            }
            $timeoutOutput = ($timeoutOutput -join [Environment]::NewLine).Trim()

            return [ordered]@{
                Succeeded = $false
                Output = ("$Command timed out after $timeoutSeconds second(s). " + $timeoutOutput).Trim()
                ExitCode = -2
            }
        }

        $exitCode = $process.ExitCode
    }
    catch {
        return [ordered]@{
            Succeeded = $false
            Output = $_.Exception.Message
            ExitCode = -3
        }
    }
    finally {
        if ($null -ne $process) {
            $process.Dispose()
        }
    }

    $output = @(
        $stdoutTask.GetAwaiter().GetResult()
        $stderrTask.GetAwaiter().GetResult()
    ) -join [Environment]::NewLine
    $output = $output.Trim()
    if ($exitCode -ne 0) {
        return [ordered]@{
            Succeeded = $false
            Output = $output
            ExitCode = $exitCode
        }
    }

    $containsToken = -not [string]::IsNullOrWhiteSpace($output) -and $output.Contains($ExpectedToken)
    return [ordered]@{
        Succeeded = ($containsToken -eq $ExpectPresent)
        Output = $output
        ExitCode = $exitCode
    }
}
