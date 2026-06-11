function Get-TuiPageMetadataCore {
    param([Parameter(Mandatory)] $State)

    switch ([string]$State.CurrentScreen) {
        'HomeScreen' {
            return [ordered]@{
                Title = 'WPF DevTools MCP'
                Subtitle = 'Model Context Protocol Server'
                Eyebrow = 'Installation Manager'
                ContextLines = @()
            }
        }
        'InstallScreen' {
            return [ordered]@{
                Title = 'Where would you like to install?'
                Subtitle = 'Choose the target AI tool.'
                Eyebrow = '<- Back'
                ContextLines = @(
                    "Architecture: $([string]$State.SelectedArchitecture)"
                    "Install location: $([string]$State.InstallRoot)"
                )
            }
        }
        'UninstallScreen' {
            return [ordered]@{
                Title = 'Select what to uninstall'
                Subtitle = 'Only detected installed targets can be removed.'
                Eyebrow = '<- Back'
                ContextLines = @(
                    "Architecture: $([string]$State.SelectedArchitecture)"
                    "Install location: $([string]$State.InstallRoot)"
                )
            }
        }
        'ConfirmScreen' {
            $title = if ([string]$State.ConfirmationMode -eq 'close-app') { 'Confirm close' } else { 'Confirm action' }
            $subtitle = if ([string]$State.ConfirmationMode -eq 'close-app') { 'Leave the installer?' } else { 'Two-step confirmation is required.' }
            return [ordered]@{
                Title = $title
                Subtitle = $subtitle
                Eyebrow = '<- Back'
                ContextLines = @()
            }
        }
        'PathEditorScreen' {
            return [ordered]@{
                Title = 'Select install parent directory'
                Subtitle = 'Browse folders first, then name the final install folder.'
                Eyebrow = '<- Back'
                ContextLines = @()
            }
        }
        'DirectoryPickerScreen' {
            return [ordered]@{
                Title = 'Select install parent directory'
                Subtitle = 'Browse folders, then press Tab to name the final install folder.'
                Eyebrow = '<- Back'
                ContextLines = @()
            }
        }
        'FolderNamePromptScreen' {
            return [ordered]@{
                Title = 'Name install folder'
                Subtitle = 'Choose the final folder name inside the selected parent directory.'
                Eyebrow = '<- Back'
                ContextLines = @()
            }
        }
        default {
            return [ordered]@{
                Title = 'Working...'
                Subtitle = 'Please wait while the installer completes the current operation.'
                Eyebrow = ''
                ContextLines = @()
            }
        }
    }
}

function Get-TuiVisibleWindowSizeCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport
    )

    if ([string]$State.CurrentScreen -notin @('InstallScreen', 'UninstallScreen')) {
        return 4
    }

    $items = @(Get-TuiCurrentItems -State $State)
    if ($items.Count -eq 0) {
        return 1
    }

    $accent = Get-TuiAccent
    $availableLines = Get-TuiSelectionListAvailableLineCountCore -State $State -Viewport $Viewport -Accent $accent
    if ($availableLines -le 0) {
        return 1
    }

    $listWidth = Get-TuiSelectionListContentWidthCore -State $State -Viewport $Viewport
    $glyphs = Get-TuiBorderGlyphsCore
    $offset = [Math]::Min([Math]::Max(0, [int]$State.ScrollOffset), [Math]::Max(0, $items.Count - 1))
    $visibleCount = 0
    $usedLines = 0
    $remainingItems = @($items | Select-Object -Skip $offset)

    foreach ($item in $remainingItems) {
        $itemLineCount = Get-TuiSelectionListItemLineCountCore -Item $item -Width $listWidth -Accent $accent -Glyphs $glyphs
        $remainingAfterThis = $remainingItems.Count - ($visibleCount + 1)
        $summaryLines = if ($remainingAfterThis -gt 0) { 1 } else { 0 }

        if (($visibleCount -gt 0) -and (($usedLines + $itemLineCount + $summaryLines) -gt $availableLines)) {
            break
        }

        if ($visibleCount -eq 0 -and $itemLineCount -gt $availableLines) {
            return 1
        }

        if ((($usedLines + $itemLineCount + $summaryLines) -le $availableLines) -or ($visibleCount -eq 0)) {
            $usedLines += $itemLineCount
            $visibleCount++
            continue
        }

        break
    }

    return [Math]::Max(1, $visibleCount)
}

function Get-TuiHeaderOverflowLineCountCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport
    )

    if ([string]$State.CurrentScreen -notin @('InstallScreen', 'UninstallScreen') -or
        [string]::IsNullOrWhiteSpace([string]$State.InstallRoot)) {
        return 0
    }

    $label = 'Install location: '
    $contentWidth = Get-TuiContentColumnWidthCore -Viewport $Viewport
    $pathWidth = [Math]::Max(1, $contentWidth - $label.Length)
    $wrappedLines = @(ConvertTo-TuiWrappedPathLinesCore -Text ([string]$State.InstallRoot) -Width $pathWidth)
    return [Math]::Max(0, $wrappedLines.Count - 1)
}

function Format-TuiCompactPathTextCore {
    param(
        [string]$Text,
        [Parameter(Mandatory)] [int]$Width
    )

    $pathText = [string]$Text
    if ([string]::IsNullOrWhiteSpace($pathText) -or $pathText.Length -le $Width) {
        return $pathText
    }

    if ($Width -le 3) {
        return $pathText.Substring([Math]::Max(0, $pathText.Length - $Width))
    }

    return '...' + $pathText.Substring([Math]::Max(0, $pathText.Length - ($Width - 3)))
}

function Get-TuiSelectionListContentWidthCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport
    )

    return (Get-TuiContentColumnWidthCore -Viewport $Viewport)
}

function Get-TuiSelectionListAvailableLineCountCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $titleBarLineCount = @(Build-TuiTitleBarLinesCore -State $State -Viewport $Viewport -Accent $Accent).Count + 1
    $bannerLineCount = if (-not [string]::IsNullOrWhiteSpace([string]$State.UpdateBannerText)) { 2 } else { 0 }
    $headerLineCount = @(Build-TuiPageHeaderLinesCore -State $State -Viewport $Viewport -Accent $Accent).Count
    $footerLineCount = @(Build-TuiFooterLinesCore -State $State -Viewport $Viewport -Accent $Accent).Count

    return [Math]::Max(0, [int]$Viewport.Height - $titleBarLineCount - $bannerLineCount - $headerLineCount - $footerLineCount)
}

function Get-TuiSelectionListItemLineCountCore {
    param(
        [Parameter(Mandatory)] $Item,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] $Accent,
        [Parameter(Mandatory)] $Glyphs
    )

    $lineCount = @(New-TuiCardBlockCore -Width $Width -PrimaryText ([string]$Item.PrimaryText) -SecondaryText ([string]$Item.SecondaryText) -StatusBadge ([string]$Item.StatusBadge) -Selected $false -Accent $Accent -Glyphs $Glyphs).Count
    if ([bool]$Item.ShowDividerBefore) {
        $lineCount++
    }

    return $lineCount
}

function Update-TuiVisibleWindowSizeCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport
    )

    $State.VisibleWindowSize = Get-TuiVisibleWindowSizeCore -State $State -Viewport $Viewport
    return (Update-TuiScrollCore -State $State)
}

function Test-TuiUseSplitStatusLayoutCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport
    )

    return $false
}

function New-TuiColumnViewportCore {
    param(
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] [int]$Width
    )

    return [ordered]@{
        Width = $Width
        Height = [int]$Viewport.Height
        UseAnsi = [bool]$Viewport.UseAnsi
    }
}

function Merge-TuiColumnLinesCore {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string[]]$LeftLines,
        [Parameter(Mandatory)] [AllowEmptyString()] [string[]]$RightLines,
        [Parameter(Mandatory)] [int]$LeftWidth,
        [Parameter(Mandatory)] [int]$RightWidth,
        [int]$Gap = 2
    )

    $merged = New-Object System.Collections.Generic.List[string]
    $lineCount = [Math]::Max($LeftLines.Count, $RightLines.Count)
    for ($index = 0; $index -lt $lineCount; $index++) {
        $leftLine = if ($index -lt $LeftLines.Count) { [string]$LeftLines[$index] } else { '' }
        $rightLine = if ($index -lt $RightLines.Count) { [string]$RightLines[$index] } else { '' }
        $merged.Add((Pad-TuiLineCore -Text $leftLine -Width $LeftWidth) + (' ' * $Gap) + (Pad-TuiLineCore -Text $rightLine -Width $RightWidth))
    }

    return @($merged)
}

