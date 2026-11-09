@echo off
rem Copyright (c) Files Community
rem SPDX-License-Identifier: MPL-2.0
rem
rem Trusts the internal signing certificate on this machine.
rem
rem Internal builds are signed with a self-signed certificate. Until that
rem certificate is trusted, the MSIX fails to install with 0x800B010A and the
rem portable launcher cannot register its identity package, which is what
rem restores the IME candidate window. Both symptoms have the same cause and
rem need this step once per machine.
rem
rem Usage: copy the release .cer next to this script and run it. The script
rem re-launches itself through UAC and imports the certificate into
rem LocalMachine\TrustedPeople.

setlocal EnableExtensions

for %%F in ("%~dp0*.cer") do set "CERTFILE=%%~fF"

whoami /groups | findstr /c:"S-1-16-12288" /c:"S-1-16-16384" >nul 2>&1
if not errorlevel 1 goto elevated

rem Elevate through VBScript. mshta would also work, but Microsoft is retiring it and its
rem JavaScript argument treats backslashes as escapes: "D:\files" becomes "D:<FF>iles"
rem and "\x" is a syntax error, which silently broke elevation for paths like
rem D:\projects\files. VBScript string literals need no escaping.
set "ELEVATE=%TEMP%\files-trust-elevate-%RANDOM%.vbs"

echo This script needs administrator rights.
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
if not defined CERTFILE goto missing
if not exist "%CERTFILE%" goto missing

echo Certificate:
echo   %CERTFILE%
echo.

certutil -addstore -f "TrustedPeople" "%CERTFILE%"
if not "%errorlevel%"=="0" goto failed

echo.
echo Done. The certificate is now trusted for this machine.
echo   1. MSIX: double-click the .msix to install it.
echo   2. Portable: start Files.exe again. The launcher registers the identity
echo      package on the next start, which restores the IME candidate window.
echo.
pause
exit /b 0

:missing
echo error: no .cer file next to this script.
echo Copy the certificate that ships with the release beside %~nx0 and run again.
echo.
pause
exit /b 1

:failed
echo.
echo error: certutil could not add the certificate.
echo Read the certutil output above for the reason.
pause
exit /b 1
