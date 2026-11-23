@echo off
rem Builds and runs the automatic tests against pretend Cisco devices (no real switch needed).
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the C# compiler. Make sure .NET Framework 4.8 is installed.
  if "%~1"=="" pause
  exit /b 1
)

if not exist test\bin mkdir test\bin
"%CSC%" /nologo /target:exe /optimize+ /out:test\bin\CiscoResetTests.exe ^
  /r:System.dll /r:System.Core.dll ^
  test\*.cs src\Telnet.cs src\Resetter.cs src\Device.cs src\AppSettings.cs
if errorlevel 1 (
  echo.
  echo TEST BUILD FAILED - see the messages above.
  if "%~1"=="" pause
  exit /b 1
)

test\bin\CiscoResetTests.exe
set RESULT=%errorlevel%
if "%~1"=="" pause
exit /b %RESULT%
