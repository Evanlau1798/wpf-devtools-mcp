@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "INSTALL_SCRIPT=%SCRIPT_DIR%bin\install.ps1"

if not exist "%INSTALL_SCRIPT%" (
    echo bin\install.ps1 was not found next to run.bat.
    exit /b 1
)

if /I not "%WPFDEVTOOLS_SKIP_ELEVATION%"=="1" (
    net session >nul 2>&1
    if not "%ERRORLEVEL%"=="0" (
        powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -Verb RunAs -FilePath 'powershell.exe' -ArgumentList '-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""%INSTALL_SCRIPT%"" %*'"
        set "EXIT_CODE=%ERRORLEVEL%"
        if not "%EXIT_CODE%"=="0" (
            echo Elevation failed with exit code %EXIT_CODE%.
        )
        exit /b %EXIT_CODE%
    )
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%INSTALL_SCRIPT%" %*
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo Installation failed with exit code %EXIT_CODE%.
)

exit /b %EXIT_CODE%
