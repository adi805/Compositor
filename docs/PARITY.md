# Parity Matrix: Compositor-Windows vs upstream Mac Compositor

Target: 100% fungsional paritas dengan `robbietilton/Compositor` (Mac, MIT).
Sumber audit: clone upstream 2026-09-21, 282 file Swift, 24.018 LOC
(Document 8.071 / Rendering 3.583 / UI 3.425 / IO 1.020 + kernels C).

Status: `done` = setara fungsional · `partial` = ada tapi belum setara · `missing` = belum ada · `n/a` = platform-spesifik Mac.
Kontrak kerja = plan tool "100% parity" (13 workstream). Matriks ini di-update tiap workstream selesai.

## Document (upstream 8.071 LOC)

| Upstream file | LOC | Status | Catatan |
|---|---|---|---|
| BrushStroke.swift | 899 | partial | Kit punya soft-round brush; belum spacing/stamping/hardness/flow/pressure (WS7) |
| EditorSession.swift | 689 | partial | Peran session dipecah ke EditorViewModel; belum tool-state lengkap |
| HueSaturation.swift | 574 | partial | WS4: band math + master/colorize + sheet; per-band spectrum UI & eyedroppers missing |
| Filters.swift | 473 | missing | WS9 |
| LayerMask.swift | 419 | missing | WS5 |
| Selection.swift | 310 | partial | Rect/ellipse/lasso/mode+antialias coverage, invert, clip (WS3); feather/expand/contract missing |
| Distort.swift | 291 | missing | WS8 |
| LayerTransform.swift | 235 | partial | Model transform ada; belum interaktif edit (WS5) |
| LiveLayerMask.swift | 230 | missing | WS5 |
| Levels.swift | 228 | done | WS4: engine + histogram + auto + sampling; slider sheet |
| ColorPalette.swift | 216 | missing | WS10 |
| SmudgeLiquify.swift | 210 | missing | WS8 |
| ProjectWorkspace.swift | 210 | missing | WS10 (multi-project tabs) |
| SelectionEdits.swift | 205 | partial | Constrained paint + masked blend (WS3) |
| SelectionClipboard.swift | 204 | done | Copy/cut/paste via selection, floating commit as one undo step |
| EditorSession+Brush.swift | 201 | partial | VM wiring brush ada; belum parameter lengkap |
| Crop.swift | 199 | missing | WS6 |
| LayerGroups.swift | 190 | missing | WS5 |
| FloatingSelection.swift | 159 | partial | Floating overlay + nudge/commit/cancel; no drag-move yet |
| ShapeTool.swift | 157 | missing | WS8 |
| MagicWand.swift | 138 | partial | Contiguous flood-fill with tolerance; no sample-merged mode |
| ImageAdjustments.swift | 134 | partial | WS4: Exposure + GradientMap done; Grain → WS9 |
| DocumentHistory.swift | 119 | partial | UndoHistory ada (per-command); belum edit-group coalescing |
| GuidedMatte.swift | 118 | missing | WS12 (ML) |
| LayerAdjustment.swift | 112 | partial | WS4: AdjustmentKind + settings ported; non-destructive adjustment LAYERS not yet |
| SubjectRemoval.swift | 110 | missing | WS12 (ML) |
| AdjustmentEditing.swift | 110 | partial | WS4: sheet state + commit/cancel in VM; preview synchronous (no async pipeline) |
| Gradient.swift | 109 | missing | WS8 |
| MaskTracing.swift | 92 | missing | WS5 |
| LayerAppearance.swift | 88 | partial | Blend enum 9/13 mode, opacity ada; 4 mode non-separable + UI picker belum (WS2/WS5) |
| LevelsAutomatic.swift | 84 | done | WS4: contrast/color/neutral + eyedropper sampling |
| CanvasSize.swift | 82 | missing | WS6 |
| LayerFlip.swift | 79 | missing | WS5 |
| LayerMerge.swift | 72 | missing | WS5 |
| PixelAdjust.swift | 65 | partial | WS4: selection blend + coverage ported; CI infra n/a |
| EditorSession+Projects.swift | 55 | partial | Open/save via VM ada |
| CloneStamp.swift | 49 | missing | WS8 |
| PixelInvert.swift | 46 | done | WS4: straight-alpha invert + selection clip |
| Curves.swift | 42 | done | WS4: Hermite spline + LUT + curves editor UI |
| BlurTool.swift | 41 | missing | WS8 |
| ContentFill.swift | 27 | missing | WS12 (ML) |

## IO (upstream 1.020 LOC)

| Upstream file | LOC | Status | Catatan |
|---|---|---|---|
| ProjectController.swift | 299 | partial | ProjectStore kita (zip .comp, atomic, validasi) |
| ProjectStore.swift | 225 | done | Round-trip teruji; format v1 kita sendiri, referensi skema upstream v6 |
| ImageExporter.swift | 146 | partial | PNG flatten Normal-only; butuh per-layer blend (WS2), JPEG + cap 100MP + DPI (WS10) |
| ImageResizer.swift | 117 | missing | WS6 |
| CanvasResizer.swift | 72 | missing | WS6 |
| ImageImporter.swift | 63 | partial | PNG only |
| ImageFileDrop.swift | 55 | missing | WS10 |
| CompositorApplicationDelegate.swift | 43 | n/a | Lifecycle Mac |

