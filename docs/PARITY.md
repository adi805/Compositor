# Parity Matrix: Compositor-Windows vs upstream Mac Compositor

Target: 100% fungsional paritas dengan `robbietilton/Compositor` (Mac, MIT).
Sumber audit: clone upstream 2026-09-21, 282 file Swift, 24.018 LOC
(Document 8.071 / Rendering 3.583 / UI 3.425 / IO 1.020 + kernels C).

Status: `done` = setara fungsional · `partial` = ada tapi belum setara · `missing` = belum ada · `n/a` = platform-spesifik Mac.
Kontrak kerja = plan tool "100% parity" (13 workstream). Matriks ini di-update tiap workstream selesai.

## Document (upstream 8.071 LOC)

| Upstream file | LOC | Status | Catatan |
|---|---|---|---|
| BrushStroke.swift | 899 | done | WS7: stamping/spacing/hardness/opacity-cap/eraser port penuh (coverage mask screen/max); flow + pressure = n/a upstream (gak ada di Mac) |
| EditorSession.swift | 689 | partial | Peran session dipecah ke EditorViewModel; belum tool-state lengkap |
| HueSaturation.swift | 574 | partial | WS4: band math + master/colorize + sheet; per-band spectrum UI & eyedroppers missing |
| Filters.swift | 473 | partial | WS9: Gaussian/Motion blur, Add Noise, Lens Correction + Grain ported (noise/lens/grain bit-exact from NoisePixels.c, LensPixels.c, adjust_grain). RemoveBackground + ContentAwareFill = Apple Vision, masuk WS12 |
| LayerMask.swift | 419 | missing | WS5 (layer mask system terpisah, bukan group) |
| Selection.swift | 310 | partial | Rect/ellipse/lasso/mode+antialias coverage, invert, clip (WS3); feather/expand/contract missing |
| Distort.swift | 291 | missing | WS8 |
| LayerTransform.swift | 235 | partial | WS5: Scaled/Rounded/Mirrored/IsValid + typed scale/rotate UI; drag handles interaktif di WS11 |
| LiveLayerMask.swift | 230 | missing | WS5 |
| Levels.swift | 228 | done | WS4: engine + histogram + auto + sampling; slider sheet |
| ColorPalette.swift | 216 | missing | WS10 |
| SmudgeLiquify.swift | 210 | missing | WS8 |
| ProjectWorkspace.swift | 210 | missing | WS10 (multi-project tabs) |
| SelectionEdits.swift | 205 | partial | Constrained paint + masked blend (WS3) |
| SelectionClipboard.swift | 204 | done | Copy/cut/paste via selection, floating commit as one undo step |
| EditorSession+Brush.swift | 201 | done | WS7: settings lengkap (diameter/hardness/opacity/eraser) di VM + toolbar |
| Crop.swift | 199 | done | WS6: CropCommand via contentOffset + CropGeometry.valid + crop-to-selection; drag-tool visual di WS8 |
| LayerGroups.swift | 190 | partial | WS5: hierarchy entries/validate/visible, group selected + new folder + reorder undo-able, indent rows; drag-reorder di panel belum |
| FloatingSelection.swift | 159 | partial | Floating overlay + nudge/commit/cancel; no drag-move yet |
| ShapeTool.swift | 157 | missing | WS8 |
| MagicWand.swift | 138 | partial | Contiguous flood-fill with tolerance; no sample-merged mode |
| ImageAdjustments.swift | 134 | done | WS4+WS9: AdjustmentColor, Exposure, GradientMap, Grain (value-noise lattice + midtone weighting + origin/unitsPerPixel document-space pinning) semua port |
| DocumentHistory.swift | 119 | partial | UndoHistory ada (per-command); belum edit-group coalescing |
| GuidedMatte.swift | 118 | missing | WS12 (ML) |
| LayerAdjustment.swift | 112 | partial | WS4: AdjustmentKind + settings ported; non-destructive adjustment LAYERS not yet |
| SubjectRemoval.swift | 110 | missing | WS12 (ML) |
| AdjustmentEditing.swift | 110 | partial | WS4: sheet state + commit/cancel in VM; preview synchronous (no async pipeline) |
| Gradient.swift | 109 | missing | WS8 |
| MaskTracing.swift | 92 | missing | WS5 |
| LayerAppearance.swift | 88 | partial | Blend enum 9/13 mode, opacity ada; 4 mode non-separable + UI picker belum (WS2/WS5) |
| LevelsAutomatic.swift | 84 | done | WS4: contrast/color/neutral + eyedropper sampling |
| CanvasSize.swift | 82 | done | WS6: CanvasSizeDraft units math ada di Core (anchor formula + caps); UI pixels-mode |
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
| ImageExporter.swift | 146 | partial | WS10: PNG + JPEG export, cap 30k/side + 100MP, DPI (pHYs / JFIF), matte untuk transparency. Gap: composit masih ignore Layer.Transform (layar honor), jadi scale/rotate layer belum ikut ke-export - dibeton di WS11; adjustment/folder masks belum |
| ImageResizer.swift | 117 | done | WS6: ImageSizeCommand (transform scale + bilinear resample + caps), resolution di manifest |
| CanvasResizer.swift | 72 | done | WS6: CanvasResizeCommand (anchor offsets, fill extension layer, non-destructive) |
| ImageImporter.swift | 63 | partial | WS10: budget 100MP/30k per file (dihitung ulang per file ala upstream), EXIF orientation, RGBA8 straight-alpha, gagal pakai taksonomi yang sama. Gap: HEIC + TIFF butuh codec yang gak ada di Skia build ini; thumbnail 96px asset belum dibuat |
| ImageFileDrop.swift | 55 | partial | WS10: drop file ke canvas (pasteboard order, drop point jadi posisi), fallback ke image data in-memory buat screenshot/gambar dari browser (upstream salin ke file sementara), pesan gagal per-file. Gap: routing ke workspace/tab lain (ProjectWorkspace belum ada) |
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
| HueSaturationSheet (193) | partial | WS4: 7 band + hue/sat/lightness + colorize; spectrum per-band + eyedropper belum |
| ColorPickerSheet (185) | partial | Swatch sederhana; belum picker penuh |
| ProjectTabs (178) | missing | WS10 |
| FilterSheet (163) | partial | WS9: Filter menu + sheet, live preview, readout per parameter; dial sudut drag & preview low-res upstream belum |
| LevelsSheet (144) | partial | WS4: slider 5 kanal + 3 Auto + histogram live + channel picker |
| LassoControls (128) | missing | WS3 |
| ImageSizeSheet (120) | partial | WS6: sheet pixels-mode + DPI (anchor picker 9 opsi); units inches/cm/percent + relative/locked belum di UI |
| BrushControls (120) | partial | Size + 5 warna; belum hardness/flow/opacity kuas |
| CanvasSizeSheet (113) | partial | WS6: sama seperti ImageSizeSheet |
| TransformInspector (112) | missing | WS5 |
| GradientControls (103) | missing | WS8 |
| FloatingPanel (101) | n/a | Pola panel Mac (NSPanel floating); kita adaptif jadi Border in-window |
| NewCanvasSheet (87) | partial | New document default saja |
| JPEGExportSheet (87) | partial | WS10: slider quality 0-1 step 0.01 + readout %, matte picker (no alpha), ukuran hasil encode live, quality terakhir dipersist, dims + sRGB + dpi |
| ColorPaletteControls (79) | missing | WS10 |
| CanvasThumbnail (79) | missing | WS10 |
| LayersPanel (76) | partial | Ter-cover NativeLayerList baris atas |
| CurvesControls (69) | partial | WS4: canvas editor add/drag points; no spectrum/histogram underlay |
| NavigationToolHeader (64) | partial | Toolbar sederhana ada |
| ProjectWindowBridge (62) | n/a | Bridging Mac |
| BlendModePicker (57) | done | WS5: ComboBox 13 mode terikat ActiveBlend |
| LayerAppearanceControls (54) | partial | WS5: opacity slider + blend picker + scale/rotate fields; belum cycle shortcut Shift+- |
| ShapeControls (49) | missing | WS8 |
| SliderSnap (42) | missing | WS10 |
| ToolHeaderStyle (26) | partial | |
| CropControls (24) | partial | WS6: crop numeric sheet + crop-to-selection; visual drag frame + snap + ratio di WS8 |
| LayerMaskMenu (14) | missing | WS5 |

