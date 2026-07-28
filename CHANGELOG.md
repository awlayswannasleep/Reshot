# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this
project adheres to [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-07-28

First public release.

### Capture

- Instant snapshot of every monitor in a single frame (Windows.Graphics.Capture)
- Shaped selections: rectangle, ellipse, triangle, lasso, polygon
- Multiple selections with `Ctrl` + drag, exported as their union
- `Ctrl+A` selects the primary monitor, pressing it again selects all of them
- Transparency outside a non-rectangular shape, clipboard included
- Export to PNG, JPG and WebP; copy, save and save as

### Annotation

- Brush, shapes (square, circle, triangle), lines and arrows, text
- Blur and pixelation, applied by brushing or as a rectangle with `Ctrl`
- Erasers: normal, absolute (down to transparency), and one for filters
- Soft erasing with a radial falloff: the centre always erases fully and the rim follows a
  setting, the way a Photoshop brush behaves
- Eyedropper, either by holding the right mouse button or from a panel button that stays
  armed until you click
- Full HSV picker, hex entry, 16 swatches
- Undo and redo, 32 steps deep
- Per-tool size, opacity and colour

### Text from an image

- Recognition through the built-in Windows engine: offline, no models to ship
- Recognised words are selected with the mouse straight over the frame, then copied
- AUTO mode merges the Russian and English engines line by line, which removes the Latin
  characters being swapped for Cyrillic look-alikes on mixed text
- Manual AUTO / RU / EN switching by right-clicking the tool

### Recording

- MP4 / H.264 with hardware encoding (NVENC, AMF, QSV), no ffmpeg
- Records a selection of any shape, not just a rectangle
- System audio and the microphone are captured separately, so the tracks are chosen after
  the recording stops; the choice can be made permanent with a "never ask again" checkbox
- Per-application sound through Windows Process Loopback
- Standalone audio recorder writing M4A
- Recording indicator with a timer and corner brackets

### Shell

- Radial menu on holding the hotkey: quick screen recording, quick audio recorder, settings
- Settings window built with Tauri 2, including a full keyboard shortcut reference
- Half-Life 2 / Source VGUI styling
- Tray icon, autostart, single instance, global hotkeys
- Tools switched with the digits `1` to `0`, in toolbar order

### Known limitations

- Monitors with different DPI scaling are not supported
- Per-application audio capture requires Windows 11
- Russian text recognition needs a Windows OCR language pack
- Automatic updates are not implemented: `update.auto` currently does nothing
- MP4 has no alpha channel, so the area outside a non-rectangular recording is black
  rather than transparent

[1.0.0]: https://github.com/reteren/reshot/releases/tag/v1.0.0
