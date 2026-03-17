@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "INSTALL_SCRIPT=%SCRIPT_DIR%install.ps1"

if not exist "%INSTALL_SCRIPT%" (
    echo install.ps1 was not found next to install.bat.
    exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%INSTALL_SCRIPT%" %*
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo Installation failed with exit code %EXIT_CODE%.
)

exit /b %EXIT_CODE%
