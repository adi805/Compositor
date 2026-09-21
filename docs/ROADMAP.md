# Compositor for Windows - Roadmap

Phased plan. Each phase has explicit acceptance criteria; no phase is "done" until its tests pass.

## Phase 0 - Scaffold (current)

Goal: solution builds, test project runs green, repository layout established.

Deliverables:
- `Compositor.Windows.sln` with Core, Kernels, App, and Core.Tests projects
- `Directory.Build.props` for shared build settings
- README with upstream attribution and tech stack
- CI skeleton (GitHub Actions, linux runner, `dotnet build` + `dotnet test`)

Acceptance:
- `dotnet build Compositor.Windows.sln` exits 0
- `dotnet test tests/Compositor.Core.Tests` exits 0 with at least one passing test

## Phase 1 - Document model

Goal: headless representation of a Compositor document that can round-trip `.comp` files.

Deliverables (in `src/Compositor.Core`):
- `Document`, `Layer`, `LayerGroup` (raster, text, shape placeholder)
- Blend modes (normal, multiply, screen, overlay, etc.)
- Opacity, visibility, clipping masks
- Undo/redo stack (command pattern)
- Project save/load (zip-based, following upstream `docs/project-format.md` v6 spec)

Acceptance:
- Create document, add layers, save to stream, reload, assert equivalent (round-trip test)
- Undo a layer add, redo it, assert final state matches
- Blend mode enum covers the 9 modes upstream supports

## Phase 2 - Pixel engine

Goal: actual raster operations backing layers.