function New-TuiCardBlockCore {
    param(
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [string]$PrimaryText,
        [string]$SecondaryText,
        [string]$StatusBadge,
        [Parameter(Mandatory)] [bool]$Selected,
        [Parameter(Mandatory)] $Accent,
        [Parameter(Mandatory)] $Glyphs,
        [switch]$PathDetail
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $borderColor = if ($Selected) { $Accent.Accent } else { $Accent.Border }
    $focusPrefix = if ($Selected) { '> ' } else { '  ' }
    $badge = Format-TuiBadgeCore -Text ([string]$StatusBadge)
    $titleRow = Join-TuiColumnsCore -LeftText "$focusPrefix$($Accent.Text)$PrimaryText$($Accent.Reset)" -RightText $badge -Width ($Width - 4)

    $lines.Add("$borderColor$(New-TuiBoxBorderLineCore -LeftGlyph $Glyphs.TopLeft -RightGlyph $Glyphs.TopRight -Width $Width -Glyphs $Glyphs)$($Accent.Reset)")
    $lines.Add("$borderColor$($Glyphs.Vertical)$($Accent.Reset) $(Pad-TuiLineCore -Text $titleRow -Width ($Width - 4)) $borderColor$($Glyphs.Vertical)$($Accent.Reset)")

    if (-not [string]::IsNullOrWhiteSpace([string]$SecondaryText)) {
        $detailWidth = $Width - 4
        $detailLines = if ($PathDetail) {
            @(ConvertTo-TuiWrappedPathLinesCore -Text ([string]$SecondaryText) -Width $detailWidth)
        }
        else {
            @(ConvertTo-TuiWrappedLinesCore -Text ([string]$SecondaryText) -Width $detailWidth)
        }

        foreach ($detailLine in $detailLines) {
            if ([string]::IsNullOrWhiteSpace([string]$detailLine)) {
                continue
            }

            $detailText = "$($Accent.Dim)$detailLine$($Accent.Reset)"
            $lines.Add("$borderColor$($Glyphs.Vertical)$($Accent.Reset) $(Pad-TuiLineCore -Text $detailText -Width ($Width - 4)) $borderColor$($Glyphs.Vertical)$($Accent.Reset)")
        }
    }

    $lines.Add("$borderColor$(New-TuiBoxBorderLineCore -LeftGlyph $Glyphs.BottomLeft -RightGlyph $Glyphs.BottomRight -Width $Width -Glyphs $Glyphs)$($Accent.Reset)")
    return @($lines)
}

function Build-TuiHomeHeroLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $contentWidth = Get-TuiContentColumnWidthCore -Viewport $Viewport
    $heroWidth = [Math]::Min($contentWidth, [Math]::Max(34, [int]$Viewport.Width - 8))
    $lines = New-Object System.Collections.Generic.List[string]
    $homeItems = @($State.HomeItems)
    $meta = Get-TuiPageMetadataCore -State $State
    $glyphs = Get-TuiBorderGlyphsCore

    foreach ($heading in @(
            [ordered]@{ Text = "$($Accent.Text)$([string]$meta.Title)$($Accent.Reset)"; Tone = 'title' }
            [ordered]@{ Text = "$($Accent.Dim)$([string]$meta.Subtitle)$($Accent.Reset)"; Tone = 'subtitle' }
            [ordered]@{ Text = "$($Accent.Dim)$([string]$meta.Eyebrow)$($Accent.Reset)"; Tone = 'eyebrow' }
        )) {
        if ([string]::IsNullOrWhiteSpace((Remove-TuiAnsiCore -Text ([string]$heading.Text)))) {
            continue
        }

        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text ([string]$heading.Text) -Centered -ContentWidth $heroWidth))
    }

    if ([bool]$State.StartupProgressActive -and -not [string]::IsNullOrWhiteSpace([string]$State.StartupProgressTitle)) {
        $lines.Add('')
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Text)$([string]$State.StartupProgressTitle)$($Accent.Reset)" -Centered -ContentWidth $heroWidth))
        return @($lines)
    }

    $lines.Add('')

    for ($index = 0; $index -lt $homeItems.Count; $index++) {
        $item = $homeItems[$index]
        $selected = ([int]$State.SelectionIndex -eq [Array]::IndexOf($homeItems, $item))
        foreach ($cardLine in @(New-TuiCardBlockCore -Width $heroWidth -PrimaryText ([string]$item.PrimaryText) -SecondaryText ([string]$item.SecondaryText) -StatusBadge ([string]$item.StatusBadge) -Selected $selected -Accent $Accent -Glyphs $glyphs -PathDetail:([string]$item.Id -eq 'edit-root'))) {
            $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text $cardLine -ContentWidth $heroWidth))
        }
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$State.VersionHint)) {
        if ($lines.Count -gt 0) {
            $lines.Add('')
        }
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$([string]$State.VersionHint)$($Accent.Reset)" -ContentWidth $heroWidth))
    }

    return @($lines)
}

