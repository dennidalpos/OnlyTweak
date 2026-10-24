@echo off
setlocal
cd /d "%~dp0"
dotnet build tests\OnlyTweakWindows.Tests.csproj -c Release --nologo
if errorlevel 1 exit /b 1
artifacts\tests\Release\net10.0-windows\OnlyTweakWindows.Tests.exe %*
exit /b %ERRORLEVEL%
