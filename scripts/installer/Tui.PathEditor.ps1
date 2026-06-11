function Get-TuiPathEditorPlaceholderCore {
    return 'Browse folders to choose the install parent directory.'
}

function Get-TuiDefaultInstallFolderNameCore {
    return 'wpf-devtools-mcp'
}

function Get-TuiPathEditorBufferCore {
    param([Parameter(Mandatory)] $State)

    if ($null -eq $State.PathEditor) {
        return ''
    }

    return [string]$State.PathEditor.Buffer
}

function Get-TuiNormalizedInstallRootCore {
    param([string]$Buffer)

    if ([string]::IsNullOrWhiteSpace($Buffer)) {
        return ''
    }

    $trimmed = [string]$Buffer.Trim().Trim('"').Replace('/', '\')
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return ''
    }

    try {
        $normalized = [System.IO.Path]::GetFullPath($trimmed)
    }
    catch {
        $normalized = $trimmed
    }

    if ($normalized.Length -gt 3) {
        $normalized = $normalized.TrimEnd('\')
    }

    return $normalized
}

function Resolve-TuiExistingDirectoryCore {
    param(
        [string]$Candidate,
        [string]$FallbackCandidate
    )

    foreach ($value in @($Candidate, $FallbackCandidate, $env:APPDATA, $env:USERPROFILE, (Get-Location).Path)) {
        $current = Get-TuiNormalizedInstallRootCore -Buffer ([string]$value)
        while (-not [string]::IsNullOrWhiteSpace($current)) {
            if (Test-Path -LiteralPath $current -PathType Container) {
                return $current
            }

            $parent = Split-Path -Path $current -Parent
            if ([string]::IsNullOrWhiteSpace($parent) -or ($parent -eq $current)) {
                break
            }

            $current = $parent
        }
    }

    return (Get-Location).Path
}

function Get-TuiPathEditorViewportCore {
    $fallbackViewport = [ordered]@{
        Width = 92
        Height = 18
        UseAnsi = $false
    }

    $viewportCommand = Get-Command Get-TuiViewportCore -ErrorAction SilentlyContinue
    if ($null -eq $viewportCommand) {
        return $fallbackViewport
    }

    $viewport = Get-TuiViewportCore
    $windowViewportCommand = Get-Command Get-TuiWindowViewportCore -ErrorAction SilentlyContinue
    if ($null -eq $windowViewportCommand) {
        return $viewport
    }

    return (Get-TuiWindowViewportCore -Viewport $viewport)
}

function Get-TuiDirectoryPickerVisibleWindowSizeCore {
    param(
        $State,
        [Parameter(Mandatory)] $Viewport
    )

    $panelWidth = if ($null -ne (Get-Command Get-TuiContentColumnWidthCore -ErrorAction SilentlyContinue)) {
        $contentWidth = Get-TuiContentColumnWidthCore -Viewport $Viewport
        [Math]::Min($contentWidth, [Math]::Max(46, [int]$Viewport.Width - 8))
    }
    else {
        [Math]::Max(46, [int]$Viewport.Width - 8)
    }

    $currentDirectory = if ($null -ne $State -and $null -ne $State.PathEditor) { [string]$State.PathEditor.BrowserCurrentDirectory } else { '' }
    $pathLineCount = [Math]::Max(1, @(ConvertTo-TuiWrappedPathLinesCore -Text $currentDirectory -Width ([Math]::Max(10, $panelWidth - 4))).Count)
    $entryCount = if ($null -ne $State -and $null -ne $State.PathEditor) { @($State.PathEditor.BrowserEntries).Count } else { 0 }

    $titleBarLineCount = 3
    $bannerLineCount = if ($null -ne $State -and -not [string]::IsNullOrWhiteSpace([string]$State.UpdateBannerText)) { 2 } else { 0 }
    $pageHeaderLineCount = 5
    $footerLineCount = 3
    $availableBodyLines = [Math]::Max(1, [int]$Viewport.Height - $titleBarLineCount - $bannerLineCount - $pageHeaderLineCount - $footerLineCount)

    $pickerChromeLines = 5 + $pathLineCount
    $availableEntryLines = [Math]::Max(0, $availableBodyLines - $pickerChromeLines)
    $windowSize = $availableEntryLines
    if ($entryCount -gt $windowSize -and $availableEntryLines -ge 2) {
        $windowSize = $availableEntryLines - 1
    }

    if ($entryCount -gt 0) {
        $windowSize = [Math]::Min($entryCount, $windowSize)
    }

    $windowSize = [Math]::Min(10, $windowSize)

    return $windowSize
}

function Get-TuiDirectoryPickerEntriesCore {
    param([Parameter(Mandatory)] [string]$CurrentDirectory)

    $entries = @()
    $parent = Split-Path -Path $CurrentDirectory -Parent
    if (-not [string]::IsNullOrWhiteSpace($parent) -and ($parent -ne $CurrentDirectory)) {
        $entries += [ordered]@{
            Label = '..'
            FullPath = $parent
            IsParent = $true
        }
    }

    foreach ($directory in @(Get-ChildItem -LiteralPath $CurrentDirectory -Directory -ErrorAction SilentlyContinue | Sort-Object Name)) {
        $entries += [ordered]@{
            Label = [string]$directory.Name
            FullPath = (Get-TuiNormalizedInstallRootCore -Buffer $directory.FullName)
            IsParent = $false
        }
    }

    return @($entries)
}

function Update-TuiDirectoryPickerScrollCore {
    param(
        [Parameter(Mandatory)] $State,
        $Viewport
    )

    if ($null -eq $State.PathEditor) {
        return $State
    }

    $entries = @($State.PathEditor.BrowserEntries)
    $viewport = if ($null -ne $Viewport) { $Viewport } else { Get-TuiPathEditorViewportCore }
    $windowSize = Get-TuiDirectoryPickerVisibleWindowSizeCore -State $State -Viewport $viewport
    $State.PathEditor.BrowserVisibleWindowSize = $windowSize
    $maxIndex = [Math]::Max(0, $entries.Count - 1)
    $State.PathEditor.BrowserSelectionIndex = [Math]::Min($maxIndex, [Math]::Max(0, [int]$State.PathEditor.BrowserSelectionIndex))

    if ($windowSize -le 0) {
        $State.PathEditor.BrowserScrollOffset = [Math]::Min($maxIndex, [Math]::Max(0, [int]$State.PathEditor.BrowserSelectionIndex))
        return $State
    }

    if ([int]$State.PathEditor.BrowserSelectionIndex -lt [int]$State.PathEditor.BrowserScrollOffset) {
        $State.PathEditor.BrowserScrollOffset = [int]$State.PathEditor.BrowserSelectionIndex
    }
    elseif ([int]$State.PathEditor.BrowserSelectionIndex -ge ([int]$State.PathEditor.BrowserScrollOffset + $windowSize)) {
        $State.PathEditor.BrowserScrollOffset = [int]$State.PathEditor.BrowserSelectionIndex - $windowSize + 1
    }

    $maxOffset = [Math]::Max(0, $entries.Count - $windowSize)
    $State.PathEditor.BrowserScrollOffset = [Math]::Min($maxOffset, [Math]::Max(0, [int]$State.PathEditor.BrowserScrollOffset))
    return $State
}

function Set-TuiDirectoryPickerDirectoryCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$CurrentDirectory
    )

    $resolvedDirectory = Resolve-TuiExistingDirectoryCore -Candidate $CurrentDirectory
    $State.CurrentScreen = 'DirectoryPickerScreen'
    $State.PathEditor.BrowserCurrentDirectory = $resolvedDirectory
    $State.PathEditor.BrowserEntries = @(Get-TuiDirectoryPickerEntriesCore -CurrentDirectory $resolvedDirectory)
    $State.PathEditor.BrowserSelectionIndex = 0
    $State.PathEditor.BrowserScrollOffset = 0
    $State.PathEditor.StatusMessage = "Browsing $resolvedDirectory"
    return (Update-TuiDirectoryPickerScrollCore -State $State -Viewport (Get-TuiPathEditorViewportCore))
}

function Reset-TuiToHomeFromPathEditorCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$StatusMessage
    )

    $homeItems = @($State.HomeItems)
    $State.PathEditor = $null
    $State.CurrentScreen = 'HomeScreen'
    $State.SelectionIndex = [Math]::Min(3, [Math]::Max(0, $homeItems.Count - 1))
    $State.ScrollOffset = 0
    $State.StatusMessage = $StatusMessage
    if ($StatusMessage -eq 'Install location unchanged.') {
        $State.HomeItems = @(Set-TuiHomeItemStatusBadgeCore -HomeItems $State.HomeItems -ItemId 'edit-root' -StatusMessage $StatusMessage)
    }
    return $State
}

function Set-TuiHomeItemStatusBadgeCore {
    param(
        [Parameter(Mandatory)] [object[]]$HomeItems,
        [Parameter(Mandatory)] [string]$ItemId,
        [Parameter(Mandatory)] [string]$StatusMessage
    )

    $updatedItems = @()
    foreach ($item in @($HomeItems)) {
        if ([string]$item.Id -ne $ItemId) {
            $updatedItems += $item
            continue
        }

        $updatedItem = [ordered]@{}
        if ($item -is [System.Collections.IDictionary]) {
            foreach ($key in $item.Keys) {
                $updatedItem[$key] = $item[$key]
            }
        }
        else {
            foreach ($property in $item.PSObject.Properties) {
                $updatedItem[$property.Name] = $property.Value
            }
        }

        $updatedItem.StatusBadge = $StatusMessage
        $updatedItems += $updatedItem
    }

    return @($updatedItems)
}

