function Remove-TuiAnsiCore {
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) {
        return ''
    }

    return ([regex]::Replace($Text, "$([char]27)\[[0-9;?]*[ -/]*[@-~]", ''))
}

function Get-TuiDisplayWidthCore {
    param([string]$Text)

    return (Remove-TuiAnsiCore -Text $Text).Length
}

function Get-TuiContentColumnWidthCore {
    param([Parameter(Mandatory)] $Viewport)

    $availableWidth = [Math]::Max(20, [int]$Viewport.Width - 4)
    return [Math]::Min(72, $availableWidth)
}

function Get-TuiContentLeftPaddingCore {
    param(
        [Parameter(Mandatory)] $Viewport,
        [Parameter(Mandatory)] [int]$ContentWidth
    )

    return [Math]::Max(0, [Math]::Floor(([int]$Viewport.Width - $ContentWidth) / 2))
}

function ConvertTo-TuiWrappedLinesCore {
    param(
        [string]$Text,
        [Parameter(Mandatory)] [int]$Width
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @('')
    }

    $normalized = $Text -replace "`r", ''
    $paragraphs = $normalized -split "`n"
    $wrapped = New-Object System.Collections.Generic.List[string]
    foreach ($paragraph in $paragraphs) {
        if ([string]::IsNullOrWhiteSpace($paragraph)) {
            $wrapped.Add('')
            continue
        }

        $current = ''
        foreach ($word in ($paragraph -split '\s+')) {
            if ([string]::IsNullOrWhiteSpace($word)) {
                continue
            }

            $candidate = if ([string]::IsNullOrWhiteSpace($current)) { $word } else { "$current $word" }
            if ((Get-TuiDisplayWidthCore -Text $candidate) -le $Width) {
                $current = $candidate
                continue
            }

            if (-not [string]::IsNullOrWhiteSpace($current)) {
                $wrapped.Add($current)
            }

            if ((Get-TuiDisplayWidthCore -Text $word) -le $Width) {
                $current = $word
                continue
            }

            $remaining = $word
            while ((Get-TuiDisplayWidthCore -Text $remaining) -gt $Width) {
                $chunkWidth = [Math]::Max(1, $Width - 3)
                $wrapped.Add($remaining.Substring(0, $chunkWidth) + '...')
                $remaining = $remaining.Substring($chunkWidth)
            }

            $current = $remaining
        }

        if (-not [string]::IsNullOrWhiteSpace($current)) {
            $wrapped.Add($current)
        }
    }

    return @($wrapped)
}

function ConvertTo-TuiWrappedPathLinesCore {
    param(
        [string]$Text,
        [Parameter(Mandatory)] [int]$Width
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @('')
    }

    $normalized = [string]$Text
    $tokenPattern = '([\\\/]+|[^\\\/]+)'
    $tokens = [regex]::Matches($normalized, $tokenPattern) | ForEach-Object { $_.Value }
    if ($tokens.Count -eq 0) {
        return @(ConvertTo-TuiWrappedLinesCore -Text $Text -Width $Width)
    }

    $wrapped = New-Object System.Collections.Generic.List[string]
    $current = ''
    foreach ($token in $tokens) {
        $candidate = if ([string]::IsNullOrEmpty($current)) { $token } else { $current + $token }
        if ((Get-TuiDisplayWidthCore -Text $candidate) -le $Width) {
            $current = $candidate
            continue
        }

        if (-not [string]::IsNullOrEmpty($current)) {
            $wrapped.Add($current)
            $current = ''
        }

        if ((Get-TuiDisplayWidthCore -Text $token) -le $Width) {
            $current = $token
            continue
        }

        foreach ($segment in @(ConvertTo-TuiWrappedLinesCore -Text $token -Width $Width)) {
            if ([string]::IsNullOrWhiteSpace($segment)) {
                continue
            }

            $wrapped.Add($segment)
        }
    }

    if (-not [string]::IsNullOrEmpty($current)) {
        $wrapped.Add($current)
    }

    return @($wrapped)
}

function Fit-TuiTextCore {
    param(
        [string]$Text,
        [Parameter(Mandatory)] [int]$Width
    )

    $plainText = [string](Remove-TuiAnsiCore -Text $Text)
    if ($plainText.Length -le $Width) {
        return $plainText
    }

    if ($Width -le 1) {
        return $plainText.Substring(0, [Math]::Max(0, $Width))
    }

    if ($Width -le 3) {
        return $plainText.Substring(0, $Width)
    }

    return $plainText.Substring(0, $Width - 3) + '...'
}

function Pad-TuiLineCore {
    param(
        [string]$Text,
        [Parameter(Mandatory)] [int]$Width
    )

    $displayWidth = Get-TuiDisplayWidthCore -Text $Text
    if ($displayWidth -gt $Width) {
        $Text = Fit-TuiTextCore -Text $Text -Width $Width
        $displayWidth = Get-TuiDisplayWidthCore -Text $Text
    }

    return ($Text + (' ' * [Math]::Max(0, $Width - $displayWidth)))
}

function Join-TuiColumnsCore {
    param(
        [string]$LeftText,
        [string]$RightText,
        [Parameter(Mandatory)] [int]$Width
    )

    $left = [string]$LeftText
    $right = [string]$RightText
    if ([string]::IsNullOrWhiteSpace($right)) {
        return (Pad-TuiLineCore -Text $left -Width $Width)
    }

    $rightWidth = Get-TuiDisplayWidthCore -Text $right
    $maxLeftWidth = [Math]::Max(0, $Width - $rightWidth - 1)
    $left = Fit-TuiTextCore -Text $left -Width $maxLeftWidth
    $left = Pad-TuiLineCore -Text $left -Width $maxLeftWidth
    return (Pad-TuiLineCore -Text ($left + ' ' + $right) -Width $Width)
}

function New-TuiViewportLineCore {
    param(
        [Parameter(Mandatory)] $Viewport,
        [string]$Text,
        [switch]$Centered,
        [int]$ContentWidth
    )

    $resolvedContentWidth = if ($ContentWidth -gt 0) { $ContentWidth } else { Get-TuiContentColumnWidthCore -Viewport $Viewport }
    $leftPadding = Get-TuiContentLeftPaddingCore -Viewport $Viewport -ContentWidth $resolvedContentWidth
    $innerText = if ($Centered) {
        $plain = Fit-TuiTextCore -Text $Text -Width $resolvedContentWidth
        $plainDisplayWidth = Get-TuiDisplayWidthCore -Text $plain
        (' ' * [Math]::Max(0, [Math]::Floor(($resolvedContentWidth - $plainDisplayWidth) / 2))) + $plain
    }
    else {
        Pad-TuiLineCore -Text $Text -Width $resolvedContentWidth
    }

    return (Pad-TuiLineCore -Text ((' ' * $leftPadding) + $innerText) -Width ([int]$Viewport.Width))
}
