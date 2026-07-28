# reshot roadmap

**Status as of 2026-07-28: phases 0 to 6 are done and version 1.0.0 is released.**

The project was built in phases, each one ending with a program you could actually use.
This file is the record of that, plus what is left.

## Shipped

| Phase | Scope | Where it lives |
|---|---|---|
| 0 | Skeleton: tray, global hotkey, single instance, settings, logging | `Reshot.App`, `Reshot.Core` |
| 1 | Capture, overlay, rectangular selection, copy and save | `Reshot.Capture`, `Overlay/` |
| 2 | Selection shapes and the tool toolbar with flyouts | `Overlay/SelectionShape.cs` |
| 3 | Drawing: brush, shapes, text, eyedropper, undo | `Reshot.Core/Tools`, `History` |
| 4 | Erasers and effects | `Reshot.Core/Document` |
| 6 | Video recording, and audio recording beyond the original plan | `Reshot.Recording` |

### Built beyond the original plan

- **Text recognition (OCR)** with a selectable text layer over the frozen frame, and an
  AUTO mode that merges the Russian and English engines line by line
- **Radial quick menu** on holding the hotkey
- **Separate audio tracks** recorded independently, so the tracks are chosen after the
  recording stops rather than mixed live
- **Per-application audio** through Windows process loopback
- **Soft erasers** with a Photoshop-style radial falloff
- **Half-Life 2 / Source styling** across the overlay, the tray menu and the settings
- **Settings window rebuilt on Tauri** instead of WPF
- **Per-tool settings**: every tool keeps its own size, opacity and colour
- **Installer and portable build**, produced by one script

## Not done

- **Automatic updates.** Velopack was planned in ARCHITECTURE §10 and the `update.auto`
  key exists in the settings and in the UI, but nothing is wired to it. Turning it on
  changes no behaviour.
- **Code signing.** Without a certificate SmartScreen warns on first launch. This needs
  money rather than code.
- **Tiled undo.** History snapshots the whole affected region instead of 256x256 tiles,
  which costs more memory on very large strokes.

## Ideas parked for later

These are deliberately not commitments, just the shortlist that keeps coming up:

- Scrolling capture for long pages
- Pinning a capture on top of other windows as a floating panel
- A live mirror of a screen region, reusing the recording stream
- Multitrack audio, one track per application, kept separate in the final file
- A rewind buffer so the last N seconds can be saved after the fact

Anything here would need to survive the project's founding constraint first: **zero cost
while idle**. A rewind buffer, for instance, directly contradicts it.
