@echo off
setlocal
title MochiDock - Build Installer
echo ============================================================
echo   MochiDock - self-contained publish + installer
echo ============================================================
echo The app is never launched and no Administrator prompt is needed.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" -Installer
set "exit_code=%ERRORLEVEL%"
if not "%exit_code%"=="0" (
    echo.
    echo FAILED: installer-mode publish exited with code %exit_code%.
    goto finish
)
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
rem winget installs Inno Setup per user when not elevated.
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC (
    echo.
    echo Publish succeeded: %~dp0..\Publish-Installer
    echo Inno Setup 6 was not found, so the installer was NOT compiled.
    echo Install it from https://jrsoftware.org/isinfo.php and run this script again.
    set "exit_code=2"
    goto finish
)
echo.
echo Compiling installer\MochiDock.iss with "%ISCC%"...
"%ISCC%" "%~dp0..\installer\MochiDock.iss"
set "exit_code=%ERRORLEVEL%"
if "%exit_code%"=="0" (
    echo SUCCESS: see %~dp0..\installer\Output
) else (
    echo FAILED: ISCC exited with code %exit_code%.
)
:finish
echo.
if /I "%~1"=="/nopause" exit /b %exit_code%
pause
exit /b %exit_code%
