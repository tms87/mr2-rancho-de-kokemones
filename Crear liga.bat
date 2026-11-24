@echo off
title Rancho de Kokemones: crear una liga
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0herramientas\crear-liga.ps1" %*
echo.
pause
