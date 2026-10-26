# Avalonia.Native macOS backend

The bundled ARM64 backend is built from Avalonia 12.1.3, upstream commit
`8eeda4f6f546165b3f72e63c9f42247abb306905`. Managed assemblies and Windows
use the official pinned NuGet packages.

`files-opened.patch` gives two file-activation COM arrays owning ComPtr values
so their references are released after callbacks. The ABI is unchanged.
MIT license, bundled binary checksum and upstream file hashes are included.

Rebuild on macOS ARM64 with Xcode and .NET 10 SDK:

```sh
python3 scripts/build-avalonia-native.py --install
```

The script downloads the exact pinned native sources and IDL, verifies Git blob
hashes against upstream-files.json, applies the patch, generates interfaces with
MicroCom 0.11.6 and builds Release ARM64. Xcode/SDK versions may change binary
bytes; SHA256.txt identifies the bundled binary.

Upstream: https://github.com/AvaloniaUI/Avalonia/tree/8eeda4f6f546165b3f72e63c9f42247abb306905/native/Avalonia.Native
