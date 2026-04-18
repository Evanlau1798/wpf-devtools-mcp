function Test-TuiAnsiSupportCore {
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

function Get-TuiViewportOverrideFromFileCore {
    $overridePath = [string]$env:WPFDEVTOOLS_INSTALLER_TEST_VIEWPORT_PATH
    if ([string]::IsNullOrWhiteSpace($overridePath) -or -not (Test-Path $overridePath)) {
        return $null
    }

    try {
        $rawValue = (Get-Content -Path $overridePath -Raw -ErrorAction Stop).Trim()
    }
    catch {
        return $null
    }

    $match = [regex]::Match($rawValue, '^\s*(\d+)\s*[x, ]\s*(\d+)\s*$')
    if (-not $match.Success) {
        return $null
    }

    return [ordered]@{
        Width = [int]$match.Groups[1].Value
        Height = [int]$match.Groups[2].Value
    }
}

function Get-TuiViewportCore {
    $width = 0
    $height = 0

    $fileViewport = Get-TuiViewportOverrideFromFileCore
    if ($null -ne $fileViewport) {
        $width = [int]$fileViewport.Width
        $height = [int]$fileViewport.Height
    }

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH)) {
        [void][int]::TryParse($env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH, [ref]$width)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT)) {
        [void][int]::TryParse($env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT, [ref]$height)
    }

    if ($width -le 0 -or $height -le 0) {
        try {
            $size = $Host.UI.RawUI.WindowSize
            if ($width -le 0) {
                $width = [int]$size.Width
            }
            if ($height -le 0) {
                $height = [int]$size.Height
            }
        }
        catch {
        }
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
        Width = [Math]::Max(20, $width)
        Height = [Math]::Max(12, $height)
        UseAnsi = [bool](Test-TuiAnsiSupportCore)
    }
}

function Get-TuiViewportCacheKeyCore {
    param([Parameter(Mandatory)] $Viewport)

    return "$([int]$Viewport.Width)x$([int]$Viewport.Height):$([int][bool]$Viewport.UseAnsi)"
}

function Sync-TuiViewportConsoleBufferCore {
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

function Enter-TuiTerminalSessionCore {
    param($Viewport)

    $bootstrapSession = $null
    try {
        $bootstrapSession = Get-Variable -Name WpfDevToolsInstallerBootstrapSession -Scope Global -ValueOnly -ErrorAction Stop
    }
    catch {
        $bootstrapSession = $null
    }

    if ($null -ne $bootstrapSession) {
        Remove-Variable -Name WpfDevToolsInstallerBootstrapSession -Scope Global -ErrorAction SilentlyContinue
        if ($null -eq $Viewport) {
            $Viewport = Get-TuiViewportCore
        }

        [void](Sync-TuiViewportConsoleBufferCore -Viewport $Viewport)
        return $bootstrapSession
    }

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

    if ($null -eq $Viewport) {
        $Viewport = Get-TuiViewportCore
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

    [void](Sync-TuiViewportConsoleBufferCore -Viewport $Viewport)

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

function Exit-TuiTerminalSessionCore {
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

function Get-TuiInputPollTimeoutCore {
    param([Parameter(Mandatory)] $State)

    $pollMilliseconds = 200
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TUI_POLL_MS)) {
        [void][int]::TryParse($env:WPFDEVTOOLS_INSTALLER_TUI_POLL_MS, [ref]$pollMilliseconds)
    }

    if ($null -ne $State.LatestVersionRefreshHandle) {
        return [Math]::Max(50, $pollMilliseconds)
    }

    return [Math]::Max(50, $pollMilliseconds)
}

function Get-TuiBorderGlyphsCore {
    if ($env:WPFDEVTOOLS_INSTALLER_TEST_ASCII_BORDER -eq '1') {
        return [ordered]@{
            Horizontal = '-'
            Vertical = '|'
            TopLeft = '+'
            TopRight = '+'
            BottomLeft = '+'
            BottomRight = '+'
            TeeLeft = '+'
            TeeRight = '+'
        }
    }

    return [ordered]@{
        Horizontal = [string][char]0x2500
        Vertical = [string][char]0x2502
        TopLeft = [string][char]0x250C
        TopRight = [string][char]0x2510
        BottomLeft = [string][char]0x2514
        BottomRight = [string][char]0x2518
        TeeLeft = [string][char]0x251C
        TeeRight = [string][char]0x2524
    }
}

function Get-TuiNormalizedFrameLinesCore {
    param(
        [Parameter(Mandatory)] [string[]]$Lines,
        [Parameter(Mandatory)] $Viewport
    )

    $targetHeight = [int]$Viewport.Height
    $targetWidth = [int]$Viewport.Width
    $normalized = New-Object System.Collections.Generic.List[string]

    foreach ($line in @($Lines)) {
        $normalized.Add((Pad-TuiLineCore -Text ([string]$line) -Width $targetWidth))
    }

    while ($normalized.Count -lt $targetHeight) {
        $normalized.Add((' ' * $targetWidth))
    }

    $script:TuiLastRenderedFrameHeight = $targetHeight
    $script:TuiLastRenderedFrameWidth = $targetWidth
    return @($normalized)
}

function Write-TuiFrameCore {
    param(
        [Parameter(Mandatory)] [string[]]$Lines,
        [Parameter(Mandatory)] $Viewport
    )

    $normalizedLines = @(Get-TuiNormalizedFrameLinesCore -Lines $Lines -Viewport $Viewport)
    $frameText = $normalizedLines -join [Environment]::NewLine
    $isRedirected = $false
    try {
        $isRedirected = [Console]::IsOutputRedirected
    }
    catch {
        $isRedirected = $false
    }

    if ($isRedirected -or (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR))) {
        foreach ($line in $normalizedLines) {
            Write-Host $line
        }

        return
    }

    [void](Sync-TuiViewportConsoleBufferCore -Viewport $Viewport)

    if ([bool]$Viewport.UseAnsi) {
        $escape = [string][char]27
        try {
            [Console]::Write("${escape}[H")
            [Console]::Write($frameText)
            return
        }
        catch {
        }
    }

    try {
        [Console]::SetCursorPosition(0, 0)
        [Console]::Write($frameText)
        return
    }
    catch {
    }

    try { Clear-Host } catch {}
    foreach ($line in $normalizedLines) {
        Write-Host $line
    }
}
