# Parity Matrix: Compositor-Windows vs upstream Mac Compositor

Target: 100% fungsional paritas dengan `robbietilton/Compositor` (Mac, MIT).
Sumber audit: target app Mac saja, yaitu `Compositor/Compositor/` di clone upstream.
Snapshot sekarang **`c64183f`** (upstream/main, tag terakhir v1.2.11), di-fetch 2026-09-25.
Terukur di commit itu: **128 file Swift, 28.877 LOC** di app dir
(Document 12.370 / Rendering 5.477 / UI 7.055 / IO 3.198); 192 file Swift di seluruh repo.
Resep ukur: `find Compositor/Compositor -name '*.swift' | xargs wc -l | tail -1`.

Riwayat koreksi denominator, karena dua-duanya pernah salah:
1. Baris pertama matriks bilang 282 file / 24.018 LOC: itu tercampur kloningan
   `Revan67/Compositor-Windows` yang hidup di `compositor/win/`, bukan upstream Mac.
2. Koreksi berikutnya bilang 92 file / 16.755 LOC: itu benar untuk snapshot yang di-audit
   (`a19db90`, "Publish update feed for Compositor 1.0.4"), tapi snapshot itu **177 commit
   di belakang upstream/main**. Yang hilang bukan detail: 36 file / 8.899 LOC berisi PSD
   import-export, Camera RAW, Type tool, Layer Effects, Object Selection, Guides/Rulers,
   dan `DocumentLimits.swift`. Lihat seksi WS19.

Status: `done` = setara fungsional · `partial` = ada tapi belum setara · `missing` = belum ada · `n/a` = platform-spesifik Mac.
Kontrak kerja = plan tool "100% parity" (13 workstream). Matriks ini di-update tiap workstream selesai.

## Document (upstream 8.071 LOC)

| Upstream file | LOC | Status | Catatan |
|---|---|---|---|
| BrushStroke.swift | 899 | done | WS7: stamping/spacing/hardness/opacity-cap/eraser port penuh (coverage mask screen/max); flow + pressure = n/a upstream (gak ada di Mac) |
| EditorSession.swift | 689 | partial | Peran session dipecah ke EditorViewModel; belum tool-state lengkap |
| EditorSession+Brush.swift | 201 | partial | State alat gambar ada di `EditorViewModel` (BeginTool/ContinueTool, hardness/flow/size, commit stroke). Belum di-audit baris per baris terhadap extension ini. |
| EditorSession+Projects.swift | 55 | partial | Simpan/buka proyek lewat `ProjectStore` + `EditorViewModel`; perilaku session multi-proyek belum setara. |
| HueSaturation.swift | 574 | partial | WS4: band math + master/colorize + sheet; per-band spectrum UI & eyedroppers missing |
| Filters.swift | 473 | partial | WS9: Gaussian/Motion blur, Add Noise, Lens Correction + Grain ported (noise/lens/grain bit-exact from NoisePixels.c, LensPixels.c, adjust_grain). RemoveBackground + ContentAwareFill = Apple Vision, masuk WS12 |
| LayerMask.swift | 419 | missing | WS5 (layer mask system terpisah, bukan group) |
| Selection.swift | 310 | partial | Rect/ellipse/lasso/mode+antialias coverage, invert, clip (WS3); feather/expand/contract missing |
| Distort.swift | 291 | missing | WS8 |
| LayerTransform.swift | 235 | partial | WS5: Scaled/Rounded/Mirrored/IsValid + typed scale/rotate UI. WS11: display transform dihormati layar + export (LayerPlacement/LayerGeometry), alat gambar DAN clip seleksi ikut di-mapping ke layer space (LayerPaintPoint + MapClipToLayer). Drag handle interaktif ada (TransformHandles/TransformDrag, termasuk drag body buat move dan resolve kotak cover-canvas yang Width-nya 0). Gap: magic wand + sheet adjustment masih baca koordinat kanvas, distort mesh belum |
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
| CameraRaw.swift | 638 | missing | WS19: RAW develop (Apple CoreImage RAW). Windows butuh decoder RAW sendiri |
| LayerEffects.swift | 638 | missing | WS19: stroke/shadow/glow per layer |
| ObjectSelection.swift | 298 | missing | WS19 |
| CameraRawColor.swift | 282 | missing | WS19 |
| TypeTool.swift | 263 | missing | WS19: type tool |
| CameraRawGeometryCalibration.swift | 255 | missing | WS19 |
| Guides.swift | 250 | missing | WS19: guides + snap |
| ImageTrim.swift | 210 | missing | WS19: trim border transparan |
| CameraRawDetailOptics.swift | 125 | missing | WS19 |
| DocumentLimits.swift | 42 | done | WS19: diport jadi `ImageBudget`. 30.000 per sisi, **200 MP per surface**, budget dokumen skala RAM (min 800 MP, max 200 MP, RAM/16) |
| ToolDefaults.swift | 22 | missing | WS19: preferensi tool yang nempel lintas tab dan lintas launch |
| MagicWand.swift | 138 | partial | Contiguous flood-fill with tolerance; no sample-merged mode. WS16: seed diambil di layer space (`LayerPaintPoint`) dan hasilnya dikembalikan ke kanvas (`LayerPlacement.PlaceMaskToDocument`), jadi wand di layer yang digeser/diskala milih pixel yang beneran di bawah kursor, bukan pixel kanvas |
| ImageAdjustments.swift | 134 | done | WS4+WS9: AdjustmentColor, Exposure, GradientMap, Grain (value-noise lattice + midtone weighting + origin/unitsPerPixel document-space pinning) semua port |
| DocumentHistory.swift | 119 | partial | UndoHistory ada (per-command); belum edit-group coalescing |
| GuidedMatte.swift | 118 | partial | WS12. BUKAN ML: guided filter He/Sun/Tang murni aritmatika (box running-sum + slope/offset). Port C# di Core + 10 golden (7eb8cb3). Gap: belum dipanggil SubjectRemoval preset Advanced |
| LayerAdjustment.swift | 112 | partial | WS4: AdjustmentKind + settings ported; non-destructive adjustment LAYERS not yet |
| SubjectRemoval.swift | 110 | partial | WS12: jalur ONNX terbukti hidup (4 test spike, 365b057). Belum: estimator di App, 4 field setting upstream, commit ke alpha, UI. Lihat seksi WS12 |
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
| ContentFill.swift | 27 | partial | WS12: kernel `Rendering/ContentFill.c` diport ke `src/Compositor.Core/Imaging/ContentFill.cs` (seed LCG tetap, urutan draw dijaga persis), menu Content-Aware Fill aktif sebagai satu undo step. Gap: clip seleksi masih koordinat kanvas, hasil belum dibandingkan dengan keluaran Mac |

