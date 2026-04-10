function Get-TuiStatusBarStateCore {
    param([Parameter(Mandatory)] $State)

    $panelLabel = 'Status'
    $panelText = [string]$State.StatusMessage

    if ([string]$State.CurrentScreen -eq 'ConfirmScreen') {
        $panelText = if ([string]$State.ConfirmationMode -eq 'close-app') {
            'Press Enter once to close the installer, or Escape to stay on the home screen.'
        }
        else {
            'Press Enter twice to proceed, or Escape to cancel the current confirmation.'
        }
    }
    elseif (([string]$State.CurrentScreen -ne 'HomeScreen') -and
        ([string]::IsNullOrWhiteSpace($panelText) -or $panelText -eq 'Use Up/Down to choose an action.')) {
        $items = @(Get-TuiCurrentItems -State $State)
        if (([int]$State.SelectionIndex -ge 0) -and ([int]$State.SelectionIndex -lt $items.Count)) {
            $selectedItem = $items[[int]$State.SelectionIndex]
            $statusText = ''
            if ($selectedItem -is [System.Collections.IDictionary] -and $selectedItem.Contains('StatusText')) {
                $statusText = [string]$selectedItem.StatusText
            }
            elseif ($null -ne $selectedItem.PSObject.Properties['StatusText']) {
                $statusText = [string]$selectedItem.StatusText
            }

            $panelText = if (-not [string]::IsNullOrWhiteSpace($statusText)) {
                $statusText
            }
            else {
                [string]$selectedItem.Description
            }
            $panelLabel = 'Focus'
        }
    }

    $tone = if ($panelLabel -eq 'Focus') {
        'warn'
    }
    elseif ($panelText -match 'failed|error') {
        'error'
    }
    else {
        'ok'
    }

    return [ordered]@{
        Label = $panelLabel
        Text = $panelText
        Tone = $tone
    }
}

function Get-TuiStatusBarMaxLineCountCore {
    param([Parameter(Mandatory)] $State)

    switch ([string]$State.CurrentScreen) {
        'InstallScreen' { return 1 }
        'UninstallScreen' { return 1 }
        default { return 2 }
    }
}

function Build-TuiStatusBarLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $status = Get-TuiStatusBarStateCore -State $State
    if ([string]::IsNullOrWhiteSpace([string]$status.Text)) {
        return @()
    }

    $badge = Format-TuiBadgeCore -Text ([string]$status.Label) -Tone ([string]$status.Tone)
    $availableWidth = [Math]::Max(8, [int]$Viewport.Width - 1)
    $maxLineCount = Get-TuiStatusBarMaxLineCountCore -State $State
    if ($maxLineCount -le 1) {
        $badgePlainText = "[{0}]" -f ([string]$status.Label)
        $textWidth = [Math]::Max(0, $availableWidth - (Get-TuiDisplayWidthCore -Text $badgePlainText) - 1)
        $fittedText = Fit-TuiTextCore -Text ([string]$status.Text) -Width $textWidth
        return @(
            Pad-TuiLineCore -Text ("$badge $fittedText") -Width ([int]$Viewport.Width)
        )
    }

    $wrapped = @(ConvertTo-TuiWrappedLinesCore -Text ("$badge $([string]$status.Text)") -Width $availableWidth)
    if ($wrapped.Count -gt $maxLineCount) {
        $wrapped = @($wrapped | Select-Object -First $maxLineCount)
    }

    return @($wrapped | ForEach-Object {
            Pad-TuiLineCore -Text ([string]$_) -Width ([int]$Viewport.Width)
        })
}

function Build-TuiFooterLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $glyphs = Get-TuiBorderGlyphsCore
    $helpText = switch ([string]$State.CurrentScreen) {
        'HomeScreen' { 'Enter select  Up/Down move  Left/Right architecture  Escape close' }
        'ConfirmScreen' { 'Enter confirm  Escape cancel' }
        'PathEditorScreen' { 'Enter browse  Tab name folder  Escape cancel' }
        'DirectoryPickerScreen' { 'Up/Down move  Enter open  Tab name folder  Left/Backspace up  Escape cancel' }
        'FolderNamePromptScreen' { 'Type name  Backspace delete  Enter save  Escape back' }
        default { 'Enter select  Up/Down move  Left/Right architecture  Escape back' }
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add((Pad-TuiLineCore -Text "$($Accent.Border)$(Get-TuiRuleLineCore -Width ([int]$Viewport.Width) -Glyphs $glyphs)$($Accent.Reset)" -Width ([int]$Viewport.Width)))
    foreach ($statusLine in @(Build-TuiStatusBarLinesCore -State $State -Viewport $Viewport -Accent $Accent)) {
        $lines.Add([string]$statusLine)
    }
    $lines.Add((Pad-TuiLineCore -Text "$($Accent.Dim)$helpText$($Accent.Reset)" -Width ([int]$Viewport.Width)))
    return @($lines)
}
