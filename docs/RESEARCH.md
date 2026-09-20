# Research: upstream specs and our design decisions

_Date: 2026-09-20. Sources read in full; every claim below cites its source file._

## Sources

| Source | Path | What it gave us |
|---|---|---|
| Mac spec (authoritative format) | `robbietilton/Compositor` `docs/project-format.md` (27 lines, v1-6 complete) | `.comp` layout, manifest fields, blend list, mask rules, validation limits |
| Windows port plan (prior art) | `Revan67/Compositor-Windows` `docs/windows-port-plan.md` (218 lines) | zip-based `.comp` decision, Avalonia/Skia stack rationale, phase order, verification method |
| Brush performance | `robbietilton/Compositor` `docs/brush-performance.md` | tile/snapshot model for strokes, undo-on-mouse-up |

## Facts extracted from the sources

### `.comp` format (Mac spec v1-6)

- A `.comp` is a document package: `manifest.json` plus `images/<layer UUID>.png` assets. [project-format.md L3]
- Manifest identifies `com.compositor.project`, version 6 for new saves (1-5 readable), sRGB working space; stores document UUID, pixel dimensions, active layer UUID, layers bottom-to-top. [L5-7]
- Layer record: UUID, name, visibility, transform (origin, size, clockwise rotation, flips, sampling), optional image filename; blank layers have no asset. [L7]
- Version additions: v2 `parentID`/`isGroup`; v3 `opacity` (0-1) + `blendMode` (Normal, Multiply, Screen, Overlay, Darken, Lighten, Difference, Color Dodge, Color Burn); v4 `maskFile`/`maskEnabled` (8-bit grayscale, white reveals); v5 `maskSourceID` (clipping masks); v6 folder masks. [L19-41]
- Validation limits: 30,000 px per side, 100 M total source pixels (+100 M mask px), 10,000 layers, 4 MiB manifest, 512 MiB per asset; rejects unsupported versions, invalid metadata, missing assets, unsafe paths before replacing the live document. [L11]
- Undo history and viewport are session-only, never serialized. [L13]
- "Future editable features must extend the schema and round-trip tests." [L14]

### Port-plan decisions we inherit (with our own caveats)

- `.comp` on Windows as a **single zip**: stored (not deflated), sibling temp file + atomic replace; schema **starts from the Mac v6 layout** but as a new format (`com.compositor.windows.project`, version restarts at 1); no reader for Mac packages. [windows-port-plan.md "The .comp format on Windows"]
- Validation limits carried over because they are sensible. [same section]
- Additive evolution: bump version for new fields; reader rejects unknown newer versions. [same section]
- Verification style: fixtures hand-authored from the spec; reference test assertions as the oracle; every rejection case becomes a test. [windows-port-plan.md "Verification"]
- Brush model: continuous soft tip, per-256-tile processing, immutable `RasterSnapshot` on mouse-up with shared unchanged tiles, undo entry recorded synchronously; opacity caps the whole stroke. [brush-performance.md L3-9]
- Prior-art pitfalls worth copying: pin one SkiaSharp version across the solution *before* UI work; `Compositor.Core` must not reference the UI framework (enforced by test); channel-order (RGBA vs BGRA) chosen once and audited. [windows-port-plan.md Phase 0 exit + Risks]

## Our design decisions (Phase 1 scope)

Our Phase 0 is deliberately smaller than the prior art's: pure C# on net8.0, no C kernels yet, no Avalonia yet. Phase 1 here = project file I/O with round-trip tests.

1. **Container: single zip `.comp`, entries stored uncompressed.** `manifest.json` at root, layer images under `images/<uuid>.png`. Write to sibling temp, validate, then atomic replace. (Source: port-plan format section; adapted to `File.Move` semantics.)
2. **Manifest identity: `com.compositor.windows.project`, format version 1.** Reader rejects unknown *newer* versions and wrong identifiers. We do not read Mac packages.
3. **Schema for v1 = Mac v6 subset, additive-only from here:**
   - Document: `documentUUID`, `width`, `height`, `activeLayerUUID`, `layers` (bottom-to-top array).
   - Layer: `uuid`, `name`, `isVisible`, `transform {originX, originY, width, height, rotationDegrees, flipH, flipV}`, optional `image` filename, `opacity` (default 1), `blendMode` (default `normal`).
   - **Deferred to later additive versions** (schema leaves room; nothing invalid in v1): groups/`parentID`/`isGroup` (Mac v2), raster masks (v4), clipping masks (v5), folder masks (v6), resolution (Mac additive field).
   - Blend mode string set for v1: exactly the nine from Mac v3, lower-cased (`normal`, `multiply`, `screen`, `overlay`, `darken`, `lighten`, `difference`, `colorDodge`, `colorBurn`); our `BlendMode` enum maps 1:1. [BlendMode.cs, project-format.md v3 paragraph]
4. **Validation before live-document replacement**, carried from the spec: dimension and pixel-count limits (30k/side, 100 M px), 10k layers, 4 MiB manifest, 512 MiB per asset, entry names must be relative and free of `..`, image files referenced must exist, unreferenced image entries are tolerated on read but never written by us. Every rejection case gets a test.
5. **PNG layer assets**: 8-bit RGBA PNG (sRGB). Writer encodes from `RasterSurface` pixels; reader decodes to `RasterSurface`. No DPR/resolution metadata in v1.
6. **Undo/viewport never serialized**; a fresh open starts with clean history. [project-format.md L13]
7. **Round-trip contract**: build document in memory -> save -> load -> assert deep equality (dimensions, active layer, per-layer fields, pixels) -> save again -> byte-identical manifest (images may differ in compression metadata but not pixels). This is the test the spec itself calls for. [project-format.md L14]
8. **Stack choice stays put for now**: net8.0 pure C# (System.IO.Compression for zip; no Skia dependency in Core yet). Raster work in Phase 3 re-evaluates SkiaSharp vs System.Drawing-free manual encode; the port-plan's "pin codec versions early" lesson is noted for that moment.

## Open questions carried forward

- PNG encode/decode without Skia on net8.0: options are `System.Drawing.Common` (Windows-only runtime, fine for CI but not cross-platform), ImageSharp (license: Six Labors Split License, free tier ok), or SkiaSharp now. Decision lands at the start of Phase 1 implementation, not here.
- Rotation semantics: Mac spec says "clockwise rotation" in degrees; our `Layer` currently has no rotation field. v1 manifest includes it (default 0) so the schema does not need a bump when rendering lands.