## IO (upstream 1.020 LOC)

| Upstream file | LOC | Status | Catatan |
|---|---|---|---|
| ProjectController.swift | 299 | partial | ProjectStore kita (zip .comp, atomic, validasi) |
| ProjectStore.swift | 225 | done | Round-trip teruji; format v1 kita sendiri, referensi skema upstream v6 |
| ImageExporter.swift | 146 | partial | WS10: PNG + JPEG export, cap 30k/side + 100MP, DPI (pHYs / JFIF), matte untuk transparency. WS11: composit sekarang render lewat placement transform (posisi/skala/rotasi), layar dan export searah. Gap: flip flag masih di-bake (bukan dihormati), adjustment/folder masks belum |
| ImageResizer.swift | 117 | done | WS6: ImageSizeCommand (transform scale + bilinear resample + caps), resolution di manifest |
| CanvasResizer.swift | 72 | done | WS6: CanvasResizeCommand (anchor offsets, fill extension layer, non-destructive) |
| ImageImporter.swift | 63 | partial | WS10: budget 100MP/30k per file (dihitung ulang per file ala upstream), EXIF orientation, RGBA8 straight-alpha, gagal pakai taksonomi yang sama. Gap: HEIC + TIFF butuh codec yang gak ada di Skia build ini; thumbnail 96px asset belum dibuat. Diukur 2026-09-25 (WS21, `CodecProbeTests`): keduanya memang tidak punya decoder di build ini, bukan cuma ditolak policy |
| ImageFileDrop.swift | 55 | partial | WS10: drop file ke canvas (pasteboard order, drop point jadi posisi), fallback ke image data in-memory buat screenshot/gambar dari browser (upstream salin ke file sementara), pesan gagal per-file. Gap: routing ke workspace/tab lain (ProjectWorkspace belum ada) |
| PSD/PSDText.swift | 682 | missing | WS19: text layer di PSD |
| PSD/PSDReader.swift | 543 | missing | WS19: PSD import |
| PSD/PSDVector.swift | 282 | missing | WS19 |
| PSD/PSDChannelCoder.swift | 194 | missing | WS19 |
| PSD/PSDDocumentBuilder.swift | 144 | missing | WS19: PSD export |
| RawImporter.swift | 124 | missing | WS19 |
| PSD/PSDTypes.swift | 100 | missing | WS19 |
| CompositorApplicationDelegate.swift | 43 | n/a | Lifecycle Mac |

