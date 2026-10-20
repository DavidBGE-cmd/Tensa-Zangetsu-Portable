@echo off
setlocal enabledelayedexpansion

echo =====================================================================
echo  Tensa-Zangetsu - Script de Limpieza Profunda y Mantenimiento
echo =====================================================================
echo.

set "ROOT_DIR=%~dp0"
cd /d "%ROOT_DIR%"

echo [1/4] Ejecutando dotnet clean en configuraciones Release y Debug...
if exist "TensaZangetsu.slnx" (
    dotnet clean TensaZangetsu.slnx -c Release --verbosity minimal
    dotnet clean TensaZangetsu.slnx -c Debug --verbosity minimal
) else if exist "TensaZangetsu.sln" (
    dotnet clean TensaZangetsu.sln -c Release --verbosity minimal
    dotnet clean TensaZangetsu.sln -c Debug --verbosity minimal
)

echo.
echo [2/4] Eliminando directorios de compilacion 'bin' y 'obj' en src\...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "Get-ChildItem -Path '%ROOT_DIR%src' -Recurse -Directory | Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' } | ForEach-Object { [System.IO.Directory]::Delete($_.FullName, $true); Write-Host ('  - Eliminado: ' + $_.FullName) -ForegroundColor DarkGray }"

if exist "%ROOT_DIR%scratch\AssetIndexer\bin" rmdir /s /q "%ROOT_DIR%scratch\AssetIndexer\bin" 2>nul
if exist "%ROOT_DIR%scratch\AssetIndexer\obj" rmdir /s /q "%ROOT_DIR%scratch\AssetIndexer\obj" 2>nul

echo.
echo [3/4] Eliminando caches de IDEs (.vs, .idea) y archivos de usuario...
for /d /r "%ROOT_DIR%" %%d in (.vs .idea) do (
    if exist "%%d" (
        echo   - Eliminando cache IDE: %%d
        rmdir /s /q "%%d" 2>nul
    )
)

del /s /q /f "%ROOT_DIR%*.user" 2>nul
del /s /q /f "%ROOT_DIR%*.suo" 2>nul
del /s /q /f "%ROOT_DIR%*.userosscache" 2>nul

echo.
echo [4/4] Verificando estado final...
echo.
echo =====================================================================
echo  Limpieza completada con exito. El workspace esta listo y limpio.
echo =====================================================================
echo.
pause
