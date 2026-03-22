function Get-TuiAccent {
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

function Get-TuiScreenTitle {
    param([Parameter(Mandatory)] $State)

    switch ([string]$State.CurrentScreen) {
        'HomeScreen' { return 'HomeScreen' }
        'InstallScreen' { return 'InstallScreen' }
        'UninstallScreen' { return 'UninstallScreen' }
        'ProgressScreen' { return 'ProgressScreen' }
        default { return [string]$State.CurrentScreen }
    }
}

function Build-TuiScreenLines {
    param([Parameter(Mandatory)] $State)

    $accent = Get-TuiAccent
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('')
    $lines.Add("$($accent.Text)WPF DevTools MCP Installer$($accent.Reset)")
    $lines.Add("$($accent.Dim)Keyboard-only full screen installer$($accent.Reset)")
    $lines.Add('')

    if (-not [string]::IsNullOrWhiteSpace([string]$State.UpdateBannerText)) {
        $lines.Add("$($accent.Banner) Update available $($accent.Reset) $($accent.Warn)$($State.UpdateBannerText)$($accent.Reset)")
        $lines.Add('')
    }

    $lines.Add("$($accent.Dim)Architecture$($accent.Reset): $($accent.Text)$([string]$State.SelectedArchitecture)$($accent.Reset)")
    $lines.Add("$($accent.Dim)Install location$($accent.Reset): $($accent.Text)$([string]$State.InstallRoot)$($accent.Reset)")
    if (-not [string]::IsNullOrWhiteSpace([string]$State.VersionHint)) {
        $lines.Add("$($accent.Dim)$([string]$State.VersionHint)$($accent.Reset)")
    }

    $lines.Add('')
    $lines.Add("$($accent.Accent)$((Get-TuiScreenTitle -State $State))$($accent.Reset)")
    $lines.Add('')

    if ($State.CurrentScreen -eq 'ProgressScreen') {
        $lines.Add("$($accent.Text)$([string]$State.StatusMessage)$($accent.Reset)")
    }
    else {
        $items = @(Get-TuiCurrentItems -State $State)
        $visibleItems = @(Get-TuiVisibleItems -State $State)
        for ($index = 0; $index -lt $visibleItems.Count; $index++) {
            $absoluteIndex = [int]$State.ScrollOffset + $index
            $item = $visibleItems[$index]
            $prefix = if ($absoluteIndex -eq [int]$State.SelectionIndex) { "$($accent.Primary) > " } else { '   ' }
            $suffix = if ($absoluteIndex -eq [int]$State.SelectionIndex) { $accent.Reset } else { '' }
            $lines.Add("$prefix$([string]$item.Label)$suffix")
            if ($absoluteIndex -eq [int]$State.SelectionIndex -and -not [string]::IsNullOrWhiteSpace([string]$item.Description)) {
                $lines.Add("   $($accent.Dim)$([string]$item.Description)$($accent.Reset)")
            }
        }

        if ($items.Count -gt [int]$State.VisibleWindowSize) {
            $lastIndex = [Math]::Min($items.Count, [int]$State.ScrollOffset + [int]$State.VisibleWindowSize)
            $lines.Add('')
            $lines.Add("$($accent.Dim)Showing $([int]$State.ScrollOffset + 1)-$lastIndex of $($items.Count)$($accent.Reset)")
        }
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$State.StatusMessage) -and $State.CurrentScreen -ne 'ProgressScreen') {
        $lines.Add('')
        $lines.Add("$($accent.Ok)$([string]$State.StatusMessage)$($accent.Reset)")
    }

    $lines.Add('')
    $lines.Add("$($accent.Dim)Enter select  Up/Down move  Left/Right architecture  Escape back  Backspace back$($accent.Reset)")
    $lines.Add("$($accent.Dim)Update All is available from the home screen.$($accent.Reset)")
    return ,$lines.ToArray()
}

function Render-TuiScreenCore {
    param(
        [Parameter(Mandatory)] $State,
        [switch]$AsString
    )

    $lines = @(Build-TuiScreenLines -State $State)
    if ($AsString) {
        return ($lines -join [Environment]::NewLine)
    }

    try { Clear-Host } catch {}
    foreach ($line in $lines) {
        Write-Host $line
    }
}
