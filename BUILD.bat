@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\qa.ps1"
if errorlevel 1 goto failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1"
if errorlevel 1 goto failed
echo Police-Image-Toolkit 驗證與打包完成（輸出於 dist\）。
pause
exit /b 0
:failed
set buildError=%ERRORLEVEL%
echo Police-Image-Toolkit 驗證或打包失敗，錯誤碼：%buildError%
pause
exit /b %buildError%
