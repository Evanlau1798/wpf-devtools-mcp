@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "SCRIPT_DIR=%~dp0"
set "INSTALL_SCRIPT=%SCRIPT_DIR%bin\install.ps1"
set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"

if not exist "%POWERSHELL_EXE%" (
    set "POWERSHELL_EXE=powershell.exe"
)

if not exist "%INSTALL_SCRIPT%" (
    echo bin\install.ps1 was not found next to run.bat.
    exit /b 1
)

set "INSTALL_ARG_COUNT=0"

:collect_install_args
if "%~1"=="" goto launch_install
set /a INSTALL_ARG_COUNT+=1
set "INSTALL_ARG_%INSTALL_ARG_COUNT%=%~1"
shift
goto collect_install_args

:launch_install
if /I "%WPFDEVTOOLS_FORCE_ELEVATION%"=="1" goto elevate

if /I not "%WPFDEVTOOLS_SKIP_ELEVATION%"=="1" (
    net session >nul 2>&1
    if not "%ERRORLEVEL%"=="0" (
        goto elevate
    )
)

"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$installerArgs = for($i = 1; $i -le [int]$env:INSTALL_ARG_COUNT; $i++){ [Environment]::GetEnvironmentVariable('INSTALL_ARG_' + $i) }; & $env:INSTALL_SCRIPT @installerArgs"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo Installation failed with exit code %EXIT_CODE%.
)

exit /b %EXIT_CODE%

:elevate
set "ELEVATED_COMMAND=""%POWERSHELL_EXE%"" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ""$installerArgs = for($i = 1; $i -le [int]$env:INSTALL_ARG_COUNT; $i++){ [Environment]::GetEnvironmentVariable('INSTALL_ARG_' + $i) }; & $env:INSTALL_SCRIPT @installerArgs"""
"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$process = Start-Process -Verb RunAs -FilePath $env:ComSpec -ArgumentList '/c', $env:ELEVATED_COMMAND -Wait -PassThru; exit $process.ExitCode"
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
    echo Elevation failed with exit code %EXIT_CODE%.
)
exit /b %EXIT_CODE%
