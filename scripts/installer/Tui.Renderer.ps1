function Get-TuiAccent {
    $useAnsi = Test-TuiAnsiSupportCore
    if (-not $useAnsi) {
        return [ordered]@{
            Reset = ''
            Dim = ''
            Text = ''
            Accent = ''
            Primary = ''
            Banner = ''
            Ok = ''
            Warn = ''
            Error = ''
            Border = ''
        }
    }

    return [ordered]@{
        Reset = "$([char]27)[0m"
        Dim = "$([char]27)[38;5;246m"
        Text = "$([char]27)[38;5;255m"
        Accent = "$([char]27)[38;5;223m"
        Primary = "$([char]27)[48;5;238m$([char]27)[38;5;255m"
        Banner = "$([char]27)[48;5;52m$([char]27)[38;5;255m"
        Ok = "$([char]27)[38;5;120m"
        Warn = "$([char]27)[38;5;221m"
        Error = "$([char]27)[38;5;203m"
        Border = "$([char]27)[38;5;240m"
    }
}

function Get-TuiVisibleItems {
    param([Parameter(Mandatory)] $State)

    $items = @(Get-TuiCurrentItems -State $State)
    $offset = [int]$State.ScrollOffset
    $count = [Math]::Min([int]$State.VisibleWindowSize, [Math]::Max(0, $items.Count - $offset))
    if ($count -le 0) {
        return @()
    }

    return @($items[$offset..($offset + $count - 1)])
}

function Get-TuiScreenTitle {
    param([Parameter(Mandatory)] $State)

    switch ([string]$State.CurrentScreen) {
        'HomeScreen' { return 'HomeScreen' }
        'InstallScreen' { return 'InstallScreen' }
        'UninstallScreen' { return 'UninstallScreen' }
        'ConfirmScreen' { return 'ConfirmScreen' }
        'ProgressScreen' { return 'ProgressScreen' }
        default { return [string]$State.CurrentScreen }
    }
}

function Get-TuiPageMetadataCore {
    param([Parameter(Mandatory)] $State)

    switch ([string]$State.CurrentScreen) {
        'HomeScreen' {
            return [ordered]@{
                Title = 'WPF DevTools MCP'
                Subtitle = 'Model Context Protocol Server'
                Eyebrow = 'Installation Manager'
            }
        }
        'InstallScreen' {
            return [ordered]@{
                Title = 'Where would you like to install?'
                Subtitle = 'Choose the target AI tool.'
                Eyebrow = '<- Back'
            }
        }
        'UninstallScreen' {
            return [ordered]@{
                Title = 'Select what to uninstall'
                Subtitle = 'Only detected installed targets can be removed.'
                Eyebrow = '<- Back'
            }
        }
        'ConfirmScreen' {
            return [ordered]@{
                Title = 'Confirm action'
                Subtitle = 'Two-step confirmation is required.'
                Eyebrow = '<- Back'
            }
        }
        default {
            return [ordered]@{
                Title = 'Working...'
                Subtitle = 'Please wait while the installer completes the current operation.'
                Eyebrow = ''
            }
        }
    }
}

function Format-TuiBadgeCore {
    param(
        [string]$Text,
        [string]$Tone = 'accent'
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ''
    }

    $accent = Get-TuiAccent
    $prefix = switch ($Tone) {
        'warn' { $accent.Warn }
        'ok' { $accent.Ok }
        'error' { $accent.Error }
        default { $accent.Accent }
    }

    return "$prefix[$Text]$($accent.Reset)"
}

function Build-TuiTitleBarLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $brand = "$($Accent.Dim)  WPF DevTools MCP$($Accent.Reset)"
    $right = "$($Accent.Dim)$(Get-TuiScreenTitle -State $State) | $([string]$State.SelectedArchitecture)$($Accent.Reset)"
    return @(
        (Join-TuiColumnsCore -LeftText $brand -RightText $right -Width ([int]$Viewport.Width))
    )
}

