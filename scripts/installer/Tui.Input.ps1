function Get-TuiTestKeyQueueCore {
    if ($null -ne $script:TuiTestKeyQueue) {
        return ,$script:TuiTestKeyQueue
    }

    $script:TuiTestKeyQueue = New-Object System.Collections.Generic.Queue[object]
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS)) {
        $tokens = $env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS -split '\|\|'
        foreach ($token in $tokens) {
            $trimmedToken = $token.Trim()
            if ([string]::IsNullOrWhiteSpace($trimmedToken)) {
                continue
            }

            if ($trimmedToken -eq 'Tick') {
                $script:TuiTestKeyQueue.Enqueue([ordered]@{
                        IsTick = $true
                    })
                continue
            }

            if ($trimmedToken -like 'Text:*') {
                $textPayload = $trimmedToken.Substring(5)
                foreach ($character in $textPayload.ToCharArray()) {
                    $script:TuiTestKeyQueue.Enqueue([ordered]@{
                            Key = [ConsoleKey]::NoName
                            Character = [string]$character
                        })
                }
                continue
            }

            try {
                $parsedKey = [ConsoleKey][System.Enum]::Parse([ConsoleKey], $trimmedToken, $true)
            }
            catch {
                throw "Unsupported TUI test key: $trimmedToken"
            }

            $script:TuiTestKeyQueue.Enqueue([ordered]@{
                    Key = $parsedKey
                    Character = ''
                })
        }
    }

    return ,$script:TuiTestKeyQueue
}

function Read-TuiConsoleKeyWithTimeoutCore {
    param([int]$TimeoutMilliseconds)

    if ($TimeoutMilliseconds -le 0) {
        return $null
    }

    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            if ([Console]::KeyAvailable) {
                $read = [Console]::ReadKey($true)
                return [ordered]@{
                    Key = [ConsoleKey]$read.Key
                    Character = [string]$read.KeyChar
                }
            }
        }
        catch {
            return $null
        }

        Start-Sleep -Milliseconds 50
    }

    return $null
}

function Test-TuiSupportCore {
    if ($NonInteractive -or $OutputJson) {
        return $false
    }

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS)) {
        return $true
    }

    try {
        $null = $Host.UI.RawUI.WindowTitle
        return -not [Console]::IsInputRedirected
    }
    catch {
        return $false
    }
}

function Read-TuiKeyCore {
    param([int]$TimeoutMilliseconds)

    $testQueue = Get-TuiTestKeyQueueCore
    if ($testQueue.Count -gt 0) {
        $nextItem = $testQueue.Dequeue()
        if ($nextItem.IsTick) {
            return $null
        }

        return $nextItem
    }

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS)) {
        throw 'TUI test key queue exhausted before the installer exited. Add more WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS tokens to cover the full interaction.'
    }

    $timedRead = Read-TuiConsoleKeyWithTimeoutCore -TimeoutMilliseconds $TimeoutMilliseconds
    if ($null -ne $timedRead) {
        return $timedRead
    }
    if ($TimeoutMilliseconds -gt 0) {
        return $null
    }

    try {
        $keyInfo = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
        return [ordered]@{
            Key = [ConsoleKey]$keyInfo.VirtualKeyCode
            Character = [string]$keyInfo.Character
        }
    }
    catch {
        $read = [Console]::ReadKey($true)
        return [ordered]@{
            Key = [ConsoleKey]$read.Key
            Character = [string]$read.KeyChar
        }
    }
}
