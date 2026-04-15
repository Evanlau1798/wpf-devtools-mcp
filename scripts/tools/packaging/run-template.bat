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

set "INSTALL_ARGS_FILE=%TEMP%\wpf-devtools-install-args-%RANDOM%%RANDOM%.txt"
if exist "%INSTALL_ARGS_FILE%" del /f /q "%INSTALL_ARGS_FILE%" >nul 2>&1

:collect_install_args
if "%~1"=="" goto launch_install
set "INSTALL_ARG_VALUE=%~1"
"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "Add-Content -LiteralPath $env:INSTALL_ARGS_FILE -Value $env:INSTALL_ARG_VALUE -Encoding UTF8"
if not "%ERRORLEVEL%"=="0" (
    echo Failed to prepare installer arguments.
    exit /b %ERRORLEVEL%
)
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

"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$installerArgs = if (Test-Path -LiteralPath $env:INSTALL_ARGS_FILE) { Get-Content -LiteralPath $env:INSTALL_ARGS_FILE } else { @() }; try { & $env:INSTALL_SCRIPT @installerArgs } finally { if (Test-Path -LiteralPath $env:INSTALL_ARGS_FILE) { Remove-Item -LiteralPath $env:INSTALL_ARGS_FILE -Force -ErrorAction SilentlyContinue } }"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo Installation failed with exit code %EXIT_CODE%.
)

exit /b %EXIT_CODE%

:elevate
set "ELEVATED_COMMAND=set ""INSTALL_SCRIPT=%INSTALL_SCRIPT%"" && set ""INSTALL_ARGS_FILE=%INSTALL_ARGS_FILE%"" && ""%POWERSHELL_EXE%"" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ""$installerArgs = if (Test-Path -LiteralPath $env:INSTALL_ARGS_FILE) { Get-Content -LiteralPath $env:INSTALL_ARGS_FILE } else { @() }; try { & $env:INSTALL_SCRIPT @installerArgs } finally { if (Test-Path -LiteralPath $env:INSTALL_ARGS_FILE) { Remove-Item -LiteralPath $env:INSTALL_ARGS_FILE -Force -ErrorAction SilentlyContinue } }"""
"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$process = Start-Process -Verb RunAs -FilePath $env:ComSpec -ArgumentList '/c', $env:ELEVATED_COMMAND -Wait -PassThru; exit $process.ExitCode"
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
    echo Elevation failed with exit code %EXIT_CODE%.
)
exit /b %EXIT_CODE%
