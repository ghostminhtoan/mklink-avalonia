@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ====================================================================
echo   MkLink-GMTPC - Build ^& Publish Standalone Windows Portable (.NET 8.0)
echo   Target: publish\MkLink-GMTPC.exe (0 Error, 0 Warning)
echo ====================================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-Release.ps1" -SkipLaunch

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [LOI] Qua trinh build that bai voi ma loi %ERRORLEVEL%!
    exit /b %ERRORLEVEL%
)

echo.
echo [HOAN TAT] Build va Publish thanh cong vao thu muc publish\!