## Ringkasan

- Setelah WS10: done 14 · partial 41 · missing 32 · n/a 3 = 90 baris matriks. Cara hitung (bisa direproduksi): `awk '/^## Ringkasan/{exit} {print}' docs/PARITY.md > /tmp/body.md` lalu `grep -c "| done |" /tmp/body.md` dst. Semua baris wajib pakai empat status kanonik; `n/a-ish` dulu ada satu (FloatingPanel) dan sudah dirapikan ke `n/a` supaya hitungannya tertutup.
- Urut dependensi (workstream plan): WS2 blend engine → WS3 selection → WS4 adjustments → WS5 layer power → WS6 geometry → WS7 brush v2 → WS8 tools → WS9 filters → WS10 IO/UX → WS11 rendering perf → WS12 ML decision → WS13 release.
- Catatan jujur: SubjectRemoval/ContentFill/GuidedMatte di Mac pakai Apple Vision ML. Paritas di Windows berarti ONNX Runtime + model terbuka; keputusan arsitektur di WS12, hasilnya di-update di matriks ini.

## WS4 - Adjustments - 2026-09-21
- Engine: LevelRange/LevelsSettings (+composite RGB∘channel, 3x256 LUT with interpolation), LevelsAuto (contrast/color/neutral via 0.1% tails + neutral gamma), 4x256 histogram (RGB = mean of channels, coverage-weighted), Curves (shape-preserving Hermite, per-channel composed with RGB), HueSaturation (7 Photoshop bands, per-degree response, multiplicative saturation, lightness pull, colorize), Exposure (sRGB decode/encode LUT), GradientMap (Rec.709 luma LUT), Invert
- Runner: whole-surface LUT/per-pixel ops + selection blend coverage×adj+(1-cov)×orig; straight-alpha storage = LUT direct on RGB
- Undo: AdjustmentCommand (buffer swap, idempotent redo)
- UI: Adjust menu, floating sheet panel (Levels sliders+auto+histogram, Curves editor, HueSat sliders, Exposure sliders, GradientMap pickers), live preview via canvas generation-keyed bitmap
- Tests: 35 Core (hand-computed values) + 10 App (sheet flow, commit/cancel, undo, selection-constrained)

