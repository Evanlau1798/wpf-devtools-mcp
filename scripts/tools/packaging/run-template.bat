@echo off
setlocal

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

if /I "%WPFDEVTOOLS_FORCE_ELEVATION%"=="1" goto elevate

if /I not "%WPFDEVTOOLS_SKIP_ELEVATION%"=="1" (
    net session >nul 2>&1
    if not "%ERRORLEVEL%"=="0" (
        goto elevate
    )
)

"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%INSTALL_SCRIPT%" %*
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo Installation failed with exit code %EXIT_CODE%.
)

exit /b %EXIT_CODE%

:elevate
set "ELEVATED_COMMAND=""%POWERSHELL_EXE%"" -NoLogo -NoProfile -ExecutionPolicy Bypass -File ""%INSTALL_SCRIPT%"" %*"
"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$process = Start-Process -Verb RunAs -FilePath $env:ComSpec -ArgumentList '/c', $env:ELEVATED_COMMAND -Wait -PassThru; exit $process.ExitCode"
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
    echo Elevation failed with exit code %EXIT_CODE%.
)
exit /b %EXIT_CODE%
