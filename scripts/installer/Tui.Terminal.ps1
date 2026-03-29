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
        Width = [Math]::Max(80, $width)
        Height = [Math]::Max(24, $height)
        UseAnsi = [bool](Test-TuiAnsiSupportCore)
    }
}
