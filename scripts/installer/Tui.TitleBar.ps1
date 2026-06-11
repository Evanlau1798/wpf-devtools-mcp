function Get-TuiTitleBarLeftTextCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Accent
    )

    $meta = Get-TuiPageMetadataCore -State $State
    $parts = New-Object System.Collections.Generic.List[string]
    $parts.Add('WPF DevTools MCP')

    if (([string]$State.CurrentScreen -ne 'HomeScreen') -and
        -not [string]::IsNullOrWhiteSpace([string]$meta.Title)) {
        $parts.Add([string]$meta.Title)
    }

    return "$($Accent.Dim) $($parts -join '   ')$($Accent.Reset)"
}

function Build-TuiTitleBarLinesCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent
    )

    $captionControls = "$($Accent.Dim)$(Get-TuiCaptionControlsTextCore)$($Accent.Reset)"
    $leftText = Get-TuiTitleBarLeftTextCore -State $State -Accent $Accent
    $rule = "$($Accent.Border)$(Get-TuiRuleLineCore -Width ([int]$Viewport.Width) -Glyphs (Get-TuiBorderGlyphsCore))$($Accent.Reset)"

    return @(
        (Join-TuiColumnsCore -LeftText $leftText -RightText $captionControls -Width ([int]$Viewport.Width))
        $rule
    )
}
