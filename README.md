# Compositor for Windows

Windows port of [robbietilton/Compositor](https://github.com/robbietilton/Compositor) ("The Photoshop alternative for Mac"), built from scratch.

**Upstream license:** MIT. Attribution and license preserved in `LICENSE-MIT`.
**This port license:** MIT (see `LICENSE`).

## Why from scratch

Upstream is macOS-only: the entire UI is AppKit/SwiftUI, the image engine is CoreImage/CoreGraphics/Metal, and build requires macOS 26 + Xcode 26. None of that compiles or runs on Windows.

This repo rebuilds Compositor as a cross-platform .NET 8 application:

| Layer | Upstream (Mac) | This port (Windows) |
|---|---|---|
| UI | AppKit + SwiftUI | Avalonia 11 |
| Image engine | CoreImage / CoreGraphics | SkiaSharp |
| Fast kernels | C + Accelerate / vImage | C via P/Invoke (portable `stdint`/`math` only) |
| ML features | Vision framework | ONNX Runtime |
| Document format | `docs/project-format.md` v6 (zip) | Same spec, compatible reader |

Goal: same document model, same editing semantics, same file format. Not a UI clone.

## Repository layout

```
src/
  Compositor.Core/        Document model, layers, blend modes, undo, project I/O (headless, no UI deps)
  Compositor.Kernels/     Native C pixel kernels + P/Invoke bindings
  Compositor.App/         Avalonia UI (shell, canvas, layers panel, tools)
tests/
  Compositor.Core.Tests/  Headless unit tests for document model & project I/O
docs/
  ROADMAP.md              Phased plan with acceptance criteria per phase
  port-notes.md           Decisions, upstream references, deviations
```

## Status

Phase 0 (scaffold). See `docs/ROADMAP.md` for the current phase and what "done" means at each step.

## Building

Requires .NET 8 SDK.

```bash
dotnet build Compositor.Windows.sln
dotnet test tests/Compositor.Core.Tests
```

## Credits

- Robbie Tilton for the original Compositor (macOS): https://github.com/robbietilton/Compositor
- Reference for document format and editing semantics. No source code reused.