## Rendering (upstream 3.583 LOC)

| Upstream file | LOC | Status | Catatan |
|---|---|---|---|
| EditorCanvas.swift | 1814 | partial | CanvasView: paint/zoom/pan; belum marquee/rulers/overlays |
| TiledLayerRenderer.swift | 419 | partial | WS11 (perf): pemilihan tile diport ke Core (`TileGrid`: `Support(level)`, `Aligned`, `Interiors`, `PixelRect`) dengan 24 golden hitungan tangan, termasuk bukti bahwa dab 100 px di kanvas 4096 menyeleksi 4 dari 256 kotak. Gap: komposisi piece (region + margin, kompres ke level, clip hard-edge) belum disambung ke `CanvasView`, jadi render masih satu gambar penuh |
| TransformOverlay.swift | 327 | partial | WS11: kotak transform + 8 handle + handle rotasi digambar di luar clip kanvas; hit-testing dan drag (move/resize/rotate) diport ke Core (`TransformHandles`, `TransformDrag`, `LayerTransform.Point/Contains`) dan disambung ke VM (`BeginTransformDrag`/`ContinueTransformDrag`/`EndTransformDrag`, satu undo step per drag, Shift = snap 15 derajat, Alt = anchor tengah). 42 golden hitungan tangan + 11 test end-to-end lewat VM termasuk bukti flatten ikut geser. Gap: marching-ants LOD buat seleksi kompleks, layout grid, user guides, overlay crop/gradient, dan pilihan lewat kotak grup/distort belum |
| RasterSnapshot.swift | 176 | partial | Flatten kita |
| LayerRenderer.swift | 173 | partial | |
| MetalBrushCoverage.swift | 162 | partial | CPU coverage path kita |
| LiveMaskRenderer.swift | 145 | missing | WS5 |
| DownsampleCache.swift | 112 | partial | WS11: `DownsampleLevels` (Core, aturan level + cakupan pixel, 27 golden) dan `DownsampleCache` (App, chain halving 2x persis, LRU ke pixel budget, invalidasi lewat Version surface) dipakai `CanvasView` buat milih copy terdekat. Gap: halving pakai Skia high-quality, bukan Lanczos vImage; belum diukur perf di kanvas besar |
| BrushCursorOverlay.swift | 95 | missing | WS7 |
| CanvasViewport.swift | 72 | partial | View transform ada |
| SampleRingOverlay.swift | 29 | missing | WS8 (clone/smudge aid) |
| AdjustmentSurface.swift | 17 | missing | WS4 |
| SeparableBlend.swift | 17 | partial | PDF-correct ColorDodge/Burn dibutuhkan di WS2 |
| InlineTextEditor.swift | 453 | missing | WS19 |
| MetalLayerEffects.swift | 395 | missing | WS19: di Windows jalurnya Skia, bukan Metal |
| LayerEffectsSurface.swift | 188 | missing | WS19 |
| EffectsPreviewCache.swift | 164 | missing | WS19 |

## UI (upstream 3.425 LOC)

| Upstream | Status | Catatan |
|---|---|---|
| ContentView (386) | partial | Kerangka jendela, menu dan host kanvas ada di `MainWindow.axaml`; chrome tab proyek dan thumbnail belum 1:1 (lihat baris ProjectTabs/CanvasThumbnail). |
| CompositorApp (270) | partial | Entry app, menu command dan shortcut ada. "Check for Updates..." sudah ada per 2026-09-25 (menu Help), lihat baris Auto-update. |
| Auto-update (Sparkle) | partial | Dilengkapi 2026-09-25: Help > "Check for Updates..." menanyakan rilis terbaru ke GitHub Releases API, membandingkan dengan informational version build (sumbernya `<Version>` di Directory.Build.props), membaca manifest SHA256SUMS yang dipublish release workflow, memverifikasi hash DAN ukuran paket yang diunduh, lalu men-stage-nya di `update-staging/` beserta `apply-update.cmd`. Versi dan parser diuji headless tanpa jaringan. Beda sadar dengan Sparkle: (1) tidak ada pengecekan latar belakang saat launch, karena kita tidak punya appcast bertanda tangan yang aman untuk dipercaya begitu saja, dan (2) penggantian binary terjadi lewat skrip stage, bukan in-place, karena `Compositor.App.exe` yang sedang berjalan tidak bisa menimpa dirinya sendiri. Konfirmasi berupa kalimat yang harus diketik ("Install update"), bukan checkbox, dan ditolak tanpa menulis apa pun kalau kalimatnya tidak persis. |
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
| CameraRawColorControls (495) | missing | WS19 |
| CameraRawControls (386) | missing | WS19 |
| KeyboardShortcuts (319) | missing | WS19: peta shortcut lengkap |
| CameraRawSlider (198) | missing | WS19 |
| EffectsSheet (193) | missing | WS19 |
| CameraRawDetailOpticsControls (189) | missing | WS19 |
| CanvasRulers (185) | missing | WS19 |
| TypeControls (157) | missing | WS19 |
| CameraRawGeometryCalibrationControls (147) | missing | WS19 |
| RawDevelopSheet (90) | missing | WS19 |
| PSDConversionSheet (69) | missing | WS19 |
| TrimSheet (68) | missing | WS19 |
| NumericScrub (63) | missing | WS19: drag-to-scrub angka |
| IndicatorlessScrollView (48) | n/a | Pola AppKit |

