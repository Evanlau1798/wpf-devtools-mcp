function Get-DetectedInstallerRegistrations {
    return @(
        [ordered]@{
            ClientId = $null
            RegistrationMode = $null
            RegistrationTarget = $null
            InstalledExecutable = $null
            InstallRoot = $null
            Architecture = $null
            InstallerOwned = $false
            EvidenceSource = $null
        }
    )
}

function Get-DetectedCliRegistrations {
    # Placeholder contract: later tasks will inspect `claude mcp list` and `codex mcp list`.
    return @()
}
