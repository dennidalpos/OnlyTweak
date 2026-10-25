@echo off
setlocal
cd /d "%~dp0"
dotnet publish OnlyTweakWindows.csproj -c Release -r win-x64 --self-contained true -o dist --nologo
if errorlevel 1 exit /b 1
pwsh -NoProfile -File scripts\write-build-evidence.ps1 -Mode Receipt
exit /b %ERRORLEVEL%
