@echo off
setlocal
chcp 65001 >nul
set "INSTALLER=%~dp0Install-Ajazz-Twitch.ps1"
if not exist "%INSTALLER%" (
  echo.
  echo Установщик запущен прямо из ZIP, поэтому Windows извлекла только Install.cmd.
  echo Распакуйте архив целиком и повторите запуск либо используйте Ajazz-Twitch-Setup.exe.
  echo.
  pause
  exit /b 2
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%INSTALLER%"
if errorlevel 1 (
  echo.
  echo Установка завершилась с ошибкой.
  pause
  exit /b 1
)
echo.
pause
