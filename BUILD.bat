@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& '%~dp0scripts\qa.ps1'; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; & '%~dp0scripts\build.ps1'"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Police-Image-Toolkit 驗證或打包失敗，錯誤碼：%ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)
echo.
echo Police-Image-Toolkit 驗證與打包完成（輸出於 dist\）。
pause
endlocal