## WS5 - Layer power - 2026-09-21
- Hierarchy: Layer.ParentId/IsGroup, LayerHierarchy (entries/visible/descendants/validate: depth<=64, parent harus group, anti-cycle), flatten + canvas render hierarchy-aware (hidden group = subtree disembunyiin)
- Groups: GroupLayersCommand (Folder N auto-name, common parent), AddGroupCommand, ReorderLayerCommand (move up/down undo-able), rows ter-indent (Depth*16)
- Merge: MergeLayersCommand = bake blend+opacity stack (CompositeStack) + trim ke content bounds (SurfaceOps.ContentBounds/Crop), hasil jadi pixel layer dengan transform origin; undo restore penuh
- Flip: pixel bake (SurfaceOps.FlipCopy) + transform flag toggle (kedua-duanya, sengaja, sampai WS11 transform-driven rendering), FlipLayerCommand + FlipCanvasCommand (semua layer + selection termirror)
- Transform: SetLayerTransformCommand; UI typed Scale%/Rotate (LostFocus), CenterX/Y preserved ala upstream scaled(toPercent:)
- Appearance: opacity slider + blend ComboBox (13 mode) terikat ActiveOpacity/ActiveBlend via SetLayerAppearanceCommand; panel hidden untuk group
- Format: manifest v2 (+parentUUID/isGroup); app v0.2 nolak file baru dengan pesan jelas
- BUG FIX bawaan: ProjectStore.Save bocor stream image entry kedua dst (multi-image save selalu crash "entries still open") - kelihatan pertama kali karena test lama maksimal 1 layer berpixel
- Tests: 20 Core + 9 App; total 207 (148 Core + 59 App)

