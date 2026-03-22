function Test-TuiSupportCore {
    if ($NonInteractive -or $OutputJson) {
        return $false
    }

    try {
        $null = $Host.UI.RawUI.WindowTitle
        return -not [Console]::IsInputRedirected
    }
    catch {
        return $false
    }
}

function Read-TuiKeyCore {
    try {
        $keyInfo = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
        return [ordered]@{
            Key = [ConsoleKey]$keyInfo.VirtualKeyCode
            Character = [string]$keyInfo.Character
        }
    }
    catch {
        $read = [Console]::ReadKey($true)
        return [ordered]@{
            Key = [ConsoleKey]$read.Key
            Character = [string]$read.KeyChar
        }
    }
}

function Invoke-TuiInstallRootPromptCore {
    param([Parameter(Mandatory)] $State)

    try {
        Write-Host ''
        $response = Read-InstallerInput -Prompt 'Install location' -DefaultValue ([string]$State.InstallRoot)
        if (-not [string]::IsNullOrWhiteSpace($response)) {
            $State.InstallRoot = $response.Trim()
            $State.StatusMessage = "Install location updated to $($State.InstallRoot)."
        }
    }
    catch {
        $State.StatusMessage = "Unable to update install location: $($_.Exception.Message)"
    }

    return $State
}
