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

function Get-TuiRuleLineCore {
    param(
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] $Glyphs
    )

    return [string]::new([char]$Glyphs.Horizontal, [Math]::Max(1, $Width))
}

function New-TuiBoxLineCore {
    param(
        [Parameter(Mandatory)] [string]$Text,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] $Glyphs
    )

    return "$($Glyphs.Vertical)$(Pad-TuiLineCore -Text $Text -Width ($Width - 2))$($Glyphs.Vertical)"
}

function New-TuiBoxBorderLineCore {
    param(
        [Parameter(Mandatory)] [string]$LeftGlyph,
        [Parameter(Mandatory)] [string]$RightGlyph,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] $Glyphs
    )

    return "$LeftGlyph$(Get-TuiRuleLineCore -Width ($Width - 2) -Glyphs $Glyphs)$RightGlyph"
}

function Get-TuiCaptionControlsTextCore {
    return '[_] [ ] [X]'
}
