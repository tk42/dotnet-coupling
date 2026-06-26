@echo off
rem ダブルクリックで単体 exe を生成するランチャ。
rem (.ps1 はダブルクリックでは実行されないため、この .cmd 経由で呼び出す)
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Task Publish -Configuration Release
echo.
echo Exit code: %ERRORLEVEL%
pause
