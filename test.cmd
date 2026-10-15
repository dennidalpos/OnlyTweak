@echo off
setlocal
cd /d "%~dp0"
set "OTW_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%OTW_CSC%" set "OTW_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%OTW_CSC%" exit /b 1
if not exist "artifacts" mkdir "artifacts"
if errorlevel 1 exit /b 1
"%OTW_CSC%" /nologo /langversion:5 /codepage:65001 /warn:4 /warnaserror+ /target:exe /platform:x64 /main:OnlyTweakWindows.Tests /out:artifacts\OnlyTweakWindows.Tests.exe /win32manifest:tests\test.manifest /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.ServiceProcess.dll src\*.cs tests\*.cs
if errorlevel 1 exit /b 1
artifacts\OnlyTweakWindows.Tests.exe %*
exit /b %ERRORLEVEL%
