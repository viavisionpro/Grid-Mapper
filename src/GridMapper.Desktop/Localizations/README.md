# Grid Mapper translations

Each UTF-8 `.json` file in this folder defines one language. `En-en.json` is the English reference; `Ru-ru.json` supplies Russian.

To add a language, copy `En-en.json` to a new file such as `Fr-fr.json`, change `id` to `Fr-fr`, set `displayName` to the name to show in Settings, and translate the values inside `strings`. Keep the keys unchanged. The filename and `id` must match, ignoring case. The ID must contain language and region segments, separated by a hyphen.

```json
{
  "id": "Fr-fr",
  "displayName": "Français",
  "strings": {
    "settings.title": "Réglages",
    "settings.language": "Langue"
  }
}
```

Reopen Settings to discover the file; no rebuild or restart is needed. Partial translations are allowed: missing or invalid entries fall back to English. Keep numbered placeholders (`{0}`, `{1}`, etc.), their format specifiers, and escaped line breaks (`\n`). You may reorder placeholders. JSON does not support comments.

Malformed JSON, mismatched IDs, files larger than 1 MiB, and invalid format entries are ignored safely. The built-in English and Russian resources remain available if the external folder is missing. English is the first-launch default regardless of the operating-system language; subsequent launches restore the user's selection.

Use **Open language folder** in Settings to find this folder. On macOS custom translations are in `~/Library/Application Support/GridMapper/Localizations` (the button creates templates without overwriting existing edits); on Windows it is beside `GridMapper.Desktop.exe`. Settings are saved separately in the current user's application-data directory under `GridMapper/settings.json`, never in a project file.

UI translations do not rename existing screens or alter custom captions, logos, project files, exported patterns, or NDI source images. New screens are named in the selected language. NDI, Canvas, PNG, and the NDI trademark notice remain product/format names.

About Grid Mapper uses publisher-controlled Russian/English text compiled into the application. Additional languages use English for that window; JSON overrides do not replace its content.
