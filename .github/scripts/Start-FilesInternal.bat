@echo off
rem Copyright (c) Files Community
rem SPDX-License-Identifier: MPL-2.0
rem
rem One entry point for an internal build:
rem   1. trusts the internal signing certificate on this machine (needed once),
rem   2. starts what sits next to this script:
rem        *.msix    -> Windows install wizard
rem        Files.exe -> portable launcher
rem
rem The certificate step is not optional housekeeping: without it the MSIX
rem fails with 0x800B010A and the portable build cannot register the identity
rem package that the IME candidate window depends on.

setlocal EnableExtensions EnableDelayedExpansion

for %%F in ("%~dp0*.cer") do set "CERTFILE=%%~fF"

rem The portable build ships an identity package next to its launcher. That file
rem is not an installer, so it must not be treated as the thing to start.
set "MSIXFILE="
for %%F in ("%~dp0*.msix") do (
    set "CANDIDATE=%%~fF"
    echo(!CANDIDATE! | findstr /i /c:"Portable.Identity" >nul || set "MSIXFILE=%%~fF"
)

set "LAUNCHER=%~dp0Files.exe"
if not exist "%LAUNCHER%" set "LAUNCHER="

whoami /groups | findstr /c:"S-1-16-12288" /c:"S-1-16-16384" >nul 2>&1
if not errorlevel 1 goto elevated

rem Elevate through VBScript. mshta would also work, but Microsoft is retiring it and
rem its JavaScript argument treats backslashes as escapes: "D:\files" becomes
rem "D:<FF>iles" and "\x" is a syntax error.
set "ELEVATE=%TEMP%\files-internal-elevate-%RANDOM%.vbs"

echo This script needs administrator rights to trust the certificate.
echo A UAC prompt will open. If it does not, right-click this script and
echo choose "Run as administrator".
echo.

> "%ELEVATE%" echo Set shell = CreateObject("Shell.Application")
>> "%ELEVATE%" echo shell.ShellExecute "%~f0", "", "", "runas", 1
cscript //nologo "%ELEVATE%" >nul 2>&1
del /f /q "%ELEVATE%" >nul 2>&1

echo.
echo Waiting for the elevated window. You can close this one.
pause
exit /b 0

:elevated
if not defined CERTFILE goto noCertificate
if not exist "%CERTFILE%" goto noCertificate

echo Certificate:
echo   %CERTFILE%
echo.
certutil -addstore -f "TrustedPeople" "%CERTFILE%"
if not "%errorlevel%"=="0" goto failed
echo.
echo The certificate is trusted on this machine.
goto launch

:noCertificate
echo warning: no .cer file next to this script.
echo Continuing without trusting a certificate; skip this only when it is already installed.
echo.

:launch
rem Start through explorer so the app runs with the desktop user's integrity level.
rem This script is elevated, and Files must not run as administrator.
if defined MSIXFILE goto launchMsix
if defined LAUNCHER goto launchPortable
echo Nothing to start beside this script. The certificate step is complete.
goto finish

:launchMsix
echo Starting the installer:
echo   %MSIXFILE%
explorer.exe "%MSIXFILE%"
goto finish

:launchPortable
echo Starting Files:
echo   %LAUNCHER%
explorer.exe "%LAUNCHER%"
goto finish

:finish
echo.
pause
exit /b 0

:failed
echo.
echo error: certutil could not add the certificate.
echo Read the certutil output above for the reason.
pause
exit /b 1
