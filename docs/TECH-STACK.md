# Tech Stack

Terverifikasi 2026-09-24 terhadap NuGet registry + nuspec + XML dokumentasi di
package cache lokal. Bukan ingatan, bukan asumsi: tiap nomor di bawah punya bukti.

## Terpasang sekarang

| Komponen | Versi | Bukti |
|---|---|---|
| .NET SDK | 8.0.131 | `dotnet --list-sdks`, `/usr/lib/dotnet/sdk` |
| TargetFramework | net8.0 | `Directory.Build.props` (semua proyek, analyzer `latest-recommended` + `TreatWarningsAsErrors`) |
| Avalonia (App + ColorPicker + Desktop + Themes.Fluent) | 11.3.22 | `dotnet build` sukses, 0 warning |
| Avalonia.Headless.XUnit | 11.3.22 | 83 test App hijau |
| SkiaSharp (transitif, via Avalonia.Skia) | 2.88.9 | `dotnet list package --include-transitive` |
| xunit / runner / Microsoft.NET.Test.Sdk | 2.9.3 / 3.1.5 / 17.14.1 | 284 test hijau |
| CI | ubuntu-latest, `setup-dotnet@v4` dengan 8.0.x | `.github/workflows/ci.yml` |
| Rilis | `dotnet publish -r win-x64 --self-contained` → zip + sha256 | `.github/workflows/release.yml` |

Sebelum hari ini kami di Avalonia **11.2.7** dan xunit **2.7.0**. Naik ke 11.3.22
karena satu alasan konkret yang bisa diukur, bukan karena "versi baru":

**11.2.7 tidak punya API drag-drop yang didokumentasikan.** Diambil dari
`Avalonia.Base.xml` tiap versi:

| Anggota | 11.2.7 | 11.3.22 | 12.1.3 |
|---|---|---|---|
| `DragDrop.SetAllowDrop` / `GetAllowDrop` / `DoDragDrop` | ada | ada | ada |
| `DragDrop.AddDropHandler` / `AddDragOverHandler` | tidak | ada | ada |
| `DragDrop.DoDragDropAsync` | tidak | ada | ada |
| `IDataTransfer` / `DataTransferItem` / `TryGetFiles` | tidak | ada | ada |
| `IDataObject` / `DataObject` (API lama) | ada | ada | masih ada |

Task 10 (drop file dari Explorer ke jendela) butuh `TryGetFiles`. Di 11.2.7 itu
tidak ada, jadi satu-satunya jalurnya adalah API `DataObject` lama yang dokumen
resminya sudah tidak membahasnya sama sekali. 11.3.22 memperbaikinya tanpa
memaksa kami naik major.

## Jebakan yang harus diketahui siapa pun yang baca docs ini

Dokumentasi Avalonia yang ke-index context7 (`/avaloniaui/avalonia-docs`, branch
`main`, 3,96 juta token, terakhir di-update 2026-09-16) itu **dokumentasi 12.x**.
Contoh nyata dari halaman `drag-and-drop.md`:

```csharp
// Ini TIDAK compile di 11.2.7. Di 11.3.22 dan 12.x: ya.
if (e.DataTransfer.Formats.Contains(DataFormat.File))
{
    var files = e.DataTransfer.TryGetFiles();
}
```

Aturan mainnya: jangan salin snippet dari docs tanpa memastikan dia milik versi
yang kita pin. Cara cek yang terbukti works, dan tidak perlu install apa pun:

```bash
curl -s -o pkg.zip https://api.nuget.org/v3-flatcontainer/avalonia/<VERSI>/avalonia.<VERSI>.nupkg
unzip -p pkg.zip lib/net8.0/Avalonia.Base.xml \
  | grep -oE 'M:Avalonia\.Input\.DragDrop\.[A-Za-z]+' | sort -u
```

## Yang sudah tersedia tapi belum dipakai (paling penting buat sisa grind)

`SkiaSharp 2.88.9` **sudah** ada di dependency tree (bawaan `Avalonia.Skia`), dan
method-nya terverifikasi ada di `SkiaSharp.xml`:

| API | Untuk | Hemat |
|---|---|---|
| `SKImage.Encode(SKEncodedImageFormat, int)` | Task 10: ekspor JPEG | encoder JPEG manual (DCT + Huffman) tidak perlu ditulis |
| `SKBitmap.Resize(SKImageInfo, SKFilterQuality)` | Task 11: downsample cache | resize berkualitas per-bandwidth sendiri |
| `SKImageFilter.CreateBlur(sx, sy, tileMode, ...)` | Task 11: pratinjau blur di GPU | blur besar di CPU bakal lambat |

