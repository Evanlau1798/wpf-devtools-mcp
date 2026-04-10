function Get-TuiDirectoryPickerVisibleEntriesCore {
    param([Parameter(Mandatory)] $State)

    $entries = @($State.PathEditor.BrowserEntries)
    $offset = [int]$State.PathEditor.BrowserScrollOffset
    $windowSize = [Math]::Max(0, [int]$State.PathEditor.BrowserVisibleWindowSize)
    $visibleEntries = New-Object System.Collections.ArrayList
    $lastIndex = [Math]::Min($entries.Count, $offset + $windowSize)
    for ($index = $offset; $index -lt $lastIndex; $index++) {
        [void]$visibleEntries.Add([pscustomobject]@{
                AbsoluteIndex = $index
                Entry = $entries[$index]
            })
    }

    return @($visibleEntries)
}

function Test-TuiDirectoryPickerSummaryFitsCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport
    )

    if ($null -eq $State.PathEditor) {
        return $false
    }

    $entryCount = @($State.PathEditor.BrowserEntries).Count
    $visibleEntryCount = [Math]::Max(0, [int]$State.PathEditor.BrowserVisibleWindowSize)
    if ($entryCount -le $visibleEntryCount) {
        return $false
    }

    $contentWidth = Get-TuiContentColumnWidthCore -Viewport $Viewport
    $panelWidth = [Math]::Min($contentWidth, [Math]::Max(46, [int]$Viewport.Width - 8))
    $pathLineCount = [Math]::Max(1, @(ConvertTo-TuiWrappedPathLinesCore -Text ([string]$State.PathEditor.BrowserCurrentDirectory) -Width ($panelWidth - 4)).Count)
    $titleBarLineCount = @(Build-TuiTitleBarLinesCore -State $State -Viewport $Viewport -Accent (Get-TuiAccent)).Count + 1
    $bannerLineCount = if (-not [string]::IsNullOrWhiteSpace([string]$State.UpdateBannerText)) { 2 } else { 0 }
    $headerLineCount = @(Build-TuiPageHeaderLinesCore -State $State -Viewport $Viewport -Accent (Get-TuiAccent)).Count
    $footerLineCount = @(Build-TuiFooterLinesCore -State $State -Viewport $Viewport -Accent (Get-TuiAccent)).Count
    $availableBodyLines = [Math]::Max(1, [int]$Viewport.Height - $titleBarLineCount - $bannerLineCount - $headerLineCount - $footerLineCount)
    $bodyLineCountWithoutSummary = 5 + $pathLineCount + $visibleEntryCount
    return (($bodyLineCountWithoutSummary + 1) -le $availableBodyLines)
}

function Build-TuiPathEditorLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $contentWidth = Get-TuiContentColumnWidthCore -Viewport $Viewport
    $editorWidth = [Math]::Min($contentWidth, [Math]::Max(42, [int]$Viewport.Width - 8))
    $bufferText = Get-TuiPathEditorBufferCore -State $State
    if ([string]::IsNullOrWhiteSpace($bufferText)) {
        $bufferText = Get-TuiPathEditorPlaceholderCore
    }

    $statusText = if ($null -ne $State.PathEditor) { [string]$State.PathEditor.StatusMessage } else { '' }
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($cardLine in @(New-TuiCardBlockCore -Width $editorWidth -PrimaryText 'Install location' -SecondaryText $bufferText -StatusBadge 'Browse' -Selected $true -Accent $Accent -Glyphs (Get-TuiBorderGlyphsCore) -PathDetail)) {
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text $cardLine -ContentWidth $editorWidth))
    }

    $lines.Add('')
    foreach ($hintLine in @(
            'Browse folders with Enter or the arrow keys to pick the install parent directory.'
            'Press Tab to confirm the current parent and choose the final install folder name.'
            'Escape cancels and returns to the home screen.'
        )) {
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$hintLine$($Accent.Reset)" -ContentWidth $editorWidth))
    }

    if (-not [string]::IsNullOrWhiteSpace($statusText)) {
        $lines.Add('')
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Text)$statusText$($Accent.Reset)" -ContentWidth $editorWidth))
    }

    return @($lines)
}

function Build-TuiDirectoryPickerLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $State = Update-TuiDirectoryPickerScrollCore -State $State -Viewport $Viewport
    $contentWidth = Get-TuiContentColumnWidthCore -Viewport $Viewport
    $panelWidth = [Math]::Min($contentWidth, [Math]::Max(46, [int]$Viewport.Width - 8))
    $glyphs = Get-TuiBorderGlyphsCore
    $heading = Join-TuiColumnsCore -LeftText "$($Accent.Dim)Current directory$($Accent.Reset)" -RightText (Format-TuiBadgeCore -Text 'Browse' -Tone 'warn') -Width ($panelWidth - 4)
    $lines = New-Object System.Collections.Generic.List[string]

    $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Border)$(New-TuiBoxBorderLineCore -LeftGlyph $glyphs.TopLeft -RightGlyph $glyphs.TopRight -Width $panelWidth -Glyphs $glyphs)$($Accent.Reset)" -ContentWidth $panelWidth))
    $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Border)$($glyphs.Vertical)$($Accent.Reset) $(Pad-TuiLineCore -Text $heading -Width ($panelWidth - 4)) $($Accent.Border)$($glyphs.Vertical)$($Accent.Reset)" -ContentWidth $panelWidth))
    $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Border)$(New-TuiBoxBorderLineCore -LeftGlyph $glyphs.TeeLeft -RightGlyph $glyphs.TeeRight -Width $panelWidth -Glyphs $glyphs)$($Accent.Reset)" -ContentWidth $panelWidth))

    foreach ($pathLine in @(ConvertTo-TuiWrappedPathLinesCore -Text ([string]$State.PathEditor.BrowserCurrentDirectory) -Width ($panelWidth - 4))) {
        $pathText = "$($Accent.Text)$pathLine$($Accent.Reset)"
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Border)$($glyphs.Vertical)$($Accent.Reset) $(Pad-TuiLineCore -Text $pathText -Width ($panelWidth - 4)) $($Accent.Border)$($glyphs.Vertical)$($Accent.Reset)" -ContentWidth $panelWidth))
    }

    $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Border)$(New-TuiBoxBorderLineCore -LeftGlyph $glyphs.TeeLeft -RightGlyph $glyphs.TeeRight -Width $panelWidth -Glyphs $glyphs)$($Accent.Reset)" -ContentWidth $panelWidth))
    foreach ($visibleEntry in @(Get-TuiDirectoryPickerVisibleEntriesCore -State $State)) {
        $entry = $visibleEntry.Entry
        $selected = ([int]$visibleEntry.AbsoluteIndex -eq [int]$State.PathEditor.BrowserSelectionIndex)
        $prefix = if ($selected) { "$($Accent.Warn)>$($Accent.Reset) " } else { '  ' }
        $label = if ([bool]$entry.IsParent) { '..' } else { [string]$entry.Label }
        $entryText = "$prefix$($Accent.Text)$label$($Accent.Reset)"
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Border)$($glyphs.Vertical)$($Accent.Reset) $(Pad-TuiLineCore -Text $entryText -Width ($panelWidth - 4)) $($Accent.Border)$($glyphs.Vertical)$($Accent.Reset)" -ContentWidth $panelWidth))
    }

    if (Test-TuiDirectoryPickerSummaryFitsCore -State $State -Viewport $Viewport) {
        $summary = "$($Accent.Dim)Showing $([int]$State.PathEditor.BrowserScrollOffset + 1)-$([Math]::Min(@($State.PathEditor.BrowserEntries).Count, [int]$State.PathEditor.BrowserScrollOffset + [int]$State.PathEditor.BrowserVisibleWindowSize)) of $(@($State.PathEditor.BrowserEntries).Count)$($Accent.Reset)"
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Border)$($glyphs.Vertical)$($Accent.Reset) $(Pad-TuiLineCore -Text $summary -Width ($panelWidth - 4)) $($Accent.Border)$($glyphs.Vertical)$($Accent.Reset)" -ContentWidth $panelWidth))
    }

    $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Border)$(New-TuiBoxBorderLineCore -LeftGlyph $glyphs.BottomLeft -RightGlyph $glyphs.BottomRight -Width $panelWidth -Glyphs $glyphs)$($Accent.Reset)" -ContentWidth $panelWidth))
    return @($lines)
}

function Build-TuiFolderNamePromptLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $contentWidth = Get-TuiContentColumnWidthCore -Viewport $Viewport
    $panelWidth = [Math]::Min($contentWidth, [Math]::Max(42, [int]$Viewport.Width - 8))
    $parentDirectory = [string]$State.PathEditor.SelectedParentDirectory
    $statusText = [string]$State.PathEditor.StatusMessage
    $folderName = [string]$State.PathEditor.FolderNameBuffer
    $lines = New-Object System.Collections.Generic.List[string]

    $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)Parent directory:$($Accent.Reset)" -ContentWidth $panelWidth))
    foreach ($pathLine in @(ConvertTo-TuiWrappedPathLinesCore -Text $parentDirectory -Width $panelWidth)) {
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Text)$pathLine$($Accent.Reset)" -ContentWidth $panelWidth))
    }

    $lines.Add('')
    foreach ($cardLine in @(New-TuiCardBlockCore -Width $panelWidth -PrimaryText 'Install folder name' -SecondaryText $folderName -StatusBadge 'Naming' -Selected $true -Accent $Accent -Glyphs (Get-TuiBorderGlyphsCore))) {
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text $cardLine -ContentWidth $panelWidth))
    }

    $lines.Add('')
    foreach ($hintLine in @(
            'Type the final install folder name under the selected parent directory.'
            'Enter saves the combined path. Escape returns to directory browsing.'
        )) {
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$hintLine$($Accent.Reset)" -ContentWidth $panelWidth))
    }

    if (-not [string]::IsNullOrWhiteSpace($statusText)) {
        $lines.Add('')
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Text)$statusText$($Accent.Reset)" -ContentWidth $panelWidth))
    }

    return @($lines)
}