## Ringkasan

- Setelah WS10: done 14 · partial 41 · missing 32 · n/a 3 = 90 baris matriks.
- **Koreksi audit 2026-09-24, denominator berubah: 95 baris** (done 14 · partial 47 · missing 31 · n/a 3). Yang salah bukan status fitur, tapi angka sumbernya: baris "282 file Swift, 24.018 LOC" di kepala dokumen itu tercampur kloningan `Revan67/Compositor-Windows` yang hidup di `compositor/win/` (141 file Swift, 24.018 LOC). Target sejati = 92 file, 16.755 LOC. Cross-check daftar file vs nama yang disebut baris nemu 4 file / 912 LOC yang tidak pernah mewakili apa pun: ContentView (386), CompositorApp (270), EditorSession+Brush (201), EditorSession+Projects (55). Keempatnya sekarang punya baris, plus satu kapabilitas yang sebelumnya tidak tercatat sama sekali: **Auto-update (Sparkle)**, yang di Windows butuh mekanisme sendiri dan belum masuk plan. Cara ceknya (wajib diulang tiap workstream): daftarkan `find Compositor/Compositor -name '*.swift'`, potong ekstensinya, lalu cari yang namanya tidak muncul di badan matriks. Ekstensi Swift boleh pakai `+` di nama (`EditorSession+Brush`), jadi pola nama yang cuma mengizinkan alfanumerik akan melaporkan cakupan 0% yang palsu.
- Pembanding jujur: port Windows independen lain (Revan67/Compositor-Windows; C#, Avalonia 12, SkiaSharp 3.119, .NET 10; fase 1 selesai dengan 114 test; kernel C upstream dipakai ulang tanpa perubahan) memang ada dan **sengaja tidak mengikuti app Mac** serta memakai format proyek sendiri. Artinya "100% paritas" belum dicapai siapa pun, dan sisa pekerjaannya mirip: fase 3-4 mereka adalah seleksi/brush/retouch/filters, remove-background ONNX, updater, dan installer. Cara hitung (bisa direproduksi): `awk '/^## Ringkasan/{exit} {print}' docs/PARITY.md > /tmp/body.md` lalu `grep -c "| done |" /tmp/body.md` dst. Semua baris wajib pakai empat status kanonik; `n/a-ish` dulu ada satu (FloatingPanel) dan sudah dirapikan ke `n/a` supaya hitungannya tertutup.
- **Setelah WS19 (2026-09-25): 131 baris matriks**, dihitung dari 128 file Swift di app dir
  (`c64183f`) plus 3 baris non-file yang sudah ada (Auto-update, LayersPanel, dan baris ini
  tidak dihitung). 36 baris baru masuk sebagai `missing` kecuali `DocumentLimits.swift`
  yang langsung `done`. Tally per status dihitung `grep -c`, bukan angka tangan.
- Urut dependensi (workstream plan): WS2 blend engine → WS3 selection → WS4 adjustments → WS5 layer power → WS6 geometry → WS7 brush v2 → WS8 tools → WS9 filters → WS10 IO/UX → WS11 rendering perf → WS12 ML decision → WS13 release.
- Catatan jujur: SubjectRemoval/ContentFill/GuidedMatte di Mac pakai Apple Vision ML. Paritas di Windows berarti ONNX Runtime + model terbuka; keputusan arsitektur di WS12, hasilnya di-update di matriks ini.

## WS19 - Sinkronisasi upstream + limit dokumen - 2026-09-25

- **Temuan:** snapshot yang dipakai seluruh audit sampai hari ini (`a19db90`) adalah 177 commit
  di belakang `upstream/main` (`c64183f`). Bukan kesalahan status per fitur, tapi kesalahan
  **target**: 36 file Swift / 8.899 LOC tidak pernah masuk daftar periksa.
- Yang hilang itu area fitur, bukan detail kecil: PSD import/export (7 file, 1.945 LOC),
  Camera RAW (6 file, 1.783 LOC), Type tool + editor inline (3 file, 873 LOC),
  Layer Effects (4 file, 1.418 LOC), Object Selection (298), Guides + Rulers (435),
  KeyboardShortcuts (319), NumericScrub (63), TrimSheet (68).
- **Koreksi limit yang berdampak ke user:** upstream memisahkan dua ceiling yang di kode kita
  tercampur jadi satu angka. `DocumentLimits.swift` bilang `maxSide = 30_000`,
  `maxSurfacePixels = 200_000_000`, dan `documentPixelBudget = min(800_000_000, max(maxSurfacePixels, RAM/16))`.
  Kita sebelumnya memakai 100 MP untuk keduanya, jadi canvas 100-200 MP yang **sah** di Mac
  ditolak di sini. `ImageBudget` sekarang memakai semantik upstream; di .NET RAM dibaca dari
  `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` (padanan portable yang juga menghormati limit container).
- Pesan error ikut berubah karena upstream menyusunnya dari konstanta itu:
  export "up to 200 megapixels and 30,000 pixels per side", import memakai angka budget dokumen.
- 3 test di-update dari ekspektasi 100 MP ke turunan rumus; 3 test baru ditambah
  (surface 14.142^2 lolos / 14.143^2 ditolak, budget dokumen menghitung pemakaian per layer,
  budget mengikuti rumus RAM upstream). Semua diverifikasi 502 hijau.
- **Repo/fork:** `adi805/Compositor-Windows` ternyata **bukan** fork dari upstream Mac
  (parent-nya `Revan67/Compositor-Windows` pada awalnya, dan sekarang `fork=false`).
  Yang benar-benar fork dari `robbietilton/Compositor` di akun itu: nol. Upstream di-set
  sebagai remote `upstream` di working copy ini supaya `git fetch upstream` jadi cara rutin
  mendeteksi drift.

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
- DEFECT yang ketahuan lalu dibenerin di WS11: `Flatten.ToRgba` dan `SurfaceOps.CompositeStack` ignore `Layer.Transform`, dan `CanvasView` juga ignore buat layer berpixel (dia gambar ke `canvasRect` penuh; `Scaled(...)` cuma kepake buat placeholder layer kosong). Sekarang tiga-tiganya lewat satu pemetaan: `LayerPlacement.Place` (Core, inverse-map + bilinear di premultiplied colour) dan `LayerGeometry.RotationAbout` (layar, rotasi keliling pusat rect placement), dibuktiin dari dua sisi: `LayerPlacementTests` (15) dan `LayerDrawTests` (6) pakai angka hitungan tangan yang sama, jadi kanvas gak bisa bohong soal export.
- Sisa yang BELUM dari transform: (1) flip tetap di-bake dan flag-nya sengaja DIABAIKAN renderer - v0.1/v0.2 nyimpen pixel bake + flag, jadi menghormati flag tanpa migrasi bikin layer lama ke-flip dobel; ini keputusan kompatibilitas `.comp`, bukan bug kecil. (2) Alat gambar udah di-mapping ke layer space (`LayerPaintPoint` via `Mapper.ToLayer`, dibuktiin `EditorViewModelTransformPaintTests`), TAPI masih ada sisa: magic wand (milih di surface pakai koordinat dokumen) dan clip sheet adjustment (`CurrentAdjustmentClip`) belum di-mapping ke layer space. (3) `TransformOverlay` (handle drag) belum ada, itu WS11 yang paling keliatan.
- Belum ada padanannya: thumbnail 96px per asset (`ImportedImage.thumbnail`), routing drop ke tab/workspace lain (`ProjectWorkspace`), dan impor sebagai undo step (add/remove layer belum punya command type).
- Tests: 300 Core (+58) + 122 App (+29) = 422 hijau; build bersih 0 warning; `--smoke` exit 0.

## WS12 - Keputusan ML - 2026-09-25

**Keputusan: jalur ONNX diterima. Tapi ternyata cuma SATU dari tiga item yang butuh model.**

| Item upstream | Sifat aslinya | Jalur Windows | Bukti sekarang |
|---|---|---|---|
| `SubjectRemoval.swift` (110) | Apple Vision `VNGenerateForegroundInstanceMaskRequest` + 3 operasi refine (guided filter, shift edge, matte contrast) | `Microsoft.ML.OnnxRuntime` 1.30.0 + bobot U2-Net | Spike hijau di `365b057`: 4 test |
| `GuidedMatte.swift` (118) | Aritmatika murni. Komentar upstream sendiri bilang `CIGuidedFilter` Core Image tidak ngapa-ngapain di sistemnya, jadi angkanya ditulis manual | Port C# langsung, deterministik, bisa dites angka tangan | dipindah ke Core dengan 10 golden (7eb8cb3), belum dipanggil SubjectRemoval |
| `ContentFill.swift` (27) | Bukan ML: wrapper tipis kernel C `Rendering/ContentFill.c`, signature `content_fill(pixels, stride, mask, maskStride, w, h)` | Port kernel, pola persis kayak `NoisePixels`/`LensPixels` yang udah bit-exact | port kernel + menu aktif, 7 golden Core + 5 test App |

Jadi "gap ML 255 LOC" yang ditulis dokumen ini selama berhari-hari **salah besarannya**: 145 LOC dari
tiga file itu adalah matematika dan kernel C biasa. Yang beneran butuh bobot cuma 110 LOC, dan
prasyarat tampilannya (refine edges) juga bukan ML.

**Urutan yang diputuskan, dan alasannya:**

1. `GuidedMatte` dulu, di Core, tanpa dependency apa pun. Preset Advanced-nya SubjectRemoval memanggil
   dia, jadi kalau dibelakang hasilnya cuma potongan mask yang potong rambutnya kepotong model.
2. `ContentFill` kernel. Kecil, terisolasi, dan satu-satunya item yang bisa nutup gap tanpa network apa pun.
3. `SubjectRemoval` utuh: estimator mask di App (satu-satunya tempat OnnxRuntime boleh dipegang, Core tetap
   dependency-free), 4 field setting upstream yang belum ada di `FilterSettings` (kualitas basic/advanced,
   refine edges, shift edge, matte contrast), commit = alpha layer dikali mask dan dibatasi seleksi aktif,
   preview di-downscale ke limit 1400 px seperti upstream supaya drag slider tetap responsif.
4. Baru setelah layer mask ada: hasilnya dituang ke **layer mask** seperti upstream (mask lama dikalikan,
   bukan pixel dirusak). Subsystem mask masih missing sekitar 900 LOC (`LayerMask`, `LiveLayerMask`,
   `MaskTracing`, `LiveMaskRenderer`, `LayerMaskMenu`), jadi versi pertama SubjectRemoval bersifat destruktif
   terhadap alpha dan itu dicatat sebagai gap, tidak diklaim sebagai paritas.

**Provenance bobot:** `u2netp.onnx`, 4.574.861 byte, sha256 `309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8`,
diambil dari release `danielgatis/rembg`. Lisensi U2-Net asli Apache-2.0 (verified 2026-09-24 di repo
`xuebinqin/U-2-Net`, 9.843 stars; mirror ONNX di HuggingFace juga deklarasikan apache-2.0). File ikut
ke-commit, jadi repo naik dari 3,2 MB ke sekitar 8 MB; konsekuensinya disadari dan dipilih supaya test-nya
deterministik dan bisa jalan offline. **Attribution NOTICE untuk app yang shipped belum ditulis dan itu
wajib sebelum rilis.**

**Yang sengaja TIDAK diklaim:** kualitas. Test spike cuma menjawab "mekanismenya hidup atau tidak", dan itu
pertanyaan berbeda dari "mask-nya sama bagusnya sama Apple Vision". Sebelum menu Remove Background
di-enable butuhnya: (a) bandingkan hasil vs Mac di beberapa foto nyata termasuk rambut dan tepi halus,
(b) kalau u2netp kurang tajam, naik ke u2net (176 MB) atau varian matting yang lebih baru - dan kalau itu
kejadiannya bobot tidak bisa lagi di-commit, harus diunduh saat runtime dengan digest yang di-pin,
(c) ukur biaya CPU: belum pernah di-time sama sekali, dan machine user bukan joyboy.

- Tally sesudah WS12 ditulis: done 14 · partial 47 · missing 31 · n/a 3 = 95 baris (SubjectRemoval pindah dari missing ke partial karena jalurnya terbukti; GuidedMatte dan ContentFill tetap missing tapi tidak lagi dicap ML).

## WS20 - Auto-update, padanan Sparkle - 2026-09-25

- **Kontrak upstream:** `CompositorApp.swift:84` memasang `Button("Check for Updates…")` di
  `CommandGroup(after: .appInfo)`, dan `CompositorApplicationDelegate.swift:11` punya
  `SPUStandardUpdaterController` yang baru start satu detik setelah launch. Sparkle tidak ada di
  Windows, jadi yang dipindah adalah perilaku yang dilihat user, bukan framework-nya.
- Yang dibangun: `AppVersion` (bandingkan angka per komponen; prerelease kalah dari rilis versi yang
  sama, dan `0.10.0 > 0.9.0` tidak bisa dikerjakan oleh compare string), `ReleaseManifest` (membaca
  body `/releases/latest` dan file `SHA256SUMS` yang di-publish `release.yml`; aset aplikasi dipilih
  lewat sufiks `-win-x64.zip` supaya arsip sumber tidak pernah dianggap aplikasi), `UpdateChecker`
  (transportnya disuntik, jadi test tidak pernah membuka socket), dan `UpdateStager` (satu-satunya
  yang boleh menulis ke disk).
- **Tiga penolakan yang membuat fitur ini aman dipakai:** hash tidak cocok, ukuran tidak cocok
  walaupun hash cocok (paket yang terpotong di tengah jalan bisa lolos pemeriksaan hash saja), dan
  kalimat konfirmasi tidak persis sama. Semuanya terjadi sebelum satu byte pun ditulis, dan
  test-nya membandingkan isi direktori install sebelum dan sesudah percobaan.
- **Dua beda sadar dengan Sparkle, dicatat di sini supaya tidak keliru dibaca sebagai paritas
  penuh:** tidak ada pengecekan otomatis saat launch (Sparkle punya appcast bertanda tangan yang
  memang bisa dipercaya begitu saja, kita tidak), dan penggantian binary tidak in-place.
  `Compositor.App.exe` yang sedang berjalan tidak bisa menimpa dirinya sendiri, jadi paket yang
  sudah terverifikasi ditampung di `update-staging/` bersama `apply-update.cmd`. Menghapus folder itu
  membatalkan segalanya, dan tidak ada satu pun file install yang tersentuh sebelum skripnya jalan.
- Konfirmasinya kalimat yang harus diketik, bukan checkbox: menu mengirim isi text box apa adanya ke
  updater, jadi tidak ada state "sudah dicentang" yang bisa kebawa oleh perubahan berikutnya.
- 25 test Core + 19 test App baru: 457 + 199 = 656 hijau, 0 warning, smoke exit 0.
- Status matriks: baris `Auto-update (Sparkle)` pindah dari `missing` ke `partial`. Bukan `done`
  karena installer yang menukar binary tanpa langkah manual, dan tanda tangan paket, belum ada.
  Tally setelah perubahan ini: done 15 · partial 52 · missing 60 · n/a 4 = 131 baris.

## WS21 - Codec gap TIFF dan HEIC, diukur bukan diandaikan - 2026-09-25

- **Kenapa task ini ada.** README dan matriks sudah menolak TIFF dan HEIC duluan, tapi alasannya
  ditulis sebagai dugaan ("needs a licensed decoder"), dan satu-satunya test yang menjaga penolakan itu
  (`SkiaCodecTests.ImportIsRefusedForContainersTheMatrixDoesNotClaim`) suapkan header karangan sendiri ke
  `SkiaCodec.Decode`. Itu membuktikan **policy kita** menolak, bukan **codec-nya** tidak bisa. Dua hal yang
  beda, dan upstream Mac menerima keduanya lewat ImageIO, jadi ini item paritas nyata.
- **Metode.** Empat fixture 8x8, kiri merah / kanan hijau, tiap satunya ditulis alat yang bukan subjek
  uji: Pillow 12.3.0 (TIFF uncompressed), LIBTIFF 4.5.1 lewat ImageMagick (TIFF LZW, terverifikasi
  `file` menyebut `compression=LZW`), dan libheif yang dibundel pillow_heif 1.8.0 (HEIC 484 byte, frame
  tersandi 64x64 dengan `clap` crop ke 8x8, persis kamera asli). PNG 8x8 dipakai sebagai kontrol. Yang
  dipanggil langsung `SKCodec.Create` dan `SKImage.FromEncodedData`, melewati policy kita sepenuhnya.
  Karena polanya gue gambar sendiri, hasil decode dicek terhadap gambarnya, bukan terhadap output mesin.
- **Hasil terukur** (linux-x64, SkiaSharp 2.88.9, 2026-09-25):

  | fixture | byte | `SKCodec.Create` | `SKImage.FromEncodedData` |
  |---|---|---|---|
  | PNG kontrol | 79 | ok, `Png`, 8x8 | ok, kiri `(255,0,0)` kanan `(0,255,0)` |
  | TIFF uncompressed | 332 | **null** | **null** |
  | TIFF LZW | 326 | **null** | **null** |
  | HEIC (buatan sendiri) | 484 | **null** | **null** |
  | HEIC kamera 1280x854 | 718.114 | **null** | **null** |

  Kontrolnya hidup, jadi nol di baris lain adalah fakta tentang build-nya, bukan tentang harness-nya.
  HEIC kedua (foto dari repo libheif, tidak di-commit karena hak atas foto orang itu bukan keputusan gue)
  dimasukin lewat `tests/Compositor.App.Tests/bin/.../probe-fixtures/` dan hasilnya sama, jadi simpulannya
  bukan artefak encoder yang gue pakai.
- **Dua fixture bug yang ketemu di jalan, dan sekarang dikunci test.** Transkripsi base64 pertama untuk
  TIFF uncompressed menghasilkan 311 byte, bukan 332: file terpotong, dan file terpotong juga balikin
  codec null. Tanpa cek panjang, itu terbaca sebagai "TIFF tidak didukung" padahal buktinya rusak. Dan
  `Pillow` diam-diam menulis TIFF **uncompressed** padahal diminta `compression='lzw'` (dua file keluaran
  Pillow identik 332 byte); varian LZW akhirnya dibuat pakai ImageMagick. `EveryFixtureDecodesToTheLengthIt
  Documents` menjaga keduanya.
- **Bukti kedua untuk TIFF, lintas platform.** Enum `SKEncodedImageFormat` di assembly SkiaSharp 2.88.9
  punya `Heif` tapi **tidak punya anggota `Tiff` sama sekali**. Itu fakta managed assembly yang sama di
  semua platform, jadi untuk TIFF ada dua sumber independen; untuk HEIC satu measurement (linux-x64) plus
  satu korelasi file ketiga.
- **Keputusan TIFF: opsi (b), dependency, belum diambil.** Yang masuk akal dan terverifikasi ada:
  `BitMiracle.LibTiff.NET` 2.4.660, 36.819.878 downloads, dideskripsikan sebagai port libtiff ke C# murni,
  jadi tanpa native asset. Harganya bukan rupiah tapi cakupan: TIFF itu laut (multi-page, CMYK,
  JPEG-in-TIFF, floating point), dan mendukung baseline RGB none/LZW/PackBits bukan berarti mendukung
  "TIFF". `SixLabors.ImageSharp` 4.1.2 (314.553.246 downloads) dan `Magick.NET` 14.17.1 (60.928.595)
  adalah kandidat yang **belum diverifikasi kemampuannya**: deskripsi ImageSharp di NuGet tidak menyebut
  TIFF, dan ImageMagick di box ini sendiri gagal decode HEVC (`Unsupported codec`, terukur di bawah), jadi
  "tambah Magick.NET" bukan jawaban otomatis. Side effect yang harus ikut diputuskan: matriks kita juga
  `CanExport=false` untuk TIFF, dan menambah decoder tanpa encoder tidak membuat barisnya `done`.
- **Keputusan HEIC: opsi (c), gap yang dideklarasikan jujur.** Bukan cuma tidak ada di Skia: butuh
  decoder HEVC eksternal, dan tidak ada satu pun jalur di stack ini yang punya. Terukur: delegate HEIC
  ImageMagick di box ini (libheif 1.17.6) menolak **dua-duanya**, file buatan gue dan file kamera, dengan
  error yang sama. Artinya ini bukan gate yang bisa dibuka dengan menghapus satu baris policy; dan
  ditambah pertanyaan lisensi HEVC, biayanya jauh di atas nilai satu format impor untuk pre-alpha. Baris
  `ImageImporter.swift` tetap `partial`, README tetap menyebutnya, dan test pin absennya decoder yang
  memaksa keputusan dibuka lagi kalau build Skia berubah.
- **Yang belum diukur.** win-x64. CI cuma jalan di ubuntu, jadi angka di atas adalah runner CI, bukan
  platform yang kita ship. Bukti enum di atas menutupi sebagian jarak itu tapi bukan semuanya; yang
  dibutuhkan buat nutup penuh adalah satu job `windows-latest` yang menjalankan `Compositor.App.Tests`.
  Ditulis di sini supaya tidak ada yang mengira "sudah diverifikasi di Windows".
- **Tally tidak berubah oleh task ini:** tidak ada status yang berpindah (yang berubah cuma kualitas
  alasannya), tetap done 15 · partial 52 · missing 60 · n/a 4 = 131 baris. 8 test baru di
  `tests/Compositor.App.Tests/CodecProbeTests.cs`, dua cabang test opsional (direktori ada / tidak ada)
  sama-sama lolos di lokal.
