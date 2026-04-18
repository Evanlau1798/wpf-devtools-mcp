function New-TuiFrameLinesCore {
    param([Parameter(Mandatory)] $State)

    $viewport = Get-TuiViewportCore
    $windowViewport = Get-TuiWindowViewportCore -Viewport $viewport
    $accent = Get-TuiAccent
    $State = Update-TuiVisibleWindowSizeCore -State $State -Viewport $windowViewport
    $innerLines = New-Object System.Collections.Generic.List[string]

    foreach ($line in @(Build-TuiTitleBarLinesCore -State $State -Viewport $windowViewport -Accent $accent)) {
        $innerLines.Add((Pad-TuiLineCore -Text ([string]$line) -Width ([int]$windowViewport.Width)))
    }
    $innerLines.Add((Pad-TuiLineCore -Text '' -Width ([int]$windowViewport.Width)))

    if (-not [string]::IsNullOrWhiteSpace([string]$State.UpdateBannerText)) {
        $bannerLine = "$($accent.Banner) Update available $($accent.Reset) $($accent.Warn)$([string]$State.UpdateBannerText)$($accent.Reset)"
        $innerLines.Add((Pad-TuiLineCore -Text $bannerLine -Width ([int]$windowViewport.Width)))
        $innerLines.Add((Pad-TuiLineCore -Text '' -Width ([int]$windowViewport.Width)))
    }

    $statusLines = @()
    $bodyLines = switch ([string]$State.CurrentScreen) {
        'HomeScreen' {
            @(Build-TuiHomeHeroLinesCore -State $State -Viewport $windowViewport -Accent $accent)
        }
        'InstallScreen' {
            @(
                @(Build-TuiPageHeaderLinesCore -State $State -Viewport $windowViewport -Accent $accent)
                @(Build-TuiListBodyLinesCore -State $State -Viewport $windowViewport -Accent $accent)
            )
        }
        'UninstallScreen' {
            @(
                @(Build-TuiPageHeaderLinesCore -State $State -Viewport $windowViewport -Accent $accent)
                @(Build-TuiListBodyLinesCore -State $State -Viewport $windowViewport -Accent $accent)
            )
        }
        'PathEditorScreen' {
            @(
                @(Build-TuiPageHeaderLinesCore -State $State -Viewport $windowViewport -Accent $accent)
                @(Build-TuiPathEditorLinesCore -State $State -Viewport $windowViewport -Accent $accent)
            )
        }
        'DirectoryPickerScreen' {
            @(
                @(Build-TuiPageHeaderLinesCore -State $State -Viewport $windowViewport -Accent $accent)
                @(Build-TuiDirectoryPickerLinesCore -State $State -Viewport $windowViewport -Accent $accent)
            )
        }
        'FolderNamePromptScreen' {
            @(
                @(Build-TuiPageHeaderLinesCore -State $State -Viewport $windowViewport -Accent $accent)
                @(Build-TuiFolderNamePromptLinesCore -State $State -Viewport $windowViewport -Accent $accent)
            )
        }
        'ConfirmScreen' {
            $contentWidth = Get-TuiContentColumnWidthCore -Viewport $windowViewport
            @(
                @(Build-TuiPageHeaderLinesCore -State $State -Viewport $windowViewport -Accent $accent)
                foreach ($line in @(Get-TuiConfirmationLinesCore -State $State)) {
                    New-TuiViewportLineCore -Viewport $windowViewport -Text "$($accent.Text)$line$($accent.Reset)" -ContentWidth $contentWidth
                }
            )
        }
        default {
            $contentWidth = Get-TuiContentColumnWidthCore -Viewport $windowViewport
            @(
                @(Build-TuiPageHeaderLinesCore -State $State -Viewport $windowViewport -Accent $accent)
                (New-TuiViewportLineCore -Viewport $windowViewport -Text "$($accent.Text)$([string]$State.StatusMessage)$($accent.Reset)" -ContentWidth $contentWidth)
            )
        }
    }

    foreach ($line in @($bodyLines)) {
        $innerLines.Add((Pad-TuiLineCore -Text ([string]$line) -Width ([int]$windowViewport.Width)))
    }

    $footerLines = @(Build-TuiFooterLinesCore -State $State -Viewport $windowViewport -Accent $accent)
    $blankLinesNeeded = [Math]::Max(0, [int]$windowViewport.Height - $innerLines.Count - $footerLines.Count)
    for ($index = 0; $index -lt $blankLinesNeeded; $index++) {
        $innerLines.Add((Pad-TuiLineCore -Text '' -Width ([int]$windowViewport.Width)))
    }

    foreach ($line in $footerLines) {
        $innerLines.Add((Pad-TuiLineCore -Text ([string]$line) -Width ([int]$windowViewport.Width)))
    }

    return @(New-TuiWindowFrameCore -Viewport $viewport -Accent $accent -InnerLines @($innerLines))
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