## WS6 - Geometry ops - 2026-09-21
- CanvasResizeCommand: 9-anchor offset formula persis upstream (floor((delta)*(anchor%3)/2)), shift semua layer origin, pixel non-destruktif; optional fill = bottom layer "Canvas Extension" dengan hole transparan di rect canvas lama (upstream context.clear)
- Crop = CanvasResize dengan contentOffset (-rect.origin), validasi CropGeometry.valid (1..30000, origin<=1e6); Crop-to-Selection via union bounds shape
- ImageSizeCommand: skala transform (origin+size, rotasi dipertahankan), resample bilinear surface ke size baru; same-dims = resolution-only; cap 100MP + dims 1..30000 + DPI 1..9600
- ResampleBilinear: half-pixel-center, edge clamp, straight-alpha per channel
- Document.SetSize + Resolution (default 72) + manifest field "resolution" (additive v2, optional, default saat load)
- BUG FIX: CoversCanvas layer dengan pixel (hasil BeginStroke yang materialisasi pixel tanpa update transform) sekarang ikut resample ke canvas baru - sebelumnya pixel-nya stale
- UI: menu Image (Image Size/Canvas Size/Crop/Crop to Selection), floating GeometryPanel (W/H/DPI, anchor picker 9 opsi, fill checkbox, crop X/Y/W/H)
- Tests: 24 Core + 10 App; total 242 (172 Core + 70 App)
- Partial vs upstream: units (percent/inches/cm) + relative/locked di CanvasSizeDraft belum di-UI (math units-nya sugar di atas op pixel yang lengkap); visual drag-crop tool nunggu WS8 tools

## WS7 - Brush v2 - 2026-09-21
- BrushSettings (upstream defaults: diameter 40, hardness 1, opacity = stroke cap, erasing flag); Validate() ala guard BrushStroke init
- StrokeCoverage: pre-rendered soft tip (Gaussian k=2.5 exact port, normalized hardness->rim), stamp spacing = max(0.25, diameter x fraction), fraction 1.5% hard / 2.5% soft, remainder state nyambung antar segmen (upstream walk(to:))
- Dab -> coverage mask: screen (soft) / max (hard) = overlapping dabs gak pernah nglampau full coverage; paint lewat warna dgn opacity cap Photoshop-style (recompute dari before-snapshot = live == final, idempoten)
- Eraser: stroke nurunin alpha layer (coverage x opacity), RGB dipertahankan
- StrokeCommand ctor BrushSettings + legacy adapter (radius/rgba/opacity); VM: slider diameter/hardness/opacity + toggle eraser; PaintDotIfNeeded obsolete (WalkTo dab titik pertama langsung)
- JUJUR: upstream Mac GAK punya knob "flow" terpisah (deposition rate = spacing x falloff) dan GAK ada stylus pressure di jalur paint (pressure cuma di konstruktor event test) - jadi dua-duanya n/a upstream, bukan missing di port
- Tests: 15 Core (falloff hand-computed, spacing fraction, tip profile, cap, live-vs-final identik, eraser, undo) + 6 App; total 263 (187 Core + 76 App)

