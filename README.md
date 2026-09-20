# Compositor for Windows

Windows port of [robbietilton/Compositor](https://github.com/robbietilton/Compositor) ("The Photoshop alternative for Mac"), built from scratch.

**Upstream license:** MIT, preserved in `LICENSE-MAC-UPSTREAM`.
**This port license:** MIT (see `LICENSE`).

## Why from scratch

Upstream is macOS-only: the entire UI is AppKit/SwiftUI, the image engine is CoreImage/CoreGraphics/Metal, and building it requires macOS 26 + Xcode 26. None of that compiles or runs on Windows.

This repo rebuilds Compositor as a cross-platform .NET 8 application:

| Layer | Upstream (Mac) | This port (Windows) |
|---|---|---|
| UI | AppKit + SwiftUI | Avalonia 11 |
| Image engine | CoreImage / CoreGraphics | Pure C# raster (SkiaSharp planned) |
| Fast kernels | C + Accelerate / vImage | Planned: C via P/Invoke |
| ML features | Vision framework | Planned: ONNX Runtime |
| Document format | `.comp` spec v6 | Zip `.comp` v1 = upstream v6 subset, additive |

Goal: same document model, same editing semantics, compatible file format. Not a UI clone.

## Current status

- **Document model**: layers (bottom-to-top), UUIDs, transform block, 9 upstream blend modes, opacity, visibility, lock.
- **Project files**: single-file zip `.comp` (`manifest.json` + `images/<uuid>.png`), atomic replace, full validation (30k px/side, 100 M px, 10k layers, 4 MiB manifest, unsafe-entry rejection). Byte-identical round-trip is test-enforced.
- **Raster engine**: RGBA8 surfaces, soft round brush (spacing + stroke-opacity cap, per upstream `brush-performance.md`), undo/redo with exact pixel restoration.
- **UI shell (Avalonia)**: editor window with canvas (checkerboard + layer rects) and layers panel (add, delete, reorder, visibility, selection).
- **CI**: GitHub Actions on every push; 52 tests.

Not usable as a daily editor yet: brush UI wiring, file dialogs, zoom/pan, and painting tools are next. See `docs/ROADMAP.md`.

## Building and running

Requires the .NET 8 SDK.

```bash
# build everything
dotnet build Compositor.Windows.sln

# run all tests (document model, project I/O, brush, PNG, headless UI)
dotnet test Compositor.Windows.sln

# headless smoke (console): document round-trip check
dotnet run --project src/Compositor.App

# open the editor window
dotnet run --project src/Compositor.App -- --ui
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
  RESEARCH.md             Upstream spec extraction + our design decisions
  upstream-project-format.md  Upstream .comp format spec (v1-6)
```

## Credits

- Robbie Tilton for the original Compositor (macOS): https://github.com/robbietilton/Compositor
- Upstream docs (`project-format.md`, `brush-performance.md`) served as the specification. No source code reused.
- Revan67's independent port plan (`Compositor-Windows`) validated the zip-format and Avalonia decisions; read as reference only.
