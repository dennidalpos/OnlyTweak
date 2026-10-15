@echo off
setlocal
cd /d "%~dp0"
set "OTW_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%OTW_CSC%" set "OTW_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%OTW_CSC%" (
  echo ERRORE: compilatore .NET Framework di Windows non trovato.
  exit /b 1
)
if not exist "dist" mkdir "dist"
if errorlevel 1 exit /b 1
"%OTW_CSC%" /nologo /langversion:5 /codepage:65001 /warn:4 /warnaserror+ /optimize+ /target:winexe /platform:x64 /out:dist\OnlyTweakWindows.exe /win32manifest:app.manifest /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.ServiceProcess.dll src\*.cs
if errorlevel 1 exit /b 1
echo Creato: %CD%\dist\OnlyTweakWindows.exe
exit /b 0
