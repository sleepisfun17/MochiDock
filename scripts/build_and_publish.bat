@echo off
setlocal
title MochiDock - Build and Publish
echo ============================================================
echo   MochiDock - Release build and publish
echo ============================================================
echo Progress and build output will appear below.
echo This may request Administrator access and stop the Publish copy.
echo MochiDock will remain closed after publishing.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1"
set "publish_exit_code=%ERRORLEVEL%"
echo.
if "%publish_exit_code%"=="0" (
    echo SUCCESS: Build, publish, asset checks and shortcut update completed.
) else (
    echo FAILED: Publisher exited with code %publish_exit_code%. See the output and log above.
)
echo.
if /I "%~1"=="/nopause" goto finish
pause
:finish
exit /b %publish_exit_code%