function Build-TuiHomeHeroLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $contentWidth = Get-TuiContentColumnWidthCore -Viewport $Viewport
    $lines = New-Object System.Collections.Generic.List[string]
    $meta = Get-TuiPageMetadataCore -State $State
    $homeItems = @($State.HomeItems)
    $primaryItems = @($homeItems | Where-Object { $_.IsPrimaryAction })
    $secondaryItems = @($homeItems | Where-Object { -not $_.IsPrimaryAction })

    $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Text)$($meta.Title)$($Accent.Reset)" -Centered -ContentWidth $contentWidth))
    $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$($meta.Subtitle)$($Accent.Reset)" -Centered -ContentWidth $contentWidth))
    $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$($meta.Eyebrow)$($Accent.Reset)" -Centered -ContentWidth $contentWidth))
    $lines.Add('')

    foreach ($item in $primaryItems) {
        $selected = ([int]$State.SelectionIndex -eq [Array]::IndexOf($homeItems, $item))
        $borderColor = if ($selected) { $Accent.Accent } else { $Accent.Border }
        $focusPrefix = if ($selected) { '>' } else { ' ' }
        $titleLine = "$focusPrefix $($item.PrimaryText)"
        $descriptionLine = "  $($item.SecondaryText)"
        $cardBorder = [string]::new('-', $contentWidth - 2)
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$borderColor+$cardBorder+$($Accent.Reset)" -ContentWidth $contentWidth))
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$borderColor|$($Accent.Reset)$(Pad-TuiLineCore -Text $titleLine -Width ($contentWidth - 2))$borderColor|$($Accent.Reset)" -ContentWidth $contentWidth))
        $descriptionText = "$($Accent.Dim)$descriptionLine$($Accent.Reset)"
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$borderColor|$($Accent.Reset)$(Pad-TuiLineCore -Text $descriptionText -Width ($contentWidth - 2))$borderColor|$($Accent.Reset)" -ContentWidth $contentWidth))
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$borderColor+$cardBorder+$($Accent.Reset)" -ContentWidth $contentWidth))
        $lines.Add('')
    }

    foreach ($item in $secondaryItems) {
        $selected = ([int]$State.SelectionIndex -eq [Array]::IndexOf($homeItems, $item))
        $prefix = if ($selected) { "$($Accent.Accent)>$($Accent.Reset)" } else { ' ' }
        $row = "$prefix $($Accent.Text)$($item.PrimaryText)$($Accent.Reset)"
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text (Pad-TuiLineCore -Text $row -Width $contentWidth) -ContentWidth $contentWidth))
        if ($selected) {
            foreach ($detailLine in @(ConvertTo-TuiWrappedLinesCore -Text ([string]$item.SecondaryText) -Width ($contentWidth - 4))) {
                $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "   $($Accent.Dim)$detailLine$($Accent.Reset)" -ContentWidth $contentWidth))
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$State.VersionHint)) {
        $lines.Add('')
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$([string]$State.VersionHint)$($Accent.Reset)" -ContentWidth $contentWidth))
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
    return @(
        (New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$($meta.Eyebrow)$($Accent.Reset)" -ContentWidth $contentWidth)
        ''
        (New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Text)$($meta.Title)$($Accent.Reset)" -ContentWidth $contentWidth)
        (New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)$($meta.Subtitle)$($Accent.Reset)" -ContentWidth $contentWidth)
        ''
    )
}

function Build-TuiListBodyLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $contentWidth = Get-TuiContentColumnWidthCore -Viewport $Viewport
    $allItems = @(Get-TuiCurrentItems -State $State)
    $visibleItems = @(Get-TuiVisibleItems -State $State)
    $lines = New-Object System.Collections.Generic.List[string]

    for ($index = 0; $index -lt $visibleItems.Count; $index++) {
        $absoluteIndex = [int]$State.ScrollOffset + $index
        $item = $visibleItems[$index]
        if ($item.ShowDividerBefore) {
            $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Border)$(Get-TuiFullUninstallDivider)$($Accent.Reset)" -ContentWidth $contentWidth))
        }

        $selected = ($absoluteIndex -eq [int]$State.SelectionIndex)
        $prefix = if ($selected) { "$($Accent.Accent)>$($Accent.Reset)" } else { ' ' }
        $badge = Format-TuiBadgeCore -Text ([string]$item.StatusBadge)
        $primaryRow = Join-TuiColumnsCore -LeftText "$prefix $($Accent.Text)$([string]$item.PrimaryText)$($Accent.Reset)" -RightText $badge -Width $contentWidth
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text $primaryRow -ContentWidth $contentWidth))

        $secondarySource = if ($selected) { [string]$item.SecondaryText } else { '' }
        foreach ($detailLine in @(ConvertTo-TuiWrappedLinesCore -Text $secondarySource -Width ($contentWidth - 4))) {
            if ([string]::IsNullOrWhiteSpace($detailLine)) {
                continue
            }

            $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "   $($Accent.Dim)$detailLine$($Accent.Reset)" -ContentWidth $contentWidth))
        }
    }

    if ($allItems.Count -gt [int]$State.VisibleWindowSize) {
        $lines.Add('')
        $lastIndex = [Math]::Min($allItems.Count, [int]$State.ScrollOffset + [int]$State.VisibleWindowSize)
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Dim)Showing $([int]$State.ScrollOffset + 1)-$lastIndex of $($allItems.Count)$($Accent.Reset)" -ContentWidth $contentWidth))
    }

    return @($lines)
}

function Build-TuiStatusPanelLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $contentWidth = Get-TuiContentColumnWidthCore -Viewport $Viewport
    $lines = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrWhiteSpace([string]$State.StatusMessage)) {
        return @()
    }

    $tone = 'ok'
    if ([string]$State.StatusMessage -match 'failed|error') {
        $tone = 'error'
    }

    $badge = Format-TuiBadgeCore -Text 'Status' -Tone $tone
    $statusRule = [string]::new('-', [Math]::Max(12, $contentWidth - 10))
    $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text (Join-TuiColumnsCore -LeftText "$($Accent.Border)$statusRule$($Accent.Reset)" -RightText $badge -Width $contentWidth) -ContentWidth $contentWidth))
    foreach ($statusLine in @(ConvertTo-TuiWrappedLinesCore -Text ([string]$State.StatusMessage) -Width $contentWidth)) {
        $lines.Add((New-TuiViewportLineCore -Viewport $Viewport -Text "$($Accent.Text)$statusLine$($Accent.Reset)" -ContentWidth $contentWidth))
    }

    return @($lines)
}