## Rendering (upstream 3.583 LOC)

| Upstream file | LOC | Status | Catatan |
|---|---|---|---|
| EditorCanvas.swift | 1814 | partial | CanvasView: paint/zoom/pan; belum marquee/rulers/overlays |
| TiledLayerRenderer.swift | 419 | missing | WS11 (perf) |
| TransformOverlay.swift | 327 | missing | WS11 |
| RasterSnapshot.swift | 176 | partial | Flatten kita |
| LayerRenderer.swift | 173 | partial | |
| MetalBrushCoverage.swift | 162 | partial | CPU coverage path kita |
| LiveMaskRenderer.swift | 145 | missing | WS5 |
| DownsampleCache.swift | 112 | missing | WS11 |
| BrushCursorOverlay.swift | 95 | missing | WS7 |
| CanvasViewport.swift | 72 | partial | View transform ada |
| SampleRingOverlay.swift | 29 | missing | WS8 (clone/smudge aid) |
| AdjustmentSurface.swift | 17 | missing | WS4 |
| SeparableBlend.swift | 17 | partial | PDF-correct ColorDodge/Burn dibutuhkan di WS2 |

## UI (upstream 3.425 LOC)

| Upstream | Status | Catatan |
|---|---|---|
| NativeLayerList (896) | partial | Layer list add/del/reorder/visibility; belum drag-reorder/grup/thumbnail |
| HueSaturationSheet (193) | missing | WS4 |
| ColorPickerSheet (185) | partial | Swatch sederhana; belum picker penuh |
| ProjectTabs (178) | missing | WS10 |
| FilterSheet (163) | missing | WS9 |
| LevelsSheet (144) | missing | WS4 |
| LassoControls (128) | missing | WS3 |
| ImageSizeSheet (120) | missing | WS6 |
| BrushControls (120) | partial | Size + 5 warna; belum hardness/flow/opacity kuas |
| CanvasSizeSheet (113) | missing | WS6 |
| TransformInspector (112) | missing | WS5 |
| GradientControls (103) | missing | WS8 |
| FloatingPanel (101) | n/a-ish | Pola UI; adaptif per platform |
| NewCanvasSheet (87) | partial | New document default saja |
| JPEGExportSheet (87) | missing | WS10 |
| ColorPaletteControls (79) | missing | WS10 |
| CanvasThumbnail (79) | missing | WS10 |
| LayersPanel (76) | partial | Ter-cover NativeLayerList baris atas |
| CurvesControls (69) | partial | WS4: canvas editor add/drag points; no spectrum/histogram underlay |
| NavigationToolHeader (64) | partial | Toolbar sederhana ada |
| ProjectWindowBridge (62) | n/a | Bridging Mac |
| BlendModePicker (57) | missing | WS5 |
| LayerAppearanceControls (54) | missing | WS5 |
| ShapeControls (49) | missing | WS8 |
| SliderSnap (42) | missing | WS10 |
| ToolHeaderStyle (26) | partial | |
| CropControls (24) | missing | WS6 |
| LayerMaskMenu (14) | missing | WS5 |

## Ringkasan

- Done: 5. Partial: 25. Missing: 39. n/a: 3. (setelah WS4)
- Urut dependensi (workstream plan): WS2 blend engine → WS3 selection → WS4 adjustments → WS5 layer power → WS6 geometry → WS7 brush v2 → WS8 tools → WS9 filters → WS10 IO/UX → WS11 rendering perf → WS12 ML decision → WS13 release.
- Catatan jujur: SubjectRemoval/ContentFill/GuidedMatte di Mac pakai Apple Vision ML. Paritas di Windows berarti ONNX Runtime + model terbuka; keputusan arsitektur di WS12, hasilnya di-update di matriks ini.

## WS4 - Adjustments - 2026-09-21
- Engine: LevelRange/LevelsSettings (+composite RGB∘channel, 3x256 LUT with interpolation), LevelsAuto (contrast/color/neutral via 0.1% tails + neutral gamma), 4x256 histogram (RGB = mean of channels, coverage-weighted), Curves (shape-preserving Hermite, per-channel composed with RGB), HueSaturation (7 Photoshop bands, per-degree response, multiplicative saturation, lightness pull, colorize), Exposure (sRGB decode/encode LUT), GradientMap (Rec.709 luma LUT), Invert
- Runner: whole-surface LUT/per-pixel ops + selection blend coverage×adj+(1-cov)×orig; straight-alpha storage = LUT direct on RGB
- Undo: AdjustmentCommand (buffer swap, idempotent redo)
- UI: Adjust menu, floating sheet panel (Levels sliders+auto+histogram, Curves editor, HueSat sliders, Exposure sliders, GradientMap pickers), live preview via canvas generation-keyed bitmap
- Tests: 35 Core (hand-computed values) + 10 App (sheet flow, commit/cancel, undo, selection-constrained)
