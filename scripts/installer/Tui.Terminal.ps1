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

function Get-TuiViewportCore {
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

function Write-TuiFrameCore {
    param(
        [Parameter(Mandatory)] [string[]]$Lines,
        [Parameter(Mandatory)] $Viewport
    )

    $frameText = $Lines -join [Environment]::NewLine
    $isRedirected = $false
    try {
        $isRedirected = [Console]::IsOutputRedirected
    }
    catch {
        $isRedirected = $false
    }

    if ($isRedirected -or (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR))) {
        foreach ($line in $Lines) {
            Write-Host $line
        }

        return
    }

    if ([bool]$Viewport.UseAnsi) {
        try {
            [Console]::Write("$([char]27)[H")
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

    try {
        $Host.UI.RawUI.CursorPosition = New-Object System.Management.Automation.Host.Coordinates 0, 0
        [Console]::Write($frameText)
        return
    }
    catch {
    }

    try { Clear-Host } catch {}
    foreach ($line in $Lines) {
        Write-Host $line
    }
}