function Sync-TuiHomeItemsAfterInstallRootChangeCore {
    param([Parameter(Mandatory)] $State)

    if ($null -eq (Get-Command Get-TuiHomeItemsCore -ErrorAction SilentlyContinue)) {
        return $State
    }

    $installerState = if ($null -ne $State.InstallerState) { $State.InstallerState } else { [ordered]@{} }
    $registrationMap = if ($null -ne $State.DetectedRegistrationMap) { $State.DetectedRegistrationMap } else { [ordered]@{} }
    $latestVersion = if ($null -ne $State.LatestVersion) { [string]$State.LatestVersion } else { '' }
    $refreshPending = if ($null -ne $State.LatestVersionRefreshPending) { [bool]$State.LatestVersionRefreshPending } else { $false }
    $State.HomeItems = @(Get-TuiHomeItemsCore -InstallRoot ([string]$State.InstallRoot) -InstallerState $installerState -LatestVersion $latestVersion -RegistrationMap $registrationMap -LatestVersionRefreshPending $refreshPending)
    return $State
}

function Save-TuiResolvedInstallRootCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$ResolvedRoot
    )

    if ([string]::IsNullOrWhiteSpace($ResolvedRoot)) {
        $State.PathEditor.StatusMessage = 'Install location cannot be empty.'
        return $State
    }

    $State.InstallRoot = $ResolvedRoot
    $State = Sync-TuiHomeItemsAfterInstallRootChangeCore -State $State
    return (Reset-TuiToHomeFromPathEditorCore -State $State -StatusMessage "Install location updated to $($State.InstallRoot).")
}

function Enter-TuiPathEditorCore {
    param([Parameter(Mandatory)] $State)

    $State.PathEditor = [ordered]@{
        Prompt = 'Install location'
        Buffer = [string]$State.InstallRoot
        OriginalValue = [string]$State.InstallRoot
        StatusMessage = 'Browse folders to choose the install parent directory.'
        BrowserCurrentDirectory = (Resolve-TuiExistingDirectoryCore -Candidate ([string]$State.InstallRoot))
        BrowserEntries = @()
        BrowserSelectionIndex = 0
        BrowserScrollOffset = 0
        BrowserVisibleWindowSize = 0
        SelectedParentDirectory = ''
        FolderNameBuffer = (Get-TuiDefaultInstallFolderNameCore)
    }
    $State.PendingAction = $null
    return (Open-TuiDirectoryPickerCore -State $State)
}

