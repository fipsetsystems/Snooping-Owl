; SnoopingOwl Agent - Inno Setup Script
; Phase 1: Windows Service installation
; Configurable WSS URL - use Cloudflare Tunnel placeholder

[Setup]
AppName=SnoopingOwl Agent
AppVersion=1.0.0
DefaultDirName={pf}\SnoopingOwl
DisableProgramGroupPage=yes
DisableDirPage=yes
UninstallAppName=SnoopingOwl Agent
UninstallDisplayVersion=1.0.0
CreateDesktopShortcut=no
CreateStartMenuShortcut=no

; --- Service section ---
; Service name must match .NET ServiceName
[Service]
Filename: "{app}\SnoopingOwl.Agent.exe"; ServiceName: "SnoopingOwlAgent"; StatusMsg="Installing and starting SnoopingOwl Agent service..."

; --- Files section ---
[Files]
Source: "bin\Release\net8.0\win-x86\publish\SnoopingOwl.Agent.exe"; DestDir: "{app}"; Flags: ignoreversion

; --- Run section ---
[Run]

; --- Registry for backend URL (configurable, not hardcoded per rule 25) ---
[Registry]
Root: "HKLM\Software\SnoopingOwl"; Subkey: "BackendUrl"; ValueType: string; ValueData: "wss://YOUR_CF_TUNNEL_ID.trycloudflare.com/ws"; Flags: createkeyifdoesnotexist

; --- Uninstall handling ---
; InnoSetup [Service] section handles stopping/removing the service on uninstall automatically