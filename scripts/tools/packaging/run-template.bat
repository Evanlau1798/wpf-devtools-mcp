@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "SCRIPT_DIR=%~dp0"
set "INSTALL_SCRIPT=%SCRIPT_DIR%bin\install.ps1"
REM ENCODER_POWERSHELL_EXE stays on a known-good Windows PowerShell path so
REM argument JSON/base64 preparation works even when execution is user-overridden.
set "ENCODER_POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
set "POWERSHELL_EXE="
set "INSTALL_ARG_COUNT=0"

if not exist "%ENCODER_POWERSHELL_EXE%" (
    set "ENCODER_POWERSHELL_EXE=powershell.exe"
)

REM POWERSHELL_EXE is the actual execution engine and may be overridden by
REM WPFDEVTOOLS_POWERSHELL_EXE for environments that need a custom host.
if not defined WPFDEVTOOLS_POWERSHELL_EXE goto use_default_powershell
call :validate_powershell_override
if errorlevel 1 exit /b 1
set "POWERSHELL_EXE=%WPFDEVTOOLS_POWERSHELL_EXE%"
goto powershell_selected

:use_default_powershell
set "POWERSHELL_EXE=%ENCODER_POWERSHELL_EXE%"

:powershell_selected

if not exist "%INSTALL_SCRIPT%" (
    echo bin\install.ps1 was not found next to run.bat.
    exit /b 1
)

:collect_install_args
if "%~1"=="" goto prepare_install_args
set "INSTALL_ARG_%INSTALL_ARG_COUNT%=%~1"
set /a INSTALL_ARG_COUNT+=1
shift
goto collect_install_args

:prepare_install_args
set "INSTALL_ARGS_B64="
for /f "usebackq delims=" %%I in (`%ENCODER_POWERSHELL_EXE% -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$installerArgs = @(); for ($i = 0; $i -lt [int]$env:INSTALL_ARG_COUNT; $i++) { $installerArgs += [Environment]::GetEnvironmentVariable('INSTALL_ARG_' + $i, 'Process') }; $argsJson = ConvertTo-Json -Compress -InputObject @($installerArgs); $argsBytes = [System.Text.Encoding]::UTF8.GetBytes($argsJson); [Convert]::ToBase64String($argsBytes)"`) do set "INSTALL_ARGS_B64=%%I"
if not defined INSTALL_ARGS_B64 (
    echo Failed to prepare installer arguments.
    exit /b 1
)

:launch_install
if /I "%WPFDEVTOOLS_FORCE_ELEVATION%"=="1" goto elevate

if /I not "%WPFDEVTOOLS_SKIP_ELEVATION%"=="1" (
    net session >nul 2>&1
    if not "%ERRORLEVEL%"=="0" (
        goto elevate
    )
)

"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$installerArgs = @(); if (-not [string]::IsNullOrWhiteSpace($env:INSTALL_ARGS_B64)) { $argsJson = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:INSTALL_ARGS_B64)); $decodedArgs = $argsJson | ConvertFrom-Json; $installerArgs = @($decodedArgs | ForEach-Object { [string]$_ }) }; & $env:INSTALL_SCRIPT @installerArgs"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo Installation failed with exit code %EXIT_CODE%.
)

exit /b %EXIT_CODE%

:elevate
set "ELEVATED_COMMAND=set ""INSTALL_SCRIPT=%INSTALL_SCRIPT%"" && set ""INSTALL_ARGS_B64=%INSTALL_ARGS_B64%"" && ""%POWERSHELL_EXE%"" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ""$installerArgs = @(); if (-not [string]::IsNullOrWhiteSpace($env:INSTALL_ARGS_B64)) { $argsJson = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:INSTALL_ARGS_B64)); $decodedArgs = $argsJson | ConvertFrom-Json; $installerArgs = @($decodedArgs | ForEach-Object { [string]$_ }) }; & $env:INSTALL_SCRIPT @installerArgs"""
"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$process = Start-Process -Verb RunAs -FilePath $env:ComSpec -ArgumentList '/c', $env:ELEVATED_COMMAND -Wait -PassThru; exit $process.ExitCode"
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
    echo Elevation failed with exit code %EXIT_CODE%.
)
exit /b %EXIT_CODE%

:validate_powershell_override
if not "%WPFDEVTOOLS_POWERSHELL_EXE%"=="%WPFDEVTOOLS_POWERSHELL_EXE:"=%" (
    echo WPFDEVTOOLS_POWERSHELL_EXE cannot contain quote characters.
    exit /b 1
)
if /I not "%WPFDEVTOOLS_POWERSHELL_EXE:~-4%"==".exe" (
    echo WPFDEVTOOLS_POWERSHELL_EXE must point to a .exe host.
    exit /b 1
)
if "%WPFDEVTOOLS_POWERSHELL_EXE:~0,2%"=="\\" (
    echo WPFDEVTOOLS_POWERSHELL_EXE must be a local drive path.
    exit /b 1
)
if not "%WPFDEVTOOLS_POWERSHELL_EXE:~1,2%"==":\" (
    echo WPFDEVTOOLS_POWERSHELL_EXE must be an absolute path.
    exit /b 1
)
for %%I in ("%WPFDEVTOOLS_POWERSHELL_EXE%") do set "POWERSHELL_EXE_NAME=%%~nxI"
if /I not "%POWERSHELL_EXE_NAME%"=="powershell.exe" if /I not "%POWERSHELL_EXE_NAME%"=="pwsh.exe" (
    echo WPFDEVTOOLS_POWERSHELL_EXE must point to powershell.exe or pwsh.exe.
    exit /b 1
)
if not exist "%WPFDEVTOOLS_POWERSHELL_EXE%" (
    echo WPFDEVTOOLS_POWERSHELL_EXE was not found: %WPFDEVTOOLS_POWERSHELL_EXE%
    exit /b 1
)
set "POWERSHELL_PATH_KIND_OK="
for /f "usebackq delims=" %%I in (`%ENCODER_POWERSHELL_EXE% -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$item = Get-Item -LiteralPath $env:WPFDEVTOOLS_POWERSHELL_EXE -ErrorAction Stop; if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) { 'OK' }"`) do set "POWERSHELL_PATH_KIND_OK=%%I"
if /I not "%POWERSHELL_PATH_KIND_OK%"=="OK" (
    echo WPFDEVTOOLS_POWERSHELL_EXE must not resolve through a reparse point.
    exit /b 1
)
set "POWERSHELL_SIGNER_OK="
for /f "usebackq delims=" %%I in (`%ENCODER_POWERSHELL_EXE% -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$sig = Get-AuthenticodeSignature -LiteralPath $env:WPFDEVTOOLS_POWERSHELL_EXE; if ($sig.Status -eq 'Valid' -and $null -ne $sig.SignerCertificate -and $sig.SignerCertificate.Subject -like '*O=Microsoft Corporation*') { 'OK' }"`) do set "POWERSHELL_SIGNER_OK=%%I"
if /I not "%POWERSHELL_SIGNER_OK%"=="OK" (
    echo WPFDEVTOOLS_POWERSHELL_EXE must be signed by Microsoft.
    exit /b 1
)
exit /b 0
