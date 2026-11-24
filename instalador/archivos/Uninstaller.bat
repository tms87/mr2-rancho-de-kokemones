@echo off
rem Desinstalador / Uninstaller: se ejecuta desde una copia en TEMP para poder borrar esta carpeta.
copy /y "%~dp0desinstalar.ps1" "%TEMP%\rancho-kokemones-desinstalar.ps1" >nul
start "MR2 Rancho de Kokemones" powershell -NoProfile -ExecutionPolicy Bypass -File "%TEMP%\rancho-kokemones-desinstalar.ps1" -Instalacion "%~dp0."
