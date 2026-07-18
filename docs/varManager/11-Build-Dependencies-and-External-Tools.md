# 11 — Build, Dependencies & External Tools

## Target frameworks & platforms

| Project | TFM | Platform | Output |
|---|---|---|---|
| varManager | .NET Framework 4.8 | x64 | WinExe |
| MMDLoader | net6.0-windows | AnyCPU | WinExe (WPF) |
| LoadScene | .NET Framework 3.5 | AnyCPU | Library (Unity plugin) |
| DgvFilterPopup / DragNDrop | .NET Framework 4.8 | AnyCPU | Library |

- varManager sets **`AllowUnsafeBlocks`** (for the P/Invoke / marshaling in `Comm.cs`) and ships an `app.manifest`.
- LoadScene targets .NET 3.5 because it runs inside VaM's Unity/Mono runtime (VaM 1.20.77.9).

## NuGet / library dependencies

**varManager**
- `SharpZipLib` **1.4.2** — managed ZIP read/write + encoding-aware extraction.
- Project refs: `DgvFilterPopup`, `DragNDrop`.
- Vendored source: `SimpleJSON` (JSON), `SimpleLogger`.
- Framework refs: `System.Windows.Forms`, `System.Data` (DataSet/OLEDB), `System.Net.Http` (hub), `Microsoft.VisualBasic` (`FileSystem` recycle-bin, in commented code).

**MMDLoader**
- No NuGet declared; framework WPF + WinForms interop (`FolderBrowserDialog`, `MediaPlayer`).
- Vendored `SimpleJSON`.

**LoadScene**
- `ILMerge` **3.0.29** + `MSBuild.ILMerge.Task` **1.1.3** — merge bundled libs into one plugin DLL.
- `JSON` **1.0.1** (SimpleJSON).
- Bundled **LibMMD** (~40 source files: PMX/VMD readers, poser, motion/camera models, SJIS decoding).
- VaM/Unity refs from `F:\VaM_1.20.77.9\VaM_Data\Managed\`: `Assembly-CSharp.dll`, `UnityEngine*.dll`.

## External runtime dependencies (not shipped)

⚠ These are hard requirements the app assumes exist:

1. **7-Zip** at `C:\Program Files\7-Zip\7z.exe` (hardcoded) — used by `ZipHandler.ExtractAndRezip` / the fixVar action. No PATH fallback, no existence check. ✎ Rebuild: discover the binary, or avoid it entirely for metadata reads via `System.IO.Compression`.
2. **Microsoft Access Database Engine** (ACE OLEDB 12.0 provider) — required to open `varManager.mdb`. A common deployment friction point. ✎ Rebuild: SQLite removes this dependency.
3. **Windows Developer Mode or admin elevation** — required to create symbolic links.
4. **VaM install** with `VaM.exe`, and for the MMD feature: `Custom/Scripts/g2f.pmx`, `AcidBubbles.Timeline`, `AcidBubbles.Embody`, and pre-existing Person/AudioSource/WindowCamera atoms in the open scene.
5. Optional external batch downloader ("Chrono" browser extension) — the hub feature only generates URL lists.

## Data files shipped with varManager
- `varManager.mdb` (Access DB, copied to output `PreserveNewest`).
- `varManagerDataSet.xsd/.xsc/.xss` (typed DataSet schema).
- `varManager.db` (an empty/staged **SQLite** file — evidence of the planned migration).
- `vam.png` (preview placeholder), `VarManager.ico`, and `meta.json` (the app's own distribution package meta, so varManager can itself be shipped as a VaM-style package).

## Build notes for the rebuild
- The legacy multi-project split (WinForms app + control libs) collapses to a clean-architecture solution (Core/Application/Infrastructure/Presentation) — see [12](./12-Rebuild-Blueprint.md).
- **Keep LoadScene essentially as-is** — it is bound to VaM's Mono runtime and the `loadscene.json` contract; only its producer side (MMDLoader) and the varManager app need modernizing.
- Remove the SqlClient EF provider (never used); target SQLite only.
- Replace ILMerge with normal packaging for any non-Unity assembly; ILMerge remains relevant only for the in-VaM plugin.

Continue to [12 — Rebuild Blueprint](./12-Rebuild-Blueprint.md).
