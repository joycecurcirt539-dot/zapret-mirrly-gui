@echo off
chcp 65001 > nul
title Сборка Zapret Mirrly GUI

echo ======================================================================
echo   Сборка проекта Zapret Mirrly GUI (.NET 10 / WinUI 3)
echo ======================================================================
echo.

cd /d "%~dp0\.."

echo [*] Сборка исполняемого файла (Release, Single-File)...
dotnet publish ZapretMirrlyGUI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:NuGetAudit=false -o release
if %errorlevel% neq 0 (
    echo.
    echo [-] Ошибка при сборке проекта!
    pause
    exit /b %errorlevel%
)

echo.
echo [OK] Сборка успешно завершена! Файл создан: release\ZapretMirrlyGUI.exe
echo.
pause
