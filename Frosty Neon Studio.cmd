@echo off
rem Frosty Neon Studio: the avatar toolkit in one window. Double-click to start.
start "" powershell -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%~dp0tools\app\FrostyNeonStudio.ps1"
