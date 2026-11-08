@echo off
rem Builds "Claude Switcher.exe" with the C# compiler that ships with Windows (.NET Framework 4).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe at %CSC%
  exit /b 1
)
"%CSC%" /nologo /codepage:65001 /platform:x64 /target:winexe /optimize "/out:Claude Switcher.exe" ClaudeSwitcher.cs
if errorlevel 1 exit /b 1
echo Built "Claude Switcher.exe"
