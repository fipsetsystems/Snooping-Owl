; SnoopingOwl Agent - Inno Setup Script
; Phase 2: Windows Service installation + auto-update helper

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

; --- Files section ---
; AssemblyName is SnoopingOwl.Agent -> published exe is SnoopingOwl.Agent.exe
; Updater is the standalone update helper (swap exe + crash rollback)
[Files]
Source: "bin\Release\net8.0\win-x86\publish\SnoopingOwl.Agent.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net8.0\win-x86\publish\SnoopingOwl.Updater.exe"; DestDir: "{app}"; Flags: ignoreversion

; --- Run section: install and start the service ---
; sc.exe is the standard Windows service control tool.
; failure recovery: restart the service if it crashes (3 attempts, growing delay)
[Run]
Filename: "{sys}\sc.exe"; Parameters: "create SnoopingOwlAgent start= auto binPath= ""{app}\SnoopingOwl.Agent.exe"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure SnoopingOwlAgent reset= 86400 actions= restart/5000/restart/10000/restart/60000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start SnoopingOwlAgent"; Flags: runhidden

; --- UninstallRun section: stop and remove the service ---
[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop SnoopingOwlAgent"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete SnoopingOwlAgent"; Flags: runhidden

; --- Registry for backend URL (configurable, not hardcoded per rule 25) ---
[Registry]
Root: "HKLM\Software\SnoopingOwl"; Subkey: "BackendUrl"; ValueType: string; ValueData: "wss://YOUR_CF_TUNNEL_ID.trycloudflare.com/ws"; Flags: createkeyifdoesnotexist
Root: "HKLM\Software\SnoopingOwl"; Subkey: "GitHubRepo"; ValueType: string; ValueData: "fipsetsystems/Snooping-Owl"; Flags: createkeyifdoesnotexist
Root: "HKLM\Software\SnoopingOwl"; Subkey: "UpdateIntervalMin"; ValueType: dword; ValueData: "10"; Flags: createkeyifdoesnotexist