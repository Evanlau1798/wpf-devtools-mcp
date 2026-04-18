function Test-TuiBootstrapAnsiSupport {
    if ($env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI -eq '1') {
        return $false
    }

    if ($env:WPFDEVTOOLS_INSTALLER_TEST_FORCE_ANSI -eq '1') {
        return $true
    }

    try {
        if ([Console]::IsOutputRedirected) {
            return $false
        }
    }
    catch {
        return $false
    }

    if (-not [string]::IsNullOrWhiteSpace($env:WT_SESSION)) {
        return $true
    }

    if (-not [string]::IsNullOrWhiteSpace($env:TERM_PROGRAM)) {
        return $true
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ConEmuANSI) -and $env:ConEmuANSI -eq 'ON') {
        return $true
    }

    return ($PSVersionTable.PSVersion.Major -ge 7)
}

function Get-TuiBootstrapViewport {
    $width = 0
    $height = 0

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH)) {
        [void][int]::TryParse($env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH, [ref]$width)
    }

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT)) {
        [void][int]::TryParse($env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT, [ref]$height)
    }

    if ($width -le 0 -or $height -le 0) {
        try {
            if ($width -le 0) {
                $width = [Console]::WindowWidth
            }

            if ($height -le 0) {
                $height = [Console]::WindowHeight
            }
        }
        catch {
        }
    }

    if ($width -le 0) {
        $width = 100
    }

    if ($height -le 0) {
        $height = 32
    }

    return [ordered]@{
        Width = [Math]::Max(20, [int]$width)
        Height = [Math]::Max(12, [int]$height)
        UseAnsi = [bool](Test-TuiBootstrapAnsiSupport)
    }
}

function Get-TuiBootstrapBorderGlyphs {
    if ($env:WPFDEVTOOLS_INSTALLER_TEST_ASCII_BORDER -eq '1') {
        return [ordered]@{
            Horizontal = '-'
            Vertical = '|'
            TopLeft = '+'
            TopRight = '+'
            BottomLeft = '+'
            BottomRight = '+'
        }
    }

    return [ordered]@{
        Horizontal = [string][char]0x2500
        Vertical = [string][char]0x2502
        TopLeft = [string][char]0x250C
        TopRight = [string][char]0x2510
        BottomLeft = [string][char]0x2514
        BottomRight = [string][char]0x2518
    }
}

function Get-TuiBootstrapRuleLine {
    param(
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] $Glyphs
    )

    return ($Glyphs.Horizontal * [Math]::Max(0, $Width))
}

function Pad-TuiBootstrapLine {
    param(
        [AllowEmptyString()] [string]$Text,
        [Parameter(Mandatory)] [int]$Width
    )

    $value = [string]$Text
    if ($value.Length -gt $Width) {
        return $value.Substring(0, $Width)
    }

    return $value.PadRight($Width)
}

function New-TuiBootstrapCenteredLine {
    param(
        [AllowEmptyString()] [string]$Text,
        [Parameter(Mandatory)] [int]$Width
    )

    $value = [string]$Text
    if ([string]::IsNullOrWhiteSpace($value)) {
        return (' ' * $Width)
    }

    if ($value.Length -ge $Width) {
        return $value.Substring(0, $Width)
    }

    $leftPadding = [Math]::Floor(([double]($Width - $value.Length)) / 2)
    return ((' ' * [int]$leftPadding) + $value).PadRight($Width)
}

function ConvertTo-TuiBootstrapWrappedLines {
    param(
        [AllowEmptyString()] [string]$Text,
        [Parameter(Mandatory)] [int]$Width
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $remaining = [string]$Text
    if ([string]::IsNullOrWhiteSpace($remaining)) {
        $lines.Add('')
        return @($lines)
    }

    while ($remaining.Length -gt $Width) {
        $lines.Add($remaining.Substring(0, $Width))
        $remaining = $remaining.Substring($Width)
    }

    if ($remaining.Length -gt 0) {
        $lines.Add($remaining)
    }

    return @($lines)
}

function Sync-TuiBootstrapConsoleBuffer {
    param([Parameter(Mandatory)] $Viewport)

    $targetWidth = [Math]::Max(20, [int]$Viewport.Width)
    $targetHeight = [Math]::Max(12, [int]$Viewport.Height)

    try {
        $rawUi = $Host.UI.RawUI
        $windowSize = $rawUi.WindowSize
        $targetWidth = [Math]::Max([int]$windowSize.Width, $targetWidth)
        $targetHeight = [Math]::Max([int]$windowSize.Height, $targetHeight)
        $bufferSize = $rawUi.BufferSize
        if (($bufferSize.Width -ne $targetWidth) -or ($bufferSize.Height -ne $targetHeight)) {
            $rawUi.BufferSize = New-Object Management.Automation.Host.Size($targetWidth, $targetHeight)
        }

        return $true
    }
    catch {
    }

    try {
        if (([Console]::BufferWidth -ne $targetWidth) -or ([Console]::BufferHeight -ne $targetHeight)) {
            [Console]::SetBufferSize($targetWidth, $targetHeight)
        }

        return $true
    }
    catch {
    }

    return $false
}

function Enter-TuiBootstrapTerminalSession {
    param([Parameter(Mandatory)] $Viewport)

    $session = [ordered]@{
        UsedAlternateScreen = $false
        HidCursor = $false
        ManagedBuffer = $false
        SavedBufferWidth = 0
        SavedBufferHeight = 0
        SavedCursorVisible = $null
    }

    $isRedirected = $false
    try {
        $isRedirected = [Console]::IsOutputRedirected
    }
    catch {
        $isRedirected = $false
    }

    if ($isRedirected -or (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR))) {
        return $session
    }

    try {
        $rawUi = $Host.UI.RawUI
        $bufferSize = $rawUi.BufferSize
        $session.SavedBufferWidth = [int]$bufferSize.Width
        $session.SavedBufferHeight = [int]$bufferSize.Height
        $session.ManagedBuffer = $true
    }
    catch {
        try {
            $session.SavedBufferWidth = [int][Console]::BufferWidth
            $session.SavedBufferHeight = [int][Console]::BufferHeight
            $session.ManagedBuffer = $true
        }
        catch {
            $session.ManagedBuffer = $false
        }
    }

    try {
        $session.SavedCursorVisible = [bool][Console]::CursorVisible
    }
    catch {
        $session.SavedCursorVisible = $null
    }

    [void](Sync-TuiBootstrapConsoleBuffer -Viewport $Viewport)

    if (-not [bool]$Viewport.UseAnsi) {
        try {
            [Console]::CursorVisible = $false
            $session.HidCursor = $true
        }
        catch {
        }

        return $session
    }

    $escape = [string][char]27
    try {
        [Console]::Write("${escape}[?1049h${escape}[?25l${escape}[2J${escape}[H")
        $session.UsedAlternateScreen = $true
        $session.HidCursor = $true
    }
    catch {
    }

    try {
        [Console]::CursorVisible = $false
        $session.HidCursor = $true
    }
    catch {
    }

    return $session
}

function Exit-TuiBootstrapTerminalSession {
    param($Session)

    if ($null -eq $Session) {
        return
    }

    $escape = [string][char]27
    if ([bool]$Session.UsedAlternateScreen) {
        try {
            [Console]::Write("${escape}[?25h${escape}[?1049l")
        }
        catch {
        }
    }

    if ([bool]$Session.ManagedBuffer -and ([int]$Session.SavedBufferWidth -gt 0) -and ([int]$Session.SavedBufferHeight -gt 0)) {
        try {
            $rawUi = $Host.UI.RawUI
            $rawUi.BufferSize = New-Object Management.Automation.Host.Size([int]$Session.SavedBufferWidth, [int]$Session.SavedBufferHeight)
        }
        catch {
            try {
                [Console]::SetBufferSize([int]$Session.SavedBufferWidth, [int]$Session.SavedBufferHeight)
            }
            catch {
            }
        }
    }

    if ($null -ne $Session.SavedCursorVisible) {
        try {
            [Console]::CursorVisible = [bool]$Session.SavedCursorVisible
            return
        }
        catch {
        }
    }

    if ([bool]$Session.HidCursor) {
        try {
            [Console]::Write("${escape}[?25h")
        }
        catch {
            try {
                [Console]::CursorVisible = $true
            }
            catch {
            }
        }
    }
}

function Close-TuiBootstrapScreen {
    if ($null -ne $script:TuiBootstrapTerminalSession) {
        Exit-TuiBootstrapTerminalSession -Session $script:TuiBootstrapTerminalSession
    }

    $script:TuiBootstrapTerminalSession = $null
    $script:LastTuiBootstrapMessage = $null
}

function Write-TuiBootstrapScreen {
    param([Parameter(Mandatory)] [string]$Message)

    if ($script:LastTuiBootstrapMessage -eq $Message) {
        return $null
    }

    $viewport = Get-TuiBootstrapViewport
    if ($null -eq $script:TuiBootstrapTerminalSession) {
        $script:TuiBootstrapTerminalSession = Enter-TuiBootstrapTerminalSession -Viewport $viewport
    }

    $glyphs = Get-TuiBootstrapBorderGlyphs
    $innerWidth = [Math]::Max(18, [int]$viewport.Width - 2)
    $innerHeight = [Math]::Max(10, [int]$viewport.Height - 2)
    $title = 'WPF DevTools MCP'
    $subtitle = 'Model Context Protocol Server'
    $eyebrow = 'Installation Manager'
    $caption = '[_] [ ] [X]'
    $titleRow = (' ' + $title).PadRight([Math]::Max(1, $innerWidth - $caption.Length)) + $caption
    $rule = Get-TuiBootstrapRuleLine -Width $innerWidth -Glyphs $glyphs
    $progressTitle = switch -Wildcard ($Message) {
        'Preparing installer UI...*' { 'Preparing installer UI' }
        'Loading installer runtime...*' { 'Loading installer runtime' }
        'Loading installer data...*' { 'Loading installer data' }
        'Checking latest release*' { 'Checking latest release' }
        default { 'Loading installer runtime' }
    }

    $heroLines = @(
        New-TuiBootstrapCenteredLine -Text $title -Width $innerWidth
        New-TuiBootstrapCenteredLine -Text $subtitle -Width $innerWidth
        New-TuiBootstrapCenteredLine -Text $eyebrow -Width $innerWidth
        (' ' * $innerWidth)
        New-TuiBootstrapCenteredLine -Text $progressTitle -Width $innerWidth
    )

    $statusLines = @(ConvertTo-TuiBootstrapWrappedLines -Text "[Status] $Message" -Width $innerWidth) | ForEach-Object {
        Pad-TuiBootstrapLine -Text ([string]$_) -Width $innerWidth
    }

    $footerLines = @(
        (Pad-TuiBootstrapLine -Text $rule -Width $innerWidth)
        @($statusLines)
    )

    $bodyLineCount = [Math]::Max(0, $innerHeight - 2 - $footerLines.Count)
    $topPadding = [Math]::Max(0, [Math]::Floor(([double]($bodyLineCount - $heroLines.Count)) / 2))
    $bodyLines = New-Object System.Collections.Generic.List[string]
    for ($index = 0; $index -lt $topPadding; $index++) {
        $bodyLines.Add((' ' * $innerWidth))
    }
    foreach ($heroLine in $heroLines) {
        if ($bodyLines.Count -ge $bodyLineCount) {
            break
        }

        $bodyLines.Add((Pad-TuiBootstrapLine -Text ([string]$heroLine) -Width $innerWidth))
    }
    while ($bodyLines.Count -lt $bodyLineCount) {
        $bodyLines.Add((' ' * $innerWidth))
    }

    $innerLines = @(
        (Pad-TuiBootstrapLine -Text $titleRow -Width $innerWidth)
        (Pad-TuiBootstrapLine -Text $rule -Width $innerWidth)
        @($bodyLines)
        @($footerLines)
    )

    $frameLines = New-Object System.Collections.Generic.List[string]
    $frameLines.Add($glyphs.TopLeft + $rule + $glyphs.TopRight)
    foreach ($line in $innerLines) {
        $frameLines.Add($glyphs.Vertical + (Pad-TuiBootstrapLine -Text ([string]$line) -Width $innerWidth) + $glyphs.Vertical)
    }
    $frameLines.Add($glyphs.BottomLeft + $rule + $glyphs.BottomRight)

    $frameText = $frameLines -join [Environment]::NewLine
    $isRedirected = $false
    try {
        $isRedirected = [Console]::IsOutputRedirected
    }
    catch {
        $isRedirected = $false
    }

    if ($isRedirected -or (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR))) {
        foreach ($line in $frameLines) {
            Write-Host $line
        }
    }
    else {
        [void](Sync-TuiBootstrapConsoleBuffer -Viewport $viewport)
        if ([bool]$viewport.UseAnsi) {
            $escape = [string][char]27
            try {
                [Console]::Write("${escape}[H")
                [Console]::Write($frameText)
            }
            catch {
                [Console]::SetCursorPosition(0, 0)
                [Console]::Write($frameText)
            }
        }
        else {
            try {
                [Console]::SetCursorPosition(0, 0)
                [Console]::Write($frameText)
            }
            catch {
                try {
                    Clear-Host
                }
                catch {
                }

                foreach ($line in $frameLines) {
                    Write-Host $line
                }
            }
        }
    }

    $script:LastTuiBootstrapMessage = $Message
    return $frameText
}
