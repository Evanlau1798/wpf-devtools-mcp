function Remove-TuiAnsiCore {
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) {
        return ''
    }

    return ([regex]::Replace($Text, "$([char]27)\[[0-9;?]*[ -/]*[@-~]", ''))
}

function Get-TuiCodePointLengthCore {
    param([Parameter(Mandatory)] [int]$CodePoint)

    if ($CodePoint -gt 0xFFFF) {
        return 2
    }

    return 1
}

function Test-TuiCodePointIsWideCore {
    param([Parameter(Mandatory)] [int]$CodePoint)

    return (($CodePoint -ge 0x1100) -and (
            ($CodePoint -le 0x115F) -or
            ($CodePoint -eq 0x2329) -or
            ($CodePoint -eq 0x232A) -or
            (($CodePoint -ge 0x2E80) -and ($CodePoint -le 0xA4CF) -and ($CodePoint -ne 0x303F)) -or
            (($CodePoint -ge 0xAC00) -and ($CodePoint -le 0xD7A3)) -or
            (($CodePoint -ge 0xF900) -and ($CodePoint -le 0xFAFF)) -or
            (($CodePoint -ge 0xFE10) -and ($CodePoint -le 0xFE19)) -or
            (($CodePoint -ge 0xFE30) -and ($CodePoint -le 0xFE6F)) -or
            (($CodePoint -ge 0xFF00) -and ($CodePoint -le 0xFF60)) -or
            (($CodePoint -ge 0xFFE0) -and ($CodePoint -le 0xFFE6)) -or
            (($CodePoint -ge 0x1F300) -and ($CodePoint -le 0x1F64F)) -or
            (($CodePoint -ge 0x1F900) -and ($CodePoint -le 0x1F9FF)) -or
            (($CodePoint -ge 0x20000) -and ($CodePoint -le 0x3FFFD))))
}

function Get-TuiCodePointDisplayWidthCore {
    param(
        [Parameter(Mandatory)] [string]$Text,
        [Parameter(Mandatory)] [int]$Index
    )

    if ([string]::IsNullOrEmpty($Text) -or $Index -lt 0 -or $Index -ge $Text.Length) {
        return 0
    }

    $codePoint = [char]::ConvertToUtf32($Text, $Index)
    if (($codePoint -lt 32) -or (($codePoint -ge 127) -and ($codePoint -lt 160))) {
        return 0
    }

    $category = [System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($Text, $Index)
    if (($category -eq [System.Globalization.UnicodeCategory]::NonSpacingMark) -or
        ($category -eq [System.Globalization.UnicodeCategory]::EnclosingMark) -or
        ($category -eq [System.Globalization.UnicodeCategory]::Format)) {
        return 0
    }

    if (Test-TuiCodePointIsWideCore -CodePoint $codePoint) {
        return 2
    }

    return 1
}

function Get-TuiDisplayWidthCacheCore {
    if ($null -eq $script:TuiDisplayWidthCache) {
        $script:TuiDisplayWidthCache = @{}
    }

    return $script:TuiDisplayWidthCache
}

function Get-TuiDisplayWidthCore {
    param([string]$Text)

    $plainText = Remove-TuiAnsiCore -Text $Text
    if ([string]::IsNullOrEmpty($plainText)) {
        return 0
    }

    $cache = Get-TuiDisplayWidthCacheCore
    if ($cache.ContainsKey($plainText)) {
        return [int]$cache[$plainText]
    }

    $width = 0
    for ($index = 0; $index -lt $plainText.Length; ) {
        $codePoint = [char]::ConvertToUtf32($plainText, $index)
        $width += Get-TuiCodePointDisplayWidthCore -Text $plainText -Index $index
        $index += Get-TuiCodePointLengthCore -CodePoint $codePoint
    }

    if ($cache.Count -ge 4096) {
        $cache.Clear()
    }
    $cache[$plainText] = $width
    return $width
}

function Split-TuiTextByDisplayWidthCore {
    param(
        [string]$Text,
        [Parameter(Mandatory)] [int]$Width
    )

    $plainText = [string](Remove-TuiAnsiCore -Text $Text)
    if ([string]::IsNullOrEmpty($plainText) -or ($Width -le 0)) {
        return [ordered]@{
            Prefix = ''
            Remainder = $plainText
            Width = 0
        }
    }

    $prefixWidth = 0
    $endIndex = 0

    while ($endIndex -lt $plainText.Length) {
        $codePoint = [char]::ConvertToUtf32($plainText, $endIndex)
        $elementWidth = Get-TuiCodePointDisplayWidthCore -Text $plainText -Index $endIndex
        if (($prefixWidth + $elementWidth) -gt $Width) {
            break
        }

        $prefixWidth += $elementWidth
        $endIndex += Get-TuiCodePointLengthCore -CodePoint $codePoint
    }

    $remainder = if ($endIndex -lt $plainText.Length) {
        $plainText.Substring($endIndex)
    }
    else {
        ''
    }

    return [ordered]@{
        Prefix = $plainText.Substring(0, $endIndex)
        Remainder = $remainder
        Width = $prefixWidth
    }
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
        $currentWidth = 0
        foreach ($word in ($paragraph -split '\s+')) {
            if ([string]::IsNullOrWhiteSpace($word)) {
                continue
            }

            $wordWidth = Get-TuiDisplayWidthCore -Text $word
            $candidateWidth = if ([string]::IsNullOrWhiteSpace($current)) { $wordWidth } else { $currentWidth + 1 + $wordWidth }
            if ($candidateWidth -le $Width) {
                $current = if ([string]::IsNullOrWhiteSpace($current)) { $word } else { "$current $word" }
                $currentWidth = $candidateWidth
                continue
            }

            if (-not [string]::IsNullOrWhiteSpace($current)) {
                $wrapped.Add($current)
                $current = ''
                $currentWidth = 0
            }

            if ($wordWidth -le $Width) {
                $current = $word
                $currentWidth = $wordWidth
                continue
            }

            $remaining = $word
            while ((Get-TuiDisplayWidthCore -Text $remaining) -gt $Width) {
                $chunk = Split-TuiTextByDisplayWidthCore -Text $remaining -Width $Width
                if ([string]::IsNullOrEmpty([string]$chunk.Prefix)) {
                    break
                }

                $wrapped.Add([string]$chunk.Prefix)
                $remaining = [string]$chunk.Remainder
            }

            $current = $remaining
            $currentWidth = Get-TuiDisplayWidthCore -Text $current
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
    if ((Get-TuiDisplayWidthCore -Text $plainText) -le $Width) {
        return $plainText
    }

    if ($Width -le 0) {
        return ''
    }

    if ($Width -le 3) {
        return [string](Split-TuiTextByDisplayWidthCore -Text $plainText -Width $Width).Prefix
    }

    $prefix = [string](Split-TuiTextByDisplayWidthCore -Text $plainText -Width ($Width - 3)).Prefix
    return ($prefix + '...')
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
