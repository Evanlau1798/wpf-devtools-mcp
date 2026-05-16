function Invoke-RegistrationCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ClientName
    )

    $isElevated = Test-InstallerRunningElevated
    $resolvedCommandPath = Resolve-ExecutableCommandPath -Command $Command -AllowPathResolution:(-not $isElevated)
    if ([string]::IsNullOrWhiteSpace($resolvedCommandPath)) {
        if ($isElevated) {
            throw (Get-ElevatedCliCommandBlockMessage -Command $Command -ClientName $ClientName -OperationName 'registration')
        }

        throw "$Command is not installed. Cannot register $ClientName automatically."
    }

    & $resolvedCommandPath @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "$Command registration failed for $ClientName with exit code $LASTEXITCODE."
    }

    return [ordered]@{
        client = $ClientName
        mode = 'cli'
        target = $resolvedCommandPath
        backupPath = $null
        applied = $true
    }
}

function Resolve-ExecutableCommandPath {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [bool]$AllowPathResolution = $true
    )

    $trustedCommandPath = Resolve-TrustedCliCommandPath -Command $Command
    if (-not [string]::IsNullOrWhiteSpace($trustedCommandPath)) {
        return $trustedCommandPath
    }

    if (-not $AllowPathResolution) {
        return $null
    }

    $resolvedCommands = @(Get-Command $Command -All -CommandType Application,ExternalScript -ErrorAction SilentlyContinue)
    foreach ($resolvedCommand in $resolvedCommands) {
        $candidatePath = if (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Path)) {
            [string]$resolvedCommand.Path
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Source)) {
            [string]$resolvedCommand.Source
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Definition)) {
            [string]$resolvedCommand.Definition
        }
        else {
            $null
        }

        if (-not [string]::IsNullOrWhiteSpace($candidatePath)) {
            return $candidatePath
        }
    }

    return $null
}

function Invoke-OptionalRemovalCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ClientName
    )

    $isElevated = Test-InstallerRunningElevated
    $resolvedCommandPath = Resolve-ExecutableCommandPath -Command $Command -AllowPathResolution:(-not $isElevated)
    if ([string]::IsNullOrWhiteSpace($resolvedCommandPath)) {
        if ($isElevated) {
            throw (Get-ElevatedCliCommandBlockMessage -Command $Command -ClientName $ClientName -OperationName 'removal')
        }

        return [ordered]@{
            client = $ClientName
            mode = 'cli'
            target = $null
            backupPath = $null
            applied = $false
        }
    }

    & $resolvedCommandPath @Arguments | Out-Null
    $succeeded = ($LASTEXITCODE -eq 0)
    return [ordered]@{
        client = $ClientName
        mode = 'cli'
        target = $resolvedCommandPath
        backupPath = $null
        applied = $succeeded
    }
}

function Invoke-DocsHomepage {
    $uri = 'https://wpf-mcptools.evanlau1798.com'
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_OPEN_BROWSER_COMMAND)) {
        & $env:WPFDEVTOOLS_INSTALLER_OPEN_BROWSER_COMMAND $uri | Out-Null
        return
    }

    Start-Process $uri | Out-Null
}
