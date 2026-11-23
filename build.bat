@echo off
rem Builds CiscoReset.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No Visual Studio or downloads needed. Just double-click this file.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the C# compiler. Make sure .NET Framework 4.8 is installed.
  if "%~1"=="" pause
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:CiscoReset.exe ^
  /win32manifest:src\app.manifest /win32icon:src\app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo BUILD FAILED - see the messages above.
  if "%~1"=="" pause
  exit /b 1
)

echo.
echo Built CiscoReset.exe successfully.
if "%~1"=="" pause