function Open-TuiDirectoryPickerCore {
    param([Parameter(Mandatory)] $State)

    if ($null -eq $State.PathEditor) {
        return $State
    }

    $directory = Resolve-TuiExistingDirectoryCore -Candidate ([string]$State.PathEditor.Buffer) -FallbackCandidate ([string]$State.PathEditor.BrowserCurrentDirectory)
    return (Set-TuiDirectoryPickerDirectoryCore -State $State -CurrentDirectory $directory)
}

function Move-TuiDirectoryPickerSelectionCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [int]$Delta
    )

    $State.PathEditor.BrowserSelectionIndex = [int]$State.PathEditor.BrowserSelectionIndex + $Delta
    return (Update-TuiDirectoryPickerScrollCore -State $State -Viewport (Get-TuiPathEditorViewportCore))
}

function Enter-TuiSelectedDirectoryCore {
    param([Parameter(Mandatory)] $State)

    $entries = @($State.PathEditor.BrowserEntries)
    if ($entries.Count -eq 0) {
        return $State
    }

    $selected = $entries[[int]$State.PathEditor.BrowserSelectionIndex]
    return (Set-TuiDirectoryPickerDirectoryCore -State $State -CurrentDirectory ([string]$selected.FullPath))
}

function Move-TuiDirectoryPickerToParentCore {
    param([Parameter(Mandatory)] $State)

    $parent = Split-Path -Path ([string]$State.PathEditor.BrowserCurrentDirectory) -Parent
    if ([string]::IsNullOrWhiteSpace($parent) -or ($parent -eq [string]$State.PathEditor.BrowserCurrentDirectory)) {
        return $State
    }

    return (Set-TuiDirectoryPickerDirectoryCore -State $State -CurrentDirectory $parent)
}

function Open-TuiFolderNamePromptCore {
    param([Parameter(Mandatory)] $State)

    $State.CurrentScreen = 'FolderNamePromptScreen'
    $State.PathEditor.SelectedParentDirectory = [string]$State.PathEditor.BrowserCurrentDirectory
    if ([string]::IsNullOrWhiteSpace([string]$State.PathEditor.FolderNameBuffer)) {
        $State.PathEditor.FolderNameBuffer = Get-TuiDefaultInstallFolderNameCore
    }
    $State.PathEditor.StatusMessage = "Selected parent: $([string]$State.PathEditor.SelectedParentDirectory)"
    return $State
}

function Save-TuiPathEditorCore {
    param([Parameter(Mandatory)] $State)

    $resolvedRoot = Get-TuiNormalizedInstallRootCore -Buffer ([string]$State.PathEditor.Buffer)
    return (Save-TuiResolvedInstallRootCore -State $State -ResolvedRoot $resolvedRoot)
}

