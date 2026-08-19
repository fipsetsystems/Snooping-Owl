@echo off
REM Builds the SnoopingOwl MSI and the Burn setup bundle (WiX v7).
REM Requires: WiX v7 (dotnet tool install --global wix), .NET 8+ SDK.
REM Run from an elevated command prompt in this directory.
REM NOTE: WiX only supports Windows; the MSI must be built on Windows.

setlocal
cd /d "%~dp0"

REM --- prerequisites: EULA acceptance + extensions ----------------------------
wix -acceptEula wix7 extension add WixToolset.UI.wixext WixToolset.Util.wixext WixToolset.BootstrapperApplications.wixext >nul 2>&1

REM --- 1. MSI --------------------------------------------------------------------
echo Building SnoopingOwl.msi ...
wix -acceptEula wix7 build -arch x64 product.wxs -o SnoopingOwl.msi -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext
if errorlevel 1 goto :failed

REM --- 2. Setup bundle ------------------------------------------------------------
echo Building SnoopingOwlSetup.exe ...
wix -acceptEula wix7 build bundle.wxs -o SnoopingOwlSetup.exe -ext WixToolset.BootstrapperApplications.wixext
if errorlevel 1 goto :failed

echo.
echo Done: SnoopingOwl.msi and SnoopingOwlSetup.exe
echo Install interactively:   SnoopingOwlSetup.exe
echo Install silently:        SnoopingOwlSetup.exe -quiet -norestart
echo Uninstall:               msiexec /x SnoopingOwl.msi
exit /b 0

:failed
echo Build failed. See messages above.
exit /b 1