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

## Adjustments (WS4) - 2026-09-21
- Levels (+Auto, histogram, eyedropper sampling), Curves (Hermite + editor), Hue/Saturation (band-based, colorize), Exposure, Gradient Map, Invert colors
- AdjustmentRunner + AdjustmentCommand; preview overlay keyed by generation counter
- Tests: 128 Core + 49 App = 177 green; smoke OK
- Known gaps: non-destructive adjustment layers, per-band spectrum UI, async preview pipeline, Grain (→ WS9)

## Layer power (WS5) - 2026-09-21
- Layer hierarchy: ParentId/IsGroup, LayerHierarchy validate (depth<=64, anti-cycle, parent-must-be-group), hidden group hides subtree (flatten + canvas)
- Group ops: group selected (Folder N), new folder, reorder up/down, semua undo-able via commands
- Merge down/group: bake blend+opacity, trim ke content bounds, satu undo step
- Flip: layer + whole canvas (pixels baked + transform flags, selection ikut termirror)
- Transform typed: Scale% / Rotate via SetLayerTransformCommand (center preserved)
- Appearance: opacity slider + blend picker (13 mode) di panel layer
- Format manifest v2 (parentUUID/isGroup)
- Tests: 148 Core + 59 App = 207 green; smoke OK
- Known gaps: interactive drag transform (WS11), layer masks (WS9/terpisah), drag-reorder panel

## Tech stack refresh - 2026-09-24
- Avalonia 11.2.7 → 11.3.22 (all four packages + Headless.XUnit). Reason is measured, not cosmetic: 11.2.7 exposes only SetAllowDrop/GetAllowDrop/DoDragDrop on `DragDrop`; 11.3.22 adds AddDropHandler/AddDragOverHandler/DoDragDropAsync and `IDataTransfer`/`TryGetFiles`, which Task 10's file-drop needs and which the official docs describe as the only drop API
- Test stack: xunit 2.7.0 → 2.9.3, xunit.runner.visualstudio 2.5.7 → 3.1.5, Microsoft.NET.Test.Sdk 17.9.0 → 17.14.1
- Verified locally with obj/ + bin/ deleted: build 0 warnings 0 errors, 201 Core + 83 App = 284 green, smoke exit 0
- Held back deliberately: Avalonia 12.1.3 (needs Diagnostics API rename + moves SkiaSharp 2.88 → 3.x, buys nothing for the remaining parity tasks; revisit after the matrix is green)
- Flagged: `net8.0` reaches EOL 2026-11-10, same date as net9. net10.0 is the LTS (2028-11-14) → the TFM bump is required before Task 13's v1.0
- Free win found: SkiaSharp 2.88.9 is already in the tree via Avalonia.Skia and exposes `SKImage.Encode(SKEncodedImageFormat, int)` → Task 10's JPEG export does not require a hand-written JPEG encoder
- See `docs/TECH-STACK.md` for the pin table, the docs-version trap, and the verify commands

## Filters (WS9) - 2026-09-24
- Set filter = Filters.swift: Gaussian Blur, Motion Blur, Add Noise, Lens Correction (menu Filter) + Grain (menu Adjust, sesuai upstream menandainya image adjustment)
- Add Noise, Lens Correction dan Grain diport bit-eksak dari kernel C upstream; blur dua-buffer premultiplied dengan margin sebar dan edge unclamped seperti kontrak upstream
- Motion radius = distance/sqrt(12); seed per-aplikasi lewat parameter, bukan state global
- Remove Background + Content-Aware Fill: entri menu ada tapi disabled dengan tooltip, implementasinya butuh Apple Vision → WS12 (ONNX)
- Bug beneran ketangkep test: sheet Adjust bisa numpuk di atas sheet Filter yang masih hidup (guard hanya satu arah). Ditutup dua arah.
- Tests: 41 Core + 10 App; 242 Core + 93 App = 335 hijau, build 0 warning
- Known gaps: preview full-res (upstream low-res saat drag), dial sudut drag, ML dua entri

## IO parity (WS10) - 2026-09-24
- JPEG export jadi: quality 0..1 (readout persen), matte untuk area transparan, DPI dokumen ditulis ke JFIF, hasil encode live keliatan ukurannya di sheet sebelum Save
- PNG export sekarang baw density (pHYs) dari `Document.Resolution`, jadi ukuran cetak gak ilang begitu keluar file
- Cap satu aturan buat dua arah: 1..30000 per sisi + ceiling surface, dihitung ulang per file pas impor batch (angka ceiling dikoreksi di WS19)
- Format matrix jadi kontrak, bukan komentar: filter dialog, pesan "unsupported", dan daftar gap semuanya dibaca dari situ; tiap baris impor dibuktiin test pake fixture nyata dari Pillow/ffmpeg
- Drop file ke canvas = impor, dengan drop point sebagai posisi; gambar tanpa file (screenshot, drag dari browser) tetep kebaca tanpa nyentuh disk
- Known gaps yang ditulis terang: TIFF + HEIC butuh codec di luar Skia build ini, thumbnail asset 96px belum dibuat, impor belum jadi undo step. `Layer.Transform` SEKARANG dihormati rendering layar + composit export (WS11); sisanya: flip masih di-bake dan flag-nya diabaikan renderer (butuh keputusan migrasi `.comp`), alat gambar masih nulis di koordinat kanvas, `TransformOverlay` handle drag belum ada
- Tests: 300 Core + 122 App = 422 hijau; CI harus tetep hijau di commit ini

## ML dan GuidedMatte (WS12) - 2026-09-25
- Keputusan ditulis di PARITY seksi WS12: dari tiga file yang selama ini dicap "gap ML", cuma SubjectRemoval yang butuh bobot. GuidedMatte itu aritmatika guided filter, ContentFill itu wrapper kernel C.
- Spike ML dibuktikan, bukan dijanjikan: u2netp 4,5 MB (Apache-2.0, sha256 di-pin) load dan inferensi jalan end-to-end lewat OnnxRuntime 1.30.0, hijau di CI ubuntu (run 29). Belum ada klaim kualitas: itu butuh benchmark vs hasil Mac di foto nyata, dan sampai hari ini belum diukur.
- GuidedFilter dipindah ke Core, dependency-free, 10 test golden angka tangan (impulse 3x3 jadi 1/9 di seluruh bidang dengan energi ke-lestarikan, box [2,5] jadi [3,4], slope -0,99955 bikin mask snap ke tepi guide, guide konstan = blur murni, dan math downscale 4000x3000 ke limit 1400 jadi 1400x1050 dengan radius 20 ke-champ 7).
- Total: 339 Core + 149 App = 488 hijau, build bersih 0 warning, --smoke exit 0.
- Masih terbuka buat nutup SubjectRemoval: estimator mask di App, empat field setting upstream yang belum ada di FilterSettings, commit ke alpha, preview low-res pas drag, dan subsystem layer mask (~900 LOC) supaya hasilnya non-destruktif seperti upstream. Atribusi NOTICE buat bobot wajib sebelum rilis.

## Sinkronisasi upstream + limit dokumen (WS19) - 2026-09-25
- **Drift ketemu, dan itu bukan drift kecil.** Seluruh audit paritas sampai hari ini berdiri di atas snapshot `a19db90` yang ternyata **177 commit di belakang** `upstream/main`. Yang tidak pernah masuk daftar periksa: 36 file Swift / 8.899 LOC, isinya PSD import-export, Camera RAW, Type tool, Layer Effects, Object Selection, Guides/Rulers, KeyboardShortcuts.
- Header matriks dikoreksi dari 92 file / 16.755 LOC jadi **128 file / 28.877 LOC** di `c64183f`. Resep ukurnya ditulis di dokumen supaya angka berikutnya bisa diperiksa orang lain, bukan dipercaya.
- **Limit dokumen naik ke semantik upstream.** Upstream memisahkan dua ceiling, kita mencampurnya: `maxSide = 30.000`, `maxSurfacePixels = 200 MP`, dan budget dokumen `min(800 MP, max(200 MP, RAM/16))`. Sebelum ini kita memakai 100 MP untuk keduanya, artinya canvas 100-200 MP yang sah di Mac ditolak di sini. Sekarang `ImageBudget` memakai rumus yang sama; di .NET RAM dibaca dari `GC.GetGCMemoryInfo()`.
- Pesan error ikut berubah karena upstream menyusunnya dari konstanta itu, bukan dari teks tetap.
- Repo di-set `upstream` = `robbietilton/Compositor` supaya `git fetch upstream` jadi cara rutin ngecek drift, dan klaim fork di dokumen dikoreksi: yang benar-benar fork dari upstream Mac di akun ini **nol**.
- Tests: 349 Core + 153 App = **502 hijau**, build 0 warning.
- Urutan berikutnya: 36 baris baru itu mayoritas `missing` dan belum masuk plan. Yang paling murah dulu: Guides + Rulers (435 LOC, murni UI), KeyboardShortcuts (319), TrimSheet (68), NumericScrub (63). Yang mahal dan butuh keputusan: PSD (1.945 LOC) dan Camera RAW (1.783 LOC).

## Status update (2026-09-25, task 17): auto-update

- Help > "Check for Updates..." sekarang hidup: membandingkan versi build dengan rilis terbaru di
  GitHub, memverifikasi SHA256 dan ukuran dari manifest yang dipublish release workflow, lalu
  men-stage paket ke `update-staging/` bersama `apply-update.cmd`. Tidak ada pengecekan otomatis saat
  launch dan tidak ada penggantian binary in-place; dua-duanya ditulis apa adanya sebagai `partial`
  di PARITY, bukan disembunyikan di balik kata "auto-update".
- Yang dibutuhkan supaya baris itu jadi `done`: installer yang mengerjakan swap waktu app ditutup
  (bukan skrip manual), dan tanda tangan paket yang bisa diverifikasi (appcast bertanda tangan atau
  setaranya) supaya sumbernya dipercaya tanpa menyuruh user mengetik kalimat konfirmasi.

## Codec gap TIFF dan HEIC (WS21) - 2026-09-25

- Yang ditolak sekarang diukur, bukan ditebak: `CodecProbeTests` memanggil `SKCodec`/`SKImage` langsung
  pada fixture nyata (Pillow, LIBTIFF 4.5.1, libheif) dengan kontrol PNG yang membuktikan harness-nya
  hidup. Hasil: tidak ada decoder TIFF dan tidak ada decoder HEIF di SkiaSharp 2.88.9.
- Keputusan: TIFF tetap opsi terbuka lewat dependency managed (satu-satunya kandidat yang terverifikasi
  deskripsinya: BitMiracle.LibTiff.NET 2.4.660, 36,8 juta downloads; ImageSharp dan Magick.NET belum
  diverifikasi kemampuannya). HEIC dinyatakan gap: butuh decoder HEVC eksternal yang tidak ada di stack
  ini sama sekali, dan ImageMagick di box ini pun menolak HEVC dengan error yang sama untuk file kamera.
- Belum diukur: win-x64. Yang dibutuhkan cuma satu job `windows-latest` di CI yang menjalankan
  Compositor.App.Tests; angka WS21 sekarang adalah angka runner ubuntu.