Deliverables (in `src/Compositor.Kernels` + `Core`):
- Sparse tile-based raster surface (matches upstream model)
- C pixel kernels: per-pixel operations, blend composition, color adjust
- P/Invoke bindings + .NET fallback (pure C#) for portability when native build unavailable
- Gaussian blur, levels, curves (via SkiaSharp where possible, custom kernels where not)

Acceptance:
- Blend two layers with each mode, assert against reference output
- Round-trip a document with raster data and verify pixel integrity

## Phase 3 - UI shell

Goal: usable editor window on Windows.

Deliverables (in `src/Compositor.App`):
- Avalonia window: menu bar, canvas viewport with zoom/pan, layers panel, tool strip
- Open/save `.comp`, import PNG/JPEG/WebP (via SkiaSharp), export flattened image
- Move tool, marquee selection, brush paint (basic round brush with hardness)
- Undo/redo wired to UI

Acceptance:
- App launches, opens a `.comp` file, renders layers correctly
- Paint stroke appears on canvas, undo removes it
- Save/reload produces visually identical result

## Phase 4 - Advanced tools

Deliverables:
- Selections: rectangle, ellipse, lasso, magic wand (flood fill based)
- Text layers
- Adjustment layers (levels, curves, hue/saturation)
- Liquify family: warp, push, twirl
- Content-aware fill placeholder (ONNX model optional)

Acceptance:
- Each tool has at least one integration test driving it through the document model
- Manual smoke checklist documented in `docs/port-notes.md`

## Phase 5 - Distribution

Deliverables:
- Self-contained single-file Windows build (x64)
- Installer (Inno Setup or MSI)
- GitHub Actions release workflow producing artifacts on tag push
- Optional: auto-update via GitHub Releases (upstream uses Sparkle on Mac)

Acceptance:
- Clean Windows 11 VM runs the installer and launches the app
- Release workflow produces a downloadable `.zip` and installer on tag push

## Non-goals (for this port)

- macOS build (upstream already covers it)
- Mobile/touch UI
- Cloud sync / collaboration
- Plugin API (out of scope until core is stable)

## Risks

- Native C kernels on Windows require a C compiler in CI (clang/gcc cross-target or MSVC); fallback to pure C# keeps phase 2-4 unblocked even without native toolchain
- SkiaSharp rendering fidelity may differ subtly from CoreImage (color management, tone mapping); document any deviation in `docs/port-notes.md`
- ONNX models for content-aware fill are large; make them optional download at runtime

## Status (2026-09-20)

- **Phase 0 done**: solution + CI + smoke entry (headless console mode; `--ui` launches Avalonia).
- **Phase 1 done**: document model aligned to upstream 9-mode blend set, UUIDs, transform block; `ProjectStore` zip `.comp` (format v1 = upstream v6 subset, see `docs/RESEARCH.md`); 16 tests incl. byte-identical round-trip and 12 rejection cases (81c910c, fa6229a, 165c3e9).
- **UI shell increment**: Avalonia 11.2 Fluent dark window, canvas checkerboard + layer rects, layers panel (add/delete/up/down/visibility/selection), 4 headless UI tests + 9 view-model tests. Remaining for full Phase 3: file dialogs (open/save wiring exists in `ProjectStore`), zoom/pan, menu bar, tools.
- Deferred to pixel-engine phase: PNG asset encode/decode in `.comp` (layers are blank until raster lands; manifest already reserves `image`).

## Status update (2026-09-20, task 5)

- **Raster engine increment live**: `RasterSurface` now carries RGBA8 pixels; soft round brush with stroke-opacity cap (per upstream `brush-performance.md`); `PaintCommand` gives exact-pixel undo/redo.
- **PNG codec**: minimal 8-bit RGBA encoder/decoder (CRC-checked) in `Compositor.Core.Imaging`; `.comp` files now embed `images/<uuid>.png` for painted layers, round-trip pixel-exact (test-enforced).
- **Test count**: 39 Core + 13 App (4 headless UI) = 52, all green locally and in CI (`eb134a2`).
- Next up (unchanged): brush UI wiring in the canvas, file dialogs, zoom/pan, PNG/JPEG/WebP import via SkiaSharp.

## Status update (2026-09-20, task 6): brush wired to canvas

- **Live painting works end-to-end**: pointer drag on the canvas paints the ACTIVE layer in document coordinates; blank layers materialize a full-doc `RasterSurface` on first stroke; locked layers and empty stacks are no-ops.
- **Stroke-granularity undo**: editor live-paints each segment for feedback, then files ONE `StrokeCommand` per stroke via new `UndoHistory.Record` (record-without-execute; re-applying an alpha stroke would double-darken it). Click-dots stamp on `EndStroke`. `Ctrl+Z` / `Ctrl+Y` + toolbar buttons round-trip exact pixels.
- **Canvas renders real pixels**: per-layer cached `WriteableBitmap` (Rgba8888 + Unpremul, zero-copy `Marshal.Copy`), refreshed only when `RasterSurface.Version` moves; blank layers keep placeholder tints.
- **Hit testing**: `CanvasView` implements `ICustomHitTest` (Avalonia.Rendering); a plain Control with no DrawList is otherwise invisible to the hit tester (verified against Avalonia 11.2.7 `CompositionDrawListVisual.HitTest`).
- **Test count**: 47 Core + 22 App (6 headless UI) = 69, all green locally. Headless drag test proves hit-test + coordinate mapping + paint + undo end-to-end.
- Next up: file dialogs (open/save), zoom/pan, PNG/JPEG/WebP import via SkiaSharp.

## Selection system (WS3) - 2026-09-21
- DocumentSelection/SelectionClip/CoverageBackedSelection, 2x2 supersampled antialiased masks
- Rectangle/ellipse/polygon(lasso) shapes, replace/add/subtract combine, invert, select-all
- MagicWand contiguous flood fill with tolerance
- Constrained brush (mask multiplies stamp alpha), masked blend for patches
- Clipboard: copy/cut/paste, FloatingSelection nudge/commit/cancel, single undo step
- UI: Select menu, tool picker, dashed outline + live draft, floating pixel overlay, Ctrl+A/D/Shift+I/X/C/V, Delete/Enter/Escape
