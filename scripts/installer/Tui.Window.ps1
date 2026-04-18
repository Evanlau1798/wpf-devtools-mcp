function Get-TuiWindowViewportCore {
    param([Parameter(Mandatory)] $Viewport)

    return [ordered]@{
        Width = [Math]::Max(18, [int]$Viewport.Width - 2)
        Height = [Math]::Max(10, [int]$Viewport.Height - 2)
        UseAnsi = [bool]$Viewport.UseAnsi
    }
}

function New-TuiWindowBorderLineCore {
    param(
        [Parameter(Mandatory)] [string]$LeftGlyph,
        [Parameter(Mandatory)] [string]$RightGlyph,
        [Parameter(Mandatory)] [int]$InnerWidth,
        [Parameter(Mandatory)] $Glyphs,
        [Parameter(Mandatory)] $Accent
    )

    $horizontal = Get-TuiRuleLineCore -Width $InnerWidth -Glyphs $Glyphs
    return "$($Accent.Border)$LeftGlyph$horizontal$RightGlyph$($Accent.Reset)"
}

function New-TuiWindowWrappedLineCore {
    param(
        [string]$Text,
        [Parameter(Mandatory)] [int]$InnerWidth,
        [Parameter(Mandatory)] $Glyphs,
        [Parameter(Mandatory)] $Accent
    )

    $content = Pad-TuiLineCore -Text ([string]$Text) -Width $InnerWidth
    return "$($Accent.Border)$($Glyphs.Vertical)$($Accent.Reset)$content$($Accent.Border)$($Glyphs.Vertical)$($Accent.Reset)"
}

function New-TuiWindowFrameCore {
    param(
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] $Accent,
        [Parameter(Mandatory)] [string[]]$InnerLines
    )

    $glyphs = Get-TuiBorderGlyphsCore
    $innerWidth = [Math]::Max(18, [int]$Viewport.Width - 2)
    $innerHeight = [Math]::Max(10, [int]$Viewport.Height - 2)
    $normalizedLines = New-Object System.Collections.Generic.List[string]
    foreach ($line in @($InnerLines)) {
        if ($normalizedLines.Count -ge $innerHeight) {
            break
        }

        $normalizedLines.Add((New-TuiWindowWrappedLineCore -Text ([string]$line) -InnerWidth $innerWidth -Glyphs $glyphs -Accent $Accent))
    }

    while ($normalizedLines.Count -lt $innerHeight) {
        $normalizedLines.Add((New-TuiWindowWrappedLineCore -Text '' -InnerWidth $innerWidth -Glyphs $glyphs -Accent $Accent))
    }

    return @(
        (New-TuiWindowBorderLineCore -LeftGlyph $glyphs.TopLeft -RightGlyph $glyphs.TopRight -InnerWidth $innerWidth -Glyphs $glyphs -Accent $Accent)
        @($normalizedLines)
        (New-TuiWindowBorderLineCore -LeftGlyph $glyphs.BottomLeft -RightGlyph $glyphs.BottomRight -InnerWidth $innerWidth -Glyphs $glyphs -Accent $Accent)
    )
}
