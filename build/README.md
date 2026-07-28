# Building a release

This folder holds everything that turns the sources into the files you attach to a GitHub
release.

| File | What it does |
|---|---|
| `build-release.ps1` | Builds it all: tests, application, settings window, archive, installer, checksums |
| `installer.iss` | Inno Setup 6 installer script (invoked by the script above) |

## What you need installed

| Tool | Why | Required |
|---|---|---|
| [.NET 8 SDK](https://dotnet.microsoft.com/download) | Building the application | yes |
| [Node.js 18+](https://nodejs.org/) and [Rust](https://rustup.rs/) | The Tauri settings window | yes, unless you pass `-SkipSettings` |
| [Inno Setup 6.3+](https://jrsoftware.org/isdl.php) | The installer | no: without it you still get the archive |

## Running it

```powershell
pwsh build/build-release.ps1
```

The version comes from `<Version>` in `Directory.Build.props`, so a new release only needs
that bumped (along with `src/reshot-tauri/src-tauri/tauri.conf.json`, `package.json` and
`Cargo.toml`, so the settings window does not fall behind).

Useful switches:

```powershell
pwsh build/build-release.ps1 -Version 1.0.1     # override the version
pwsh build/build-release.ps1 -SkipSettings      # do not rebuild Tauri, which is slow
```

## What comes out

Everything lands in `dist/`, which is gitignored:

```
dist/
├── reshot/                                  staging layout
│   ├── reshot.exe                           the application (self-contained)
│   ├── reshot-tauri.exe                     the settings window
│   ├── LICENSE
│   └── README.md
├── reshot-1.0.0-win-x64-portable.zip        portable build
├── reshot-1.0.0-setup.exe                   installer
└── SHA256SUMS.txt                           checksums
```

## Things worth knowing

- **Both executables must sit side by side.** The application looks for `reshot-tauri.exe`
  next to itself first (`App.ResolveSettingsExe`); without it the "Settings…" entry opens
  nothing.
- **The settings window can only be built through `tauri build`.** A plain `cargo build`
  produces a dev binary that loads its UI from `localhost:1420` and shows a blank page
  without the dev server running.
- **A running application locks its own executable.** The script closes `reshot.exe` and
  `reshot-tauri.exe` before building.
- **Tests run first.** If they fail, nothing is packaged.
- The installer is **per user** and needs no administrator rights: reshot's autostart entry
  lives in `HKCU` anyway, and an elevated install would only mismatch permissions.
