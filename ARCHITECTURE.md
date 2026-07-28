# reshot architecture

## 1. Stack: C# / .NET 8

**Decision: C# rather than Rust.** The project is written together with an AI assistant by
an author without deep programming experience, and C# wins on every criterion that matters
for that situation:

- Compiler and runtime errors are readable and easy to feed back into the assistant. In
  Rust, fighting the borrow checker as a beginner stretches every phase severalfold.
- There is a huge amount of open source to learn from. **ShareX** (C#) is effectively a
  reference implementation of half of reshot.
- First-class access to the Windows API: Windows.Graphics.Capture, WASAPI, Media
  Foundation, the registry, the tray. All of it is either in the BCL or one NuGet away.
- Performance is more than sufficient. The frame is frozen, so editing is operations on a
  single bitmap rather than a GPU render every frame.

The tradeoff is accepted honestly: roughly 40 to 60 MB of RAM during an active editing
session and 25 to 30 MB in the background. That is normal for a utility, and the "under
30 MB idle" goal is reachable because WPF is not loaded until the first call (see §7).

### Components

| Layer | Technology | Why |
|---|---|---|
| Runtime | .NET 8 (self-contained, win-x64) | One installer, no runtime prerequisite |
| Overlay and UI | WPF (a borderless topmost window per stage) | Mature, flexible, easy to style dark |
| Editor canvas | **SkiaSharp** (`SKElement`) | Brushes, alpha, blur, pixelation, arbitrary paths and text, all in one library |
| Screen capture | **Windows.Graphics.Capture** (CsWinRT) | Modern API: captures games and protected windows, fast, free of GDI artefacts |
| Global hotkey | `RegisterHotKey` (user32, P/Invoke) | Zero background cost: the message arrives in the message loop |
| Tray | `NotifyIcon` (WinForms interop) | Standard |
| Settings | `System.Text.Json` writing `%AppData%\reshot\settings.json` | Shared with the settings window |
| Settings window | **Tauri 2** (Rust + TypeScript), a separate process | Styling the Source-like dialog in HTML and CSS beats fighting WPF for it |
| Video | Media Foundation `SinkWriter` (hardware H.264: NVENC, AMF, QSV) plus WASAPI through NAudio | No bundled ffmpeg, which saves about 80 MB |
| Text recognition | `Windows.Media.Ocr` | Offline, no models to ship, already part of the OS |

### Key NuGet packages

```
SkiaSharp
SkiaSharp.Views.WPF
Vortice.Direct3D11        (capture)
Vortice.MediaFoundation   (recording)
NAudio                    (audio)
```

## 2. Solution layout

```
reshot/
├── src/
│   ├── Reshot.App/            WPF: entry point, tray, hotkeys, overlay, OCR, export
│   │   ├── Overlay/           session window, toolbar, tool panels
│   │   ├── Ocr/               recognition and the selectable text layer
│   │   ├── Radial/            hold-to-open quick menu
│   │   ├── Recording/         recording HUD, audio track prompt
│   │   └── Tray/
│   ├── Reshot.Core/           no UI dependencies: document model, tools, history
│   │   ├── Document/
│   │   ├── Tools/
│   │   ├── History/
│   │   └── Export/
│   ├── Reshot.Capture/        Windows.Graphics.Capture wrapper
│   ├── Reshot.Recording/      Media Foundation plus WASAPI
│   └── reshot-tauri/          settings window (Tauri 2)
├── build/                     release scripts and the installer
└── tests/Reshot.Core.Tests/
```

Rule: **Reshot.Core knows nothing about WPF.** Every tool and the document model are
testable without windows.

## 3. Document model

An editing session is a `CaptureDocument`:

```
CaptureDocument
├── base frame            the frozen capture of every monitor (immutable, held by the app)
├── EffectsLayer          blur and pixelation over the untouched base
├── PaintLayer            brush strokes plus rasterised shapes and text
└── AbsoluteMask          coverage of the Absolute Eraser, punched out on export
```

Why it is split this way:

- **The Filter Eraser must return the original pixels**, so effects cannot be burnt into
  the base. They need their own layer over an untouched original.
- **The normal Eraser only removes user drawing**, so PaintLayer is separate from the base.
- **The Absolute Eraser erases everything down to transparency**, which is an alpha mask
  applied to the final composite.

Shapes and text are **baked into PaintLayer on commit**, so the eraser treats them exactly
like brush strokes, pixel by pixel. An earlier design kept a movable vector layer; it was
dropped because "erase part of an arrow" is worth more than "move an arrow afterwards".

Export composition: base, then effects, then paint, cropped to the union of the selection
paths, with everything outside those paths transparent.

## 4. Erasers: the strength model

An eraser stroke is a stamp with a radial alpha gradient:

```
alpha(r) = 1.0                    at the centre, always 100%
alpha(r) = falloff(r, hardness)   towards the rim
```

- `hardness = 1.0` gives a step: the whole disc erases fully, a hard round eraser.
- `hardness = 0.0` fades from the centre to the rim, a soft patch.
- The centre erases fully at any setting.

Implementation: the stroke is rasterised into a greyscale **coverage bitmap** whose
luminance is the erase strength. Overlapping stamps along the stroke are combined with
`SKBlendMode.Lighten`, taking the maximum rather than accumulating, and the finished
coverage is applied to the layer once with `SKBlendMode.DstOut`. Stamping semi-transparent
discs directly would compound along the stroke and erase the middle of a soft line fully.

## 5. Undo and redo

Command pattern, 32 steps deep.

Raster operations (brush, eraser, effect) store a snapshot of the affected region before
and after, and undo restores it. The original design called for 256x256 tiles so a stroke
would only cost a few tiles; the shipped implementation snapshots the whole bounding
region instead, which is simpler and has been adequate in practice but costs more memory
on very large strokes.

## 6. Capture and multi-monitor

- On the hotkey, **every monitor is captured at once** (one `GraphicsCaptureItem` per
  display) and the frames are composed into a single bitmap in virtual-desktop
  coordinates. That is a hard requirement for "Ctrl+A then Ctrl+A selects all monitors":
  the selection can only grow if the pixels are already there.
- The overlay is one borderless window spanning the whole virtual desktop.
- Protected content: Windows.Graphics.Capture returns whatever the system allows. Windows
  with a DRM flag may come back black, which is a platform limitation.
- The yellow Windows capture border is disabled through `IsBorderRequired = false`, which
  needs the Windows 11 SDK projection and a borderless access request.

## 7. Zero background cost

- The background process is a message-only window plus `RegisterHotKey` plus the tray. No
  timers, no polling, no watchers.
- The WPF overlay is **not created at startup**. It is constructed on the first hotkey, so
  the first call is about 100 ms slower.
- Every capture resource (D3D device, frame pool) is released when the session closes.

## 8. Input routing

One input router per session, highest priority first:

1. Open panels and toolbar flyouts. A right-click on the toolbar never reaches the
   eyedropper.
2. Context modifiers for the tool (`Shift`, `Ctrl`).
3. The active tool.

Session states are a small state machine:

```
Idle → Capturing → Selecting → Editing → Exporting → Idle
Editing → Esc → Selecting → Esc → Idle
Editing → Recording → Idle
```

## 9. Video

- Live capture through the same Windows.Graphics.Capture frame pool, cropped to the
  selection and fed to a Media Foundation `SinkWriter` with hardware H.264.
- Audio: independent WASAPI streams (loopback for system sound, capture for the
  microphone, process loopback for individual applications). Each source is written to its
  **own raw PCM file** during the recording.
- On stop, the chosen tracks are mixed and muxed into the final MP4. The video is copied
  through without re-encoding, so choosing tracks costs no quality and little time. This is
  what makes the post-recording track picker honest: nothing is mixed until the user picks.
- The recording indicator is a separate small topmost window, and the corner brackets are a
  click-through window with a transparent background.
- Stop is the same global hotkey, which the router interprets as Stop while recording.

## 10. Distribution

- A portable ZIP and an Inno Setup installer, both produced by `build/build-release.ps1`.
  The installer is per user and needs no administrator rights.
- The application is published self-contained, so no .NET runtime is required.
- There is no code signing (open source, no budget), so SmartScreen will complain at first.
  The README explains it.
- **Automatic updates are not implemented.** Velopack was planned and the `update.auto`
  setting exists in the UI, but nothing is wired to it yet.
- License: MIT.