function Build-TuiPageHeaderLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $contentWidth = Get-TuiContentColumnWidthCore -Viewport $Viewport
    $meta = Get-TuiPageMetadataCore -State $State
    $lines = New-Object System.Collections.Generic.List[string]

    if (-not [string]::IsNullOrWhiteSpace([string]$meta.Eyebrow)) {
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$($meta.Eyebrow)$($Accent.Reset)" -ContentWidth $contentWidth))
    }

    $lines.Add('')
    $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Text)$($meta.Title)$($Accent.Reset)" -ContentWidth $contentWidth))
    $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$($meta.Subtitle)$($Accent.Reset)" -ContentWidth $contentWidth))

    foreach ($contextLine in @($meta.ContextLines)) {
        if ([string]::IsNullOrWhiteSpace([string]$contextLine)) {
            continue
        }

        if ([string]$contextLine -like 'Install location:*') {
            $label = 'Install location: '
            $pathText = ([string]$contextLine).Substring($label.Length)
            if ([string]$State.CurrentScreen -in @('InstallScreen', 'UninstallScreen')) {
                $compactPathText = Format-TuiCompactPathTextCore -Text $pathText -Width ([Math]::Max(1, $contentWidth - $label.Length))
                $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$label$compactPathText$($Accent.Reset)" -ContentWidth $contentWidth))
                continue
            }

            $wrappedLines = @(ConvertTo-TuiWrappedPathLinesCore -Text $pathText -Width ($contentWidth - $label.Length))
            for ($index = 0; $index -lt $wrappedLines.Count; $index++) {
                $prefix = if ($index -eq 0) { $label } else { (' ' * $label.Length) }
                $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$prefix$($wrappedLines[$index])$($Accent.Reset)" -ContentWidth $contentWidth))
            }
            continue
        }

        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$contextLine$($Accent.Reset)" -ContentWidth $contentWidth))
    }

    $lines.Add('')
    return @($lines)
}

function Build-TuiListBodyLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent,
        [int]$ContentWidth,
        [switch]$ColumnOnly
    )

    $contentWidth = if ($ContentWidth -gt 0) { $ContentWidth } else { Get-TuiContentColumnWidthCore -Viewport $Viewport }
    $lineViewport = if ($ColumnOnly) { New-TuiColumnViewportCore -Viewport $Viewport -Width $contentWidth } else { $Viewport }
    $allItems = @(Get-TuiCurrentItems -State $State)
    $visibleItems = @(Get-TuiVisibleItems -State $State)
    $lines = New-Object System.Collections.Generic.List[string]
    $glyphs = Get-TuiBorderGlyphsCore

    for ($index = 0; $index -lt $visibleItems.Count; $index++) {
        $absoluteIndex = [int]$State.ScrollOffset + $index
        $item = $visibleItems[$index]
        if ($item.ShowDividerBefore) {
            $divider = "$($Accent.Border)$(New-TuiBoxBorderLineCore -LeftGlyph $glyphs.TeeLeft -RightGlyph $glyphs.TeeRight -Width $contentWidth -Glyphs $glyphs)$($Accent.Reset)"
            $lines.Add((New-TuiViewportLineCore -Viewport $lineViewport -Text $divider -ContentWidth $contentWidth))
        }

        $selected = ($absoluteIndex -eq [int]$State.SelectionIndex)
        foreach ($cardLine in @(New-TuiCardBlockCore -Width $contentWidth -PrimaryText ([string]$item.PrimaryText) -SecondaryText ([string]$item.SecondaryText) -StatusBadge ([string]$item.StatusBadge) -Selected $selected -Accent $Accent -Glyphs $glyphs)) {
            $lines.Add((New-TuiViewportLineCore -Viewport $lineViewport -Text $cardLine -ContentWidth $contentWidth))
        }
    }

    if ($allItems.Count -gt [int]$State.VisibleWindowSize) {
        $lastIndex = [Math]::Min($allItems.Count, [int]$State.ScrollOffset + [int]$State.VisibleWindowSize)
        $lines.Add((New-TuiViewportLineCore -Viewport $lineViewport -Text "$($Accent.Dim)Showing $([int]$State.ScrollOffset + 1)-$lastIndex of $($allItems.Count)$($Accent.Reset)" -ContentWidth $contentWidth))
    }

    return @($lines)
}

$titleBarHelperPath = Join-Path -Path $PSScriptRoot -ChildPath 'Tui.TitleBar.ps1'
if (Test-Path -LiteralPath $titleBarHelperPath) {
    . $titleBarHelperPath
}

$statusBarHelperPath = Join-Path -Path $PSScriptRoot -ChildPath 'Tui.StatusBar.ps1'
if (Test-Path -LiteralPath $statusBarHelperPath) {
    . $statusBarHelperPath
}
