function Write-InstallerUtf8NoBomFile {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [AllowEmptyString()] [string]$Content
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $utf8Encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, [string]$Content, $utf8Encoding)
}
