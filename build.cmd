@echo off
setlocal
cd /d "%~dp0"
dotnet publish OnlyTweakWindows.csproj -c Release -r win-x64 --self-contained true -o dist --nologo
exit /b %ERRORLEVEL%
