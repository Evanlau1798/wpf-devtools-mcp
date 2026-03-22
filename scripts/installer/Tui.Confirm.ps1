function New-TuiConfirmationState {
    return [ordered]@{
        CurrentScreen = 'ConfirmScreen'
        ConfirmationStep = 1
        ConfirmationMode = 'unregister'
        UnregisterTarget = $null
        FullUninstall = $false
    }
}