## WS8 - Tools - 2026-09-22
- GradientFill: linear (proyeksi start→end) + radial (pusat start, rim end), 2 stop (fg→bg alpha 1 / fg→fg fade 0), reversed, opacity cap, komposit source-over dari before-snapshot
- ShapeRasterizer: rectangle + cornerRadius (clamp setengah sisi pendek = pill; ellipse abaikan) + ellipse, coverage 2x2 supersample, clamp radius persis upstream
- BlurStroke: sigma = clamp(diameter/10, 1.5, 30), sample di ambil saat stroke mulai (re-stroke = makin blur, sesuai upstream), gaussian 3-pass box blur di ruang premultiplied, paint lewat coverage mask
- CloneStroke: offset source - brush (whole pixel), aligned keep offset antar stroke (re-set saat source baru), sample layer saat stroke mulai, copy lewat coverage
- SmudgeStroke: carried square (2r+1)² premultiplied, pickUp saat start, dab lerp weight smoothstep t²(3-2t) hardness→rim, keep = strength, spacing max(1, diameter×8%) - port persis upstream; LIQUIFY push warp BELUM (deferred, lihat catatan)
- MagicWand UI: tool click → flood fill → selection (tolerance 32 default)
- VM: BeginTool/ContinueTool/EndTool dispatch per tool; RegionCommand undo (before/after region); clone source via SetCloneSource; toolbar 10 tool
- Tests: 14 Core + 6 App; total 284 (201 Core + 83 App)
- JUJUR partial: gradient/shape belum live preview saat drag (commit saat release, upstream live pending-edit); liquify warp belum; clone "sample all layers" belum


## WS9 - Filters - 2026-09-24
- Set = Filters.swift: Gaussian Blur, Motion Blur, Add Noise, Lens Correction di menu Filter; Grain ikut menu Adjust karena upstream menandai curves/exposure/gradientMap/grain sebagai `isImageAdjustment`, bukan entri Filter
- Kernel: `noise_add`, `lens_distort`, `adjust_grain` diport bit-eksak dari C upstream (hash + box-Muller + lattice value-noise + pembobotan midtone `0.4 + 2.4*L*(1-L)`); rounding dijaga AwayFromZero supaya sama dengan `lroundf`/`+0.5f` upstream
- Blur: dua buffer premultiplied, margin `radius*3+2` (Gaussian) / `distance/2+2` (Motion), edge unclamped supaya "melunakkan tepi layer dan menyebar ke ruang yang disediakan" seperti upstream, lalu crop balik ke ukuran layer
- Motion radius = `distance/sqrt(12)`: konversi panjang garis ke sebaran Gaussian persis komentar upstream, diverifikasi test
- Seed per-aplikasi (bukan state global): `FilterRunner.Apply(..., seed)` mengikuti `FilterJob.seed`; VM menggambar seed baru tiap sheet dibuka dan menahannya selama preview supaya pola tidak "berenang" saat slider digerakkan
- Menu Filter menampilkan Remove Background + Content-Aware Fill dalam keadaan disabled dengan tooltip, supaya gap ML terlihat dan bukan hilang diam-diam (WS12)
- Undo: reuse `AdjustmentCommand`; lens distortion 0 diperlakukan identity sehingga commit-nya menutup sheet tanpa menambah langkah undo kosong
- Bug ketangkep test: guard `OpenAdjustmentSheet`/`ApplyInvert` cuma ngecek `OpenAdjustment` + `Floating`, jadi sheet Adjust bisa dibuka di atas sheet Filter yang masih hidup dan dua preview rebutan satu slot. Sekarang kedua arah ditutup.
- Tests: 41 Core (golden noise/lens dihitung ulang dengan implementasi Python independen dari C upstream; blur pakai properti kernel ternormalisasi + konservasi energi di seam 153+102=255) + 10 App (alur sheet, seed stability, eksklusivitas dua arah, locked layer, satu langkah undo, identity tanpa langkah)
- Total: 242 Core + 93 App = 335 hijau; build bersih 0 warning
- Belum: RemoveBackground/ContentAwareFill (WS12), preview low-res saat drag (upstream downscales preview surface; kita full-res), dial sudut drag ala upstream (pakai slider)


