# Building Grid Mapper

The source snapshot corresponds to public release v.0.1.1 (package version 0.1.1).
Use the .NET 10 SDK selected by global.json. Dependencies are pinned in the
checked-in packages.lock.json files. Internet access is needed for the first
restore; the installed application is self-contained.

```sh
dotnet restore GridMapper.slnx --locked-mode
dotnet build GridMapper.slnx -c Release --no-restore
dotnet run --project src/GridMapper.Desktop -c Release
```

Publish the desired target:

```sh
dotnet publish src/GridMapper.Desktop -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
dotnet publish src/GridMapper.Desktop -c Release -r osx-arm64 --self-contained true -o artifacts/publish/osx-arm64
```

Packaging uses Python 3 and a Bash environment. Run from the repository root:

```sh
python3 scripts/package-app.py --rid win-x64
python3 scripts/package-app.py --rid osx-arm64
```

The scripts construct the runtime/resource folders and collect dependency
licenses from the restored NuGet packages. Do not distribute a single EXE or
omit license files. Output is in artifacts/packages. On macOS, archive and DMG
creation use the system ditto/hdiutil tools:

```sh
bash scripts/package-macos-dmg.sh
```

For the Windows installer, install NSIS 3.13 and provide its compiler if it is
not found automatically:

```sh
python3 scripts/package-windows-installer.py --makensis /path/to/makensis
```

The scripts preserve existing versioned packages; use a fresh checkout/artifacts
folder for a rebuild. They do not supply a publisher certificate or Apple
notarization. The bundled macOS backend's corresponding source, pinned upstream
revision, patch and rebuild instructions are in
[third_party/avalonia-native](third_party/avalonia-native/README.md).

NDI Runtime is optional, proprietary and not bundled. The application uses an
ABI interop layer; installing a compatible runtime is a separate user action.
All separately licensed fonts/native components retain their notices.

macOS packaging uses `artifacts/packages/0.1.1/Grid Mapper.app`; the bundle name
is stable and the versioned parent preserves earlier local packages. Native
libraries and bundle resources receive an ad-hoc integrity seal, not a Developer
ID signature or notarization. `codesign --verify --deep --strict` validates it.
Bundled runtime/resources are in `Contents/Resources`, and the launcher alone is
in `Contents/MacOS`. User translations and NDI overrides live in Application
Support/GridMapper outside the seal.
