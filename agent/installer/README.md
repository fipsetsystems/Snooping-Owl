# SnoopingOwl Installer (WiX v7)

Produces a professional enterprise installer for the Windows agent:

- **SnoopingOwl.msi** — the product package (Windows Installer)
- **SnoopingOwlSetup.exe** — Burn bootstrapper with the full wizard
  (Welcome → License → Install → Progress → Completion)

Built on **Windows only** (WiX requires Windows APIs; not supported on
Linux/Mac).

## What the installer does

- Installs `agent.exe` to `%ProgramFiles%\SnoopingOwl\`
- Installs a default `config.json` to `%ProgramData%\SnoopingOwl\`
  (`NeverOverwrite` — user config survives upgrades)
- Registers the **SnoopingOwl** Windows service:
  - `LocalSystem`, auto start, starts immediately after install
  - Failure recovery: restart up to 3 times (5 s apart), reset after 1 day
  - Stopped on uninstall/upgrade, removed on uninstall
- Per-machine install: requires administrator (UAC elevation)
- Standard uninstall via Programs & Features or `msiexec /x`

## Build

Prerequisites on the Windows build machine:

```bat
dotnet tool install --global wix
```

Then copy the release `agent.exe` into this directory next to
`config.json` and run:

```bat
build.cmd
```

The script accepts the WiX v7 OSMF EULA (`-acceptEula wix7`; free under
the $10k/yr revenue threshold) and adds the required WiX extensions.

Note: CI (`win64-release.yml`) builds the plain win64 `agent.exe` and does
not produce an MSI; run this locally on Windows when enterprise packaging
is required.

## Deploy

```bat
SnoopingOwlSetup.exe                 :: interactive (wizard)
SnoopingOwlSetup.exe -quiet -norestart :: silent (enterprise/Intune/GPO)
msiexec /i SnoopingOwl.msi /qn /norestart :: MSI-only silent install
msiexec /x SnoopingOwl.msi          :: uninstall
```

Upgrades: running the new `SnoopingOwlSetup.exe` over an older version
performs an MSI major upgrade — service stopped, files replaced, service
restarted.

## Files

| File | Purpose |
|---|---|
| `product.wxs` | MSI package: files, service install/config, upgrade logic, MSI UI |
| `bundle.wxs` | Burn bundle: wizard UI, license page, chains the MSI |
| `license.rtf` | **Placeholder license text — replace with real terms before release** |
| `config.json` | Default configuration shipped to `%ProgramData%\SnoopingOwl\` |
| `build.cmd` | Build script (MSI + bundle) |

## Not yet included

- Authenticode code signing (sign `SnoopingOwlSetup.exe` + `agent.exe` with
  your certificate before distribution; add `-sw`/`-s` options to `wix build`
  or sign post-build)
- Product icon / installer artwork (wire via `IconSourceFile` in
  `bundle.wxs` and `<Icon>` in `product.wxs`)
- Enrollment/configuration page (deferred to the protocol phase)