# Third-party notices

Grid Mapper's code is GPL-3.0-only. Separately licensed components retain their
copyright and license terms. Exact dependency versions and hashes are pinned
in packages.lock.json. Distribution packages include full license texts,
copyrights and notices in the Licenses folder and its INDEX.txt.

| Component | License / source |
| --- | --- |
| Avalonia 12.1.3 and ColorPicker | MIT — https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md |
| .NET 10 runtime | MIT and third-party notices — https://github.com/dotnet/runtime/blob/main/LICENSE.TXT |
| SkiaSharp 3.119.4 | MIT wrapper and native notices — https://github.com/mono/SkiaSharp/blob/main/LICENSE.txt |
| HarfBuzzSharp / HarfBuzz / ANGLE | Wrapper and native license texts included in each package |
| Noto Sans and Noto Sans Mono | SIL Open Font License 1.1 — OFL texts included beside fonts in src/GridMapper.Rendering/Assets |

The macOS Avalonia.Native backend includes a small file-activation lifetime
patch. Its pinned upstream revision, verified source hashes, MIT license, patch
and rebuild procedure are in third_party/avalonia-native. The prebuilt binary's
SHA256 is included. Windows uses the official native NuGet package.

Windows Setup includes NSIS license notices in NSIS-COPYING.txt.
NDI Runtime is not bundled and is not relicensed under GPL. The NDI logo remains
the property of its owner; see third_party/ndi/LOGO_NOTICE.md and NOTICE.txt.
NDI® is a registered trademark of Vizrt NDI AB.