Rencana pemakaiannya: **hanya di lapisan App**, jangan di Core. Alasannya
eksplisit: Core saat ini dependency-free, deterministik, dan 201 test-nya jalan
tanpa native library apa pun. Menyeret SkiaSharp ke Core bikin test Core butuh
`SkiaSharp.NativeAssets`, dan itu mengorbankan sifat yang paling kita hargai di
proyek ini: angka yang keluar dari test bisa dipercaya dan bisa diulang di mesin
mana pun. App.Tests sudah punya native Skia (lewat Avalonia.Headless), jadi di
sananya gratis.

Catatan versi: `SKSamplingOptions` **tidak ada** di 2.88.9 (0 hit di XML-nya),
API itu baru masuk SkiaSharp 3.x. Kalau Task 11 ternyata butuh sampling control
yang halus, itu pemicu sah untuk naik ke Avalonia 12.

## Belum dipakai, sudah dievaluasi

**Avalonia 12.1.3** (stabil terbaru, rilis 2026-09-22). Bisa jalan di net8.0:
nupkg-nya berisi `lib/net8.0` **dan** `lib/net10.0`, dan `Avalonia.Skia 12.1.3`
mem-pin `SkiaSharp 3.119.4`. Tapi `Avalonia12-breaking-changes.md` minta
`AttachDevTools()` → `AttachDeveloperTools()` dan pergantian paket Diagnostics,
dan pindah Skia 2.88 → 3.x mengubah API render. Tidak ada satu pun task tersisa
yang butuh itu. Jadi: **tunda ke task khusus setelah matriks paritas hijau.**
Naik major di tengah grind 5 task terakhir = membeli risiko regresi tanpa
membeli fitur.

**Microsoft.ML.OnnxRuntime 1.30.0** (stabil terbaru). Ini kandidat jawaban
Task 12: upstream Mac pakai Apple Vision untuk Remove Background dan
Content-Aware Fill, dan itu `n/a` di Windows. Runtime-nya cross-platform dan
net8-compatible, jadi gap-nya tinggal milih model-nya, bukan toolingnya.
Keputusan model = bagian dari Task 12, bukan di sini.

## Deadline nyata: net8 habis masa dukung 2026-11-10

Dari `releases-index.json` Microsoft (diambil 2026-09-24):

| Channel | Rilis terbaru | Fase | EOL |
|---|---|---|---|
| 11.0 | 11.0.0-rc.1 | go-live | - |
| 10.0 | 10.0.12 | active (LTS) | 2028-11-14 |
| 9.0 | 9.0.20 | maintenance | 2026-11-10 |
| 8.0 | 8.0.31 | maintenance | **2026-11-10** |

Aplikasi kami tetap *jalan* setelah 2026-11-10 (rilisnya self-contained, runtime
ikut ke-bundle), tapi tidak dapat patch keamanan lagi. Untuk pre-alpha tidak
fatal; buat v1.0 yang lo distribusikan ke orang lain, itu cacat. Konsekuensinya
bersih: **migrasi `net8.0` → `net10.0` (LTS, dukung sampai 2028-11-14) harus
masuk sebelum Task 13**, bukan sesudahnya. SDK-nya `Microsoft.NET.Sdk` saja,
perubahan kecil, dan tidak menyentuh paritas.

## Verifikasi perubahan hari ini

Semua dijalankan lokal di joyboy sebelum sentuh CI, dengan `obj/` dan `bin/`
dihapus dulu (build inkremental tidak menjalankan ulang analyzer, dan itu sudah
dua kali bikin CI merah mendadak):

```
dotnet build -c Release                     →succes, 0 Warning(s), 0 Error(s), 75 detik
dotnet test tests/Compositor.Core.Tests     →Passed! Failed: 0, Passed: 201
dotnet test tests/Compositor.App.Tests      →Passed! Failed: 0, Passed: 83
dotnet run -- --smoke                       →"doc 64x64, layers=1, round-trip OK", exit 0
```

284/284 hijau, termasuk test headless yang menempuk `ICustomHitTest` dan
rendering WriteableBitmap. Itu justru bagian paling berisiko dari bump ini
(hit-test Avalonia pernah bikin kami jatuh sebelumnya), dan dia terbukti aman
di 11.3.22.
