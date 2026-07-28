# Contributing

## What you need

- [.NET 8 SDK](https://dotnet.microsoft.com/download), Windows 10 2004+ x64
- [Node.js 18+](https://nodejs.org/) and [Rust](https://rustup.rs/), but only if you touch
  the settings window (`src/reshot-tauri`)

```powershell
dotnet build reshot.sln -c Debug
dotnet test
dotnet run --project src/Reshot.App
```

## How the project is laid out

| Project | Purpose |
|---|---|
| `src/Reshot.Core` | The core, **with no UI**: document model, history, settings, hotkeys |
| `src/Reshot.Capture` | The only place Windows.Graphics.Capture lives |
| `src/Reshot.Recording` | MP4 and M4A through Media Foundation and NAudio |
| `src/Reshot.App` | WPF shell: tray, overlay, OCR, export |
| `src/reshot-tauri` | Settings window: Tauri 2, Rust, TypeScript |

Boundaries worth keeping:

1. **`Reshot.Core` knows nothing about WPF.** That is what lets the document model, the
   history and the settings be unit tested without any windows.
2. **Windows.Graphics.Capture lives only in `Reshot.Capture`.** The rest of the code sees
   the `IScreenCaptureService` interface.
3. **`settings.json` is shared** between the C# app and the settings window. Write it by
   merging, otherwise one side wipes keys belonging to the other.

## Style

- Comments explain **why**, they do not restate the code
- Documentation, comments and the user interface are all in English
- New logic in `Reshot.Core` comes with tests (`tests/Reshot.Core.Tests`)

## Verifying changes

A running `reshot.exe` holds a lock on its own file, so close it before rebuilding:

```powershell
taskkill /IM reshot.exe /F
```

The overlay is a fullscreen window and launching it for every check is disruptive.
Whatever can be verified without it should be: the core logic is covered by unit tests,
and the HUD layout can be rendered off screen by building a frame with
`VirtualLeft` and `VirtualTop` set to -30000.

## Building a release

See [build/README.md](build/README.md).
