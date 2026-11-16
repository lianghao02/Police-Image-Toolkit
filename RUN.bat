@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run.ps1" %*
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Police-Image-Toolkit 啟動失敗，錯誤碼：%ERRORLEVEL%
    pause
)
endlocal
