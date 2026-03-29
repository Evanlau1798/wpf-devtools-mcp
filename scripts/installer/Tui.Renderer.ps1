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
    $blankLinesNeeded = [Math]::Max(0, [int]$viewport.Height - $frameLines.Count - $footerLines.Count)
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

    Write-TuiFrameCore -Lines $lines -Viewport (Get-TuiViewportCore)
}