function Save-TuiFolderNamePromptCore {
    param([Parameter(Mandatory)] $State)

    $folderName = [string]$State.PathEditor.FolderNameBuffer
    if ([string]::IsNullOrWhiteSpace($folderName)) {
        $State.PathEditor.StatusMessage = 'Install folder name cannot be empty.'
        return $State
    }

    $trimmedName = $folderName.Trim().Trim('\', '/')
    if ([string]::IsNullOrWhiteSpace($trimmedName)) {
        $State.PathEditor.StatusMessage = 'Install folder name cannot be empty.'
        return $State
    }

    $selectedParent = Resolve-TuiExistingDirectoryCore -Candidate ([string]$State.PathEditor.SelectedParentDirectory) -FallbackCandidate ([string]$State.PathEditor.BrowserCurrentDirectory)
    $resolvedRoot = Get-TuiNormalizedInstallRootCore -Buffer (Join-Path $selectedParent $trimmedName)
    return (Save-TuiResolvedInstallRootCore -State $State -ResolvedRoot $resolvedRoot)
}

function Cancel-TuiPathEditorCore {
    param([Parameter(Mandatory)] $State)

    return (Reset-TuiToHomeFromPathEditorCore -State $State -StatusMessage 'Install location unchanged.')
}

function Return-TuiToPathEditorCore {
    param([Parameter(Mandatory)] $State)

    return (Cancel-TuiPathEditorCore -State $State)
}

function Return-TuiToDirectoryPickerCore {
    param([Parameter(Mandatory)] $State)

    $State.CurrentScreen = 'DirectoryPickerScreen'
    $State.PathEditor.StatusMessage = "Selected parent: $([string]$State.PathEditor.BrowserCurrentDirectory)"
    return $State
}

function Handle-TuiPathEditorCharacterInputCore {
    param(
        [Parameter(Mandatory)] $State,
        [AllowEmptyString()] [string]$Character
    )

    if ([string]::IsNullOrEmpty($Character)) {
        return $State
    }

    $charCode = [int][char]$Character
    if ([char]::IsControl([char]$charCode)) {
        return $State
    }

    if ([string]$State.CurrentScreen -eq 'FolderNamePromptScreen') {
        $State.PathEditor.FolderNameBuffer = ([string]$State.PathEditor.FolderNameBuffer) + $Character
        $State.PathEditor.StatusMessage = 'Editing install folder name...'
        return $State
    }

    return $State
}

function Handle-TuiPathEditorKeyCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $KeyInfo
    )

    if ($null -eq $State.PathEditor) {
        return $State
    }

    switch ([string]$State.CurrentScreen) {
        'PathEditorScreen' {
            switch ($KeyInfo.Key) {
                ([ConsoleKey]::Escape) { return (Cancel-TuiPathEditorCore -State $State) }
                ([ConsoleKey]::Tab) { return (Open-TuiDirectoryPickerCore -State $State) }
            }
        }
        'DirectoryPickerScreen' {
            switch ($KeyInfo.Key) {
                ([ConsoleKey]::Escape) { return (Cancel-TuiPathEditorCore -State $State) }
                ([ConsoleKey]::Tab) { return (Open-TuiFolderNamePromptCore -State $State) }
                ([ConsoleKey]::UpArrow) { return (Move-TuiDirectoryPickerSelectionCore -State $State -Delta -1) }
                ([ConsoleKey]::DownArrow) { return (Move-TuiDirectoryPickerSelectionCore -State $State -Delta 1) }
                ([ConsoleKey]::Enter) { return (Enter-TuiSelectedDirectoryCore -State $State) }
                ([ConsoleKey]::RightArrow) { return (Enter-TuiSelectedDirectoryCore -State $State) }
                ([ConsoleKey]::LeftArrow) { return (Move-TuiDirectoryPickerToParentCore -State $State) }
                ([ConsoleKey]::Backspace) { return (Move-TuiDirectoryPickerToParentCore -State $State) }
            }
        }
        'FolderNamePromptScreen' {
            switch ($KeyInfo.Key) {
                ([ConsoleKey]::Escape) { return (Return-TuiToDirectoryPickerCore -State $State) }
                ([ConsoleKey]::Enter) { return (Save-TuiFolderNamePromptCore -State $State) }
                ([ConsoleKey]::Backspace) {
                    $buffer = [string]$State.PathEditor.FolderNameBuffer
                    if ($buffer.Length -gt 0) {
                        $State.PathEditor.FolderNameBuffer = $buffer.Substring(0, $buffer.Length - 1)
                    }
                    $State.PathEditor.StatusMessage = 'Editing install folder name...'
                    return $State
                }
            }
        }
    }

    if ([string]::IsNullOrEmpty([string]$KeyInfo.Character)) {
        return $State
    }

    return (Handle-TuiPathEditorCharacterInputCore -State $State -Character ([string]$KeyInfo.Character))
}

function Invoke-TuiInstallRootPromptCore {
    param([Parameter(Mandatory)] $State)

    return (Enter-TuiPathEditorCore -State $State)
}

$pathEditorViewsPath = Join-Path -Path $PSScriptRoot -ChildPath 'Tui.PathEditor.Views.ps1'
if (Test-Path -LiteralPath $pathEditorViewsPath) {
    . $pathEditorViewsPath
}