## WS10 - IO parity - 2026-09-24
- Export: PNG pakai encoder Core sendiri + chunk pHYs dari `Document.Resolution`; JPEG lewat Skia dengan alpha di-flatten ke matte, persis pola upstream (context tanpa alpha diisi warna matte, canvas digambar di atasnya). Cap dipake berdua: 1..30000 per sisi dan 100MP, pesan error disalin dari upstream biar diagnosanya sama.
- JFIF density: encoder Skia nulis APP0 dengan unit aspect, jadi density di-patch setelah encode - ditambal kalau APP0 sudah ada, disisip tepat setelah SOI kalau belum. Bukan angka tempelan: 300 dpi ditulis, 300 dpi dibaca balik oleh test.
- Import: satu jalur API (`ImportImage` / `ImportImages` / `ImportImageBytes`) yang ngenal kontainer dari magic bytes, bukan ekstensi - alasan yang sama yang ditulis upstream di `ImageFileDrop`. Matrix format jadi satu-satunya sumber buat filter dialog, pesan "unsupported", dan daftar gap.
- Yang beneran bisa di-import: PNG, JPEG, GIF, BMP, ICO, WebP. Semuanya dibuktiin pake fixture nyata buatan tool lain (Pillow/ffmpeg), bukan hasil encoder yang lagi diuji. Yang gak bisa dan ditulis alasannya: TIFF + HEIC (upstream narik dua-duanya dari ImageIO).
- Budget impor: sisa 100MP dihitung ulang per file dalam satu batch (kayak `drainImports` upstream), jadi layer yang udah ada ngurangin anggaran.
- EXIF orientation: tag-nya gue parse sendiri (APP1/TIFF, little-endian dan big-endian), TAPI pixel-nya sengaja gak gue puter: terbukti jalur encoded-image Skia udah ngasih pixel tegak. Ekspektasi awal gue (puter sendiri) bikin rotasi ganda dan ditolak test yang pake JPEG EXIF buatan Pillow.
- Placement: upstream naro gambar di tengah drop point (atau tengah kanvas kalau tanpa titik). Kita bake ke surface - surface selalu seukuran kanvas - dengan clip buat bagian yang keluar kanvas.
- Drop-to-import: `AllowDrop` di canvas + `TryGetFiles()`. Kalau gak ada file (screenshot, gambar dari browser) kita ambil image data dan encode in-memory; upstream nyelipin ke file sementara, kita gak nyentuh disk sama sekali.
- Sheet JPEG = `JPEGExportSheet` upstream: slider quality 0..1 step 0.01 + readout persen, matte picker tanpa alpha, ukuran hasil encode live, baris dims + sRGB + dpi, dan quality terakhir dipersist (padanan UserDefaults). Beda jujur: preview kita angka doang, gak ada thumbnail + spinner debounce.
- DEFECT yang ketahuan dan sengaja belum dibenerin di sini: `Flatten.ToRgba` dan `SurfaceOps.CompositeStack` skip layer yang surface-nya bukan seukuran kanvas DAN ignore `Layer.Transform`, sementara `CanvasView` honor transform itu. Artinya export != layar begitu layer di-scale/rotate. Perbaikannya = composit transform-aware, masuk WS11.
- Belum ada padanannya: thumbnail 96px per asset (`ImportedImage.thumbnail`), routing drop ke tab/workspace lain (`ProjectWorkspace`), dan impor sebagai undo step (add/remove layer belum punya command type).
- Tests: 300 Core (+58) + 122 App (+29) = 422 hijau; build bersih 0 warning; `--smoke` exit 0.
