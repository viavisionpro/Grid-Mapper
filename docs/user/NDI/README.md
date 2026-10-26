# Optional NDI Runtime override

Grid Mapper does not include NDI Runtime. To use a compatible version instead
of a system installation, close Grid Mapper and use the appropriate location:

- Windows x64: `Processing.NDI.Lib.x64.dll` in this folder.
- macOS ARM64: `~/Library/Application Support/GridMapper/NDI/libndi.dylib`.
  Create that folder if needed. Do not put libraries inside the sealed .app.

Keep the dependencies supplied by the runtime vendor. Restart Grid Mapper and
check the loaded version and path in Settings. Remove your local library to
return to the system runtime. See `../README.md` or `../README.ru.md` for steps.

This folder and any runtime files you add are preserved by the Windows
uninstaller, except for this installed README. No NDI binaries are redistributed.