function Build-TuiFooterLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $footerWidth = [Math]::Max(40, [int]$Viewport.Width - 6)
    return @(
        (Pad-TuiLineCore -Text "$($Accent.Dim)Enter select  Up/Down move  Left/Right architecture  Escape back  Backspace back$($Accent.Reset)" -Width ([int]$Viewport.Width))
        (Pad-TuiLineCore -Text "$($Accent.Dim)Update All and install location are available from the home screen.$($Accent.Reset)" -Width ([int]$Viewport.Width))
    )
}

function New-TuiFrameLinesCore {
    param([Parameter(Mandatory)] $State)

    $viewport = Get-TuiViewportCore
    $accent = Get-TuiAccent
    $frameLines = New-Object System.Collections.Generic.List[string]

    foreach ($line in @(Build-TuiTitleBarLinesCore -State $State -Viewport $viewport -Accent $accent)) {
        $frameLines.Add((Pad-TuiLineCore -Text $line -Width ([int]$viewport.Width)))
    }
    $frameLines.Add((Pad-TuiLineCore -Text '' -Width ([int]$viewport.Width)))

    if (-not [string]::IsNullOrWhiteSpace([string]$State.UpdateBannerText)) {
        $frameLines.Add((Pad-TuiLineCore -Text "$($accent.Banner) Update available $($accent.Reset) $($accent.Warn)$([string]$State.UpdateBannerText)$($accent.Reset)" -Width ([int]$viewport.Width)))
        $frameLines.Add((Pad-TuiLineCore -Text '' -Width ([int]$viewport.Width)))
    }

    $bodyLines = switch ([string]$State.CurrentScreen) {
        'HomeScreen' { @(Build-TuiHomeHeroLinesCore -State $State -Viewport $viewport -Accent $accent) }
        'InstallScreen' {
            @(
                @(Build-TuiPageHeaderLinesCore -State $State -Viewport $viewport -Accent $accent)
                @(Build-TuiListBodyLinesCore -State $State -Viewport $viewport -Accent $accent)
            )
        }
        'UninstallScreen' {
            @(
                @(Build-TuiPageHeaderLinesCore -State $State -Viewport $viewport -Accent $accent)
                @(Build-TuiListBodyLinesCore -State $State -Viewport $viewport -Accent $accent)
            )
        }
        'ConfirmScreen' {
            $contentWidth = Get-TuiContentColumnWidthCore -Viewport $viewport
            @(
                @(Build-TuiPageHeaderLinesCore -State $State -Viewport $viewport -Accent $accent)
                foreach ($line in @(Get-TuiConfirmationLinesCore -State $State)) {
                    New-TuiViewportLineCore -Viewport $viewport -Text "$($accent.Text)$line$($accent.Reset)" -ContentWidth $contentWidth
                }
            )
        }
        default {
            $contentWidth = Get-TuiContentColumnWidthCore -Viewport $viewport
            @(
                @(Build-TuiPageHeaderLinesCore -State $State -Viewport $viewport -Accent $accent)
                (New-TuiViewportLineCore -Viewport $viewport -Text "$($accent.Text)$([string]$State.StatusMessage)$($accent.Reset)" -ContentWidth $contentWidth)
            )
        }
    }

    foreach ($line in $bodyLines) {
        $frameLines.Add((Pad-TuiLineCore -Text $line -Width ([int]$viewport.Width)))
    }

    $statusLines = @(Build-TuiStatusPanelLinesCore -State $State -Viewport $viewport -Accent $accent)
    if ($statusLines.Count -gt 0) {
        $frameLines.Add((Pad-TuiLineCore -Text '' -Width ([int]$viewport.Width)))
        foreach ($line in $statusLines) {
            $frameLines.Add((Pad-TuiLineCore -Text $line -Width ([int]$viewport.Width)))
        }
    }

    $footerLines = @(Build-TuiFooterLinesCore -State $State -Viewport $viewport -Accent $accent)
    $blankLinesNeeded = [Math]::Max(1, [int]$viewport.Height - $frameLines.Count - $footerLines.Count)
    for ($index = 0; $index -lt $blankLinesNeeded; $index++) {
        $frameLines.Add((Pad-TuiLineCore -Text '' -Width ([int]$viewport.Width)))
    }

    foreach ($line in $footerLines) {
        $frameLines.Add((Pad-TuiLineCore -Text $line -Width ([int]$viewport.Width)))
    }

    return @($frameLines)
}

function Render-TuiScreenCore {
    param(
        [Parameter(Mandatory)] $State,
        [switch]$AsString
    )

    $lines = @(New-TuiFrameLinesCore -State $State)
    if ($AsString) {
        return ($lines -join [Environment]::NewLine)
    }

    if ([string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR)) {
        try { Clear-Host } catch {}
    }

    foreach ($line in $lines) {
        Write-Host $line
    }
}
