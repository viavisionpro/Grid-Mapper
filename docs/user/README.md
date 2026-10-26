# Grid Mapper

Offline LED and projection test-pattern generator by **VIAVISION — https://viavision.pro**.

## Install and remove

**Windows 10/11 x64:** run the setup executable. The installer and uninstaller
are in English. The default destination is `C:\Program Files\Grid Mapper`;
administrator permission is required. Open Grid Mapper from the Start menu.
Close the application before uninstalling it through Installed apps. Before
installing a replacement version, uninstall the existing installation.
User-added projects and PNG files are preserved; keep them in Documents.
The portable ZIP can instead be extracted to a writable folder and removed
by deleting that folder. Keep all portable files together.

**macOS Apple Silicon:** open the DMG, drag **Grid Mapper.app** to Applications,
then eject the disk. For an update, quit Grid Mapper and replace the existing
Grid Mapper.app; keep projects separately. A ZIP is also available. Remove the
application using Finder. Projects and user settings are unaffected.

**First launch:** this release has a local ad-hoc integrity signature, not an
Apple Developer ID signature, and is not notarized. After trying to open it,
open **System Settings → Privacy & Security → Open Anyway**, then confirm
**Open**. This is a one-time approval for that installed copy; normal subsequent
launches need no Terminal command. A new download/update may need approval again.
If Open Anyway is unavailable, do not disable Gatekeeper: check whether your Mac
is managed by an organisation and contact its administrator or the publisher.
The app does not remove quarantine or change system security settings.
[Apple's instructions](https://support.apple.com/102445).

## Use

- Add a screen, choose panel dimensions, pixel resolution, columns and rows.
- Parameters apply as you type. Enter or Tab evaluates arithmetic expressions.
- The mouse wheel or a pinch gesture zooms (10–200%). Middle-drag, or hold
  Space and drag, to pan the workspace in any mode.
- Canvas supports multiple screens, snapping and layer ordering. Single
  screen has its own viewport. The eraser toggles panel masks. Inspector and information-block counts show
  active LED panels only; masked panels retain their geometry.
- Save projects as `.gpm`. Imported logos are embedded in the project.
- Export a screen or the Canvas as PNG. Canvas export follows its bounds.
- Settings selects the interface language and NDI frame rate (50/60 fps).
  Russian and English are included; additional interface translations go
  in Localizations. Publisher information in About is not editable there.
- Help → Readme opens this document. A Russian version is `README.ru.md`.

## Canvas reference image

In Canvas mode, the reference-image mask button is beside the eraser. Enable it,
click **Load**, and choose a static PNG, JPG/JPEG or WebP image: **up to 8 MiB and
16384 × 16384 pixels**. Animated images are rejected. Alpha is supported.

Drag the reference to move it. Its four corner handles resize it with the original
aspect ratio locked; the turquoise frame identifies it above all screens.
**Transparency** ranges from 0% (original image opacity) to 100% (image invisible;
frame remains visible). Undo/Redo covers its edits. Turn the tool off to work
freely with screens; turning it on again restores its image, position and size.
When deselected, the interior is click-through over screens; click its coloured
frame to select it again. The eraser always edits panels through the reference.

The padlock beside the transparency slider locks the reference's size and
position. Locked references pass left clicks/drags through to screens and show
neither a border nor resize handles; transparency remains editable. Source replacement requires
unlocking. Right-click on the reference offers **Lock mask / Unlock mask** only,
with no layer-order actions. The lock is saved in the project and supports Undo.

The magnet also controls reference snapping to the edges and centres of visible
screens and the Canvas. Pink guides show alignment during movement and corner
resizing. Resizing preserves the opposite corner and image aspect ratio; turn
the magnet off for free placement.

The original image and transform are embedded in `.gpm`; moving a project does
not require its original image file. The reference is never included in screen
or Canvas PNG exports, nor in NDI. Large images use a bounded editor preview.

## NDI®

Each screen can send its own NDI output at its native resolution. A separately
installed NDI Runtime is required. Editing and PNG export do not need network
access; NDI transmission uses the local network. Settings displays the version
and path of the runtime actually loaded. NDI Runtime is not bundled.
Information and runtime downloads: https://ndi.video/ and https://ndi.video/tools/.

### Updating NDI without updating Grid Mapper

1. In **Settings → NDI**, note the current runtime version and loaded library
   path. Obtain a compatible official NDI Runtime for Windows x64 or macOS ARM64.
2. **Close Grid Mapper completely**, including all NDI outputs.
3. Choose one method:
   - **System installation:** install/update the official NDI Runtime using its
     installer. An existing local override takes priority; remove that override
     first if you want to use the system runtime.
   - **Application-local override:** copy the official library and its supplied
     dependencies into the application's `NDI` folder. On Windows the library is
     `NDI/Processing.NDI.Lib.x64.dll`, beside `GridMapper.Desktop.exe`.
     On macOS, use Finder → Go → Go to Folder, create the NDI folder if needed, and use
     `~/Library/Application Support/GridMapper/NDI/libndi.dylib`. Do not put NDI files in `Runtime`.
4. Restart Grid Mapper. Enable one screen's NDI output, then check **Settings →
   NDI** again: the actual version and path should match the intended library.
5. To roll back a local override, close Grid Mapper, remove the library and the
   dependencies you added from `NDI`, then restart to use the system runtime.

Program Files and Applications may require administrator permission. Do not
replace libraries while Grid Mapper is running. Replacing files is suitable
for compatible runtimes; a future incompatible NDI API may require a Grid
Mapper update. Never overwrite Grid Mapper's own .NET/graphics libraries.

NDI® is a registered trademark of Vizrt NDI AB.

## Application folders

On Windows, the application folder contains the launcher
`GridMapper.Desktop.exe`, README files, copyright/license notices, and:

- `Runtime` — the self-contained .NET runtime, application and graphics libraries.
- `Localizations` — editable interface translation files and their instructions.
- `NDI` — optional user-supplied NDI Runtime override (see above).
- `Licenses` — complete third-party license texts and notices.
- `Metadata` — package checksums and, after installation, the installation record.

An installed Windows copy also has `Uninstall.exe` and `NSIS-COPYING.txt`.
Keep these folders with the launcher; moving only the executable will not work.
The Windows uninstaller preserves files you add, including your NDI library.

On macOS, Finder shows a single `.app`. Its launcher is in `Contents/MacOS`;
README files, Runtime, bundled translations, notices, metadata and the icon are
in `Contents/Resources`. Keep the standard bundle structure intact. Store projects
and exported images in Documents, outside either installation.

## License and components

Copyright (C) 2026 VIAVISION. Grid Mapper code is licensed under the
GNU General Public License version 3 only (GPL-3.0-only); see `LICENSE`.
Sources and build instructions must accompany redistribution as required by
that license. Separately licensed third-party components retain their terms.
The optional proprietary NDI Runtime and NDI logo are not relicensed under GPL;
NDI Runtime is installed separately and retains its own license terms.

Avalonia/.NET and wrappers, native Skia/HarfBuzz/ANGLE, and the Noto Sans / Noto
Sans Mono fonts are used. Full notices, copyrights and license texts are in
`Licenses` (inside `Contents/Resources` on macOS). Fonts use SIL OFL 1.1. The Windows
installer also includes NSIS notices in `NSIS-COPYING.txt`. About shows a short
component list and links to the notices.

## LED and Projection

The header switch selects the working mode. Each mode has a separate screen
collection in the same `.gpm` project. Switching restores its list and selection;
adding or deleting in one mode does not affect the other. New screen names are
LED Screen 1 / Projection Screen 1.

In LED, enter panel dimensions in mm and pixels, then the number of columns and
rows. Preset buttons beside the height fields offer compatible panel sizes and
pixel pitches. One screen uses one panel type. Use Canvas to combine screens
with different panel types, position them, reorder layers and snap edges/centres.
Canvas starts at 3840 × 2160 px; its size fields are beside the mode switch.
Coordinates start at the top-left corner. Workspace navigation is free, while
Canvas PNG output is cropped to those bounds.

In Projection, enter Width px / Height px, then physical Width m. Height m is
derived from output aspect ratio: 1920 × 1080 px and 10 m width give 5.625 m.
Resolution presets: FHD, WUXGA, 4K UHD, WQUXGA and 4K DCI.

Grid scale is the real size of a square cell. The pattern is centred and clipped
at the image edges. The Grid checkbox switches lines, IDs and checkerboard
together, retaining individual preferences. Checkerboard starts off.
Circles starts with one ring spanning the image height; the slider sets 1–10
touching rows. Columns repeat sideways and clip at the left/right edges.
Frame starts on in pink, with inward short ticks every 0.1 m and long ticks
every 1 m from the centre. The information block shows metres, resolution, cell
and ring sizes on an 80% opaque black plate.

Projection has a transparent background and a single-image workspace, without
Canvas positioning, eraser, reference image or snapping. PNG and NDI use the
selected pixel resolution. Middle-drag or Space + left-drag pans every mode;
Fit restores the overview. Navigation does not change source geometry or exports.

## Resolume Arena XML

In LED Canvas, **Resolume XML…** and the File menu export an Advanced Output
preset: one virtual output at Canvas resolution, each visible screen a Slice
with the same name, size and input/output coordinates. Set the Arena composition
to the Canvas resolution, load the XML Advanced Output preset, then assign the
physical output. Export/load the Canvas PNG separately. Erased panels affect
the PNG, not rectangular Slice geometry; the reference image is excluded.

On macOS, keep the sealed .app unchanged. Custom translations go in
`~/Library/Application Support/GridMapper/Localizations`; Settings → Open language
folder creates templates there without overwriting edits. NDI overrides use the
sibling `NDI` folder. These customizations survive application replacement.
