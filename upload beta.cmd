@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ====================================================================
echo   MkLink-GMTPC - Upload Release Beta qua Microsoft Edge (Direct-to-S3)
echo   Yeu cau: Toc do ^>= 10 Mbps (Sau 7s tu huy neu ^< 10 Mbps, vong lap vo han)
echo   Backup: Tu dong rename file cu thanh zbackup_* truoc khi upload file moi
echo ====================================================================
echo.

python "%~dp0upload_beta_browser.py" %*

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [LOI] Qua trinh upload that bai voi ma loi %ERRORLEVEL%!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [HOAN TAT] Da upload thanh cong len Release Beta!
