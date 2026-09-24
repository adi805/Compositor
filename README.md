# Compositor for Windows

Windows port of [robbietilton/Compositor](https://github.com/robbietilton/Compositor) ("The Photoshop alternative for Mac"), built from scratch.

**Upstream license:** MIT, preserved in `LICENSE-MAC-UPSTREAM`.
**This port license:** MIT (see `LICENSE`).

## Why from scratch

Upstream is macOS-only: the entire UI is AppKit/SwiftUI, the image engine is CoreImage/CoreGraphics/Metal, and building it requires macOS 26 + Xcode 26. None of that compiles or runs on Windows.

This repo rebuilds Compositor as a cross-platform .NET 8 application:

| Layer | Upstream (Mac) | This port (Windows) |
|---|---|---|
| UI | AppKit + SwiftUI | Avalonia 11.3 |
| Image engine | CoreImage / CoreGraphics | Pure C# raster; SkiaSharp 2.88 is already in the tree via Avalonia.Skia and is the planned codec/filter backend (see `docs/TECH-STACK.md`) |
| Fast kernels | C + Accelerate / vImage | C# ports of upstream's C kernels, bit-exact |
| ML features | Vision framework | Planned: ONNX Runtime 1.30 |
| Document format | `.comp` spec v6 | Zip `.comp` v1 = upstream v6 subset, additive |

Pinned versions, why each one, and the verification commands: `docs/TECH-STACK.md`.

Goal: same document model, same editing semantics, compatible file format. Not a UI clone.

## Current status (v0.2 line + WS3-WS10)

**Working today:**

- **Document model**: layers (bottom-to-top), UUIDs, transform block, 13 blend modes (PDF-correct separable + non-separable), opacity, visibility, lock.
- **Project files**: single-file zip `.comp` (`manifest.json` + `images/<uuid>.png`), atomic replace, full validation (30k px/side, 100 M px, 10k layers, 4 MiB manifest, unsafe-entry rejection). Byte-identical round-trip is test-enforced.
- **Painting**: left-drag on canvas paints the active layer (soft round brush, spacing + stroke-opacity cap per upstream `brush-performance.md`). Click = dot. Undo/redo is per-stroke with exact pixel restoration.
- **Editing**: rectangular/ellipse/lasso selection with add-subtract-intersect and feather-free coverage masks, invert, copy/cut/paste-as-floating, magic wand; levels, curves, hue/saturation, exposure, gradient map, invert and grain as live-preview sheets; Gaussian blur, motion blur, add noise and lens correction as filter sheets; gradient, shape, blur, clone stamp and smudge tools; canvas resize, crop, image resize.
- **Layers panel**: add, delete, reorder, visibility, selection, groups, merge down, flip, typed scale/rotate, per-layer blend + opacity.
- **Files**: File menu with New (Ctrl+N), Open/Save `.comp` (Ctrl+O/S). Import (Ctrl+I) reads PNG, JPEG, GIF, BMP, ICO and WebP by content, multi-select, centred on the canvas. Drag-drop onto the canvas imports what you drop, at the drop point, including image data with no file behind it. Export flattened PNG (Ctrl+E) or JPEG (Ctrl+Shift+E) with a quality slider, a matte for transparency, and the document resolution written into pHYs / JFIF. Last JPEG quality is remembered. Pickers collect paths only; all I/O is in the testable view-model.
- **View**: wheel zoom anchored at the cursor (Ctrl-free), middle-drag pan, zoom % readout, Fit button (resets to fit).
- **CI**: GitHub Actions on every push; 422 tests (300 core + 122 app incl. headless UI).

**Known limits:**

- Layer transforms are inert for pixel layers on BOTH sides: the canvas draws a pixel layer into the full canvas rect (`CanvasView` calls `DrawImage(bitmap, canvasRect)`; `Scaled(...)` is only used for blank-layer placeholders) and `Flatten`/`CompositeStack` ignore the transform too. So the typed Scale%/Rotate fields change the model without changing anything you can see. Flip does look correct because v0.2 bakes the pixels, while upstream renders flips through the transform flag instead. Making rendering transform-driven is WS11, and it needs a format decision first: files saved by v0.1/v0.2 hold baked pixels AND the flip flag, so honoring the flag without a migration double-flips them.
- TIFF and HEIC import need a codec beyond the shipped Skia build; upstream reads both through ImageIO. Documented per row in `docs/PARITY.md`.
- Import is not an undo step yet: layer add/remove has no command type (upstream wraps a batch in one edit).
- Imported images are baked into a canvas-sized surface; upstream places them with a transform, so a partially-off-canvas drop clips instead of hanging off the edge.
- Selected layer appears on top in the panel (inverted-order issue deferred); no stylus pressure; no zoom-preserving undo/redo.
- Remaining upstream surfaces and tools are tracked item by item in `docs/PARITY.md`.

## Building and running

Requires the .NET 8 SDK.

```bash
# build everything
dotnet build Compositor.Windows.sln

# run all tests (document model, project I/O, brush, PNG, headless UI)
dotnet test Compositor.Windows.sln

# headless smoke (console): document round-trip check
dotnet run --project src/Compositor.App -- --smoke

# open the editor window
dotnet run --project src/Compositor.App
```

On Windows the same commands work; the app is a plain .NET desktop app (no MSIX, no Windows App SDK).

## Repository layout

```
src/
  Compositor.Core/        Document model, blend modes, brush, undo, PNG codec, project I/O (no UI deps)
  Compositor.Kernels/     Reserved for native pixel kernels
  Compositor.App/         Avalonia UI (window, canvas view, layers panel)
tests/
  Compositor.Core.Tests/  Headless tests: model, I/O, brush, codec
  Compositor.App.Tests/   View-model tests + headless Avalonia UI tests
docs/
  ROADMAP.md              Phased plan with acceptance criteria per phase
  PARITY.md               Feature-by-feature parity matrix vs upstream (the contract)
  TECH-STACK.md           Pinned versions, why, and how to verify them
  RESEARCH.md             Upstream spec extraction + our design decisions
  upstream-project-format.md  Upstream .comp format spec (v1-6)
```

## Credits

- Robbie Tilton for the original Compositor (macOS): https://github.com/robbietilton/Compositor
- Upstream docs (`project-format.md`, `brush-performance.md`) served as the specification. No source code reused.
- Revan67's independent port plan (`Compositor-Windows`) validated the zip-format and Avalonia decisions; read as reference only.
