# SnapActions

A free, open-source alternative to [SnipDo](https://snipdo-app.com/). Select text anywhere on Windows and get instant context-aware actions — no limits, no subscription.

![.NET 9](https://img.shields.io/badge/.NET-9.0-purple) ![WPF](https://img.shields.io/badge/WPF-Windows-blue) ![License](https://img.shields.io/badge/License-MIT-green)

## Download

**[Download SnapActions.exe](https://github.com/roko-tech/SnapActions/releases/latest)** — single file, no installation needed.

> Requires Windows 10 version 19041 or higher.

## Features

### Text Selection Toolbar
- **Drag-select**, **double-click** (word), or **triple-click** (line) text anywhere
- A floating toolbar appears above the selection with smart actions
- Hover over actions to preview the result before applying
- Toolbar stays open while your mouse is over it

### Smart Text Detection
Automatically detects what you selected and shows relevant actions:

| Type | Example | Actions |
|------|---------|---------|
| URL | `https://example.com` | Open in browser |
| Email | `user@test.com` | Send email |
| File path | `C:\folder\file.txt` | Open file, open folder |
| JSON | `{"key": "val"}` | Format, minify |
| XML/HTML | `<div>text</div>` | Format, strip tags |
| Math | `2+3*4` | Calculate (= 14) |
| IP address | `192.168.1.1` | IP lookup |
| Color code | `#89B4FA` | Preview, convert to RGB |
| UUID | `550e8400-e29b-...` | Generate new UUID |
| Base64 | `SGVsbG8gV29ybGQ=` | Decode |
| Date/Time | `2026-04-11T12:00` | Convert timezone, Unix timestamp |

### Text Transforms (in editable fields)
UPPERCASE, lowercase, Title Case, camelCase, PascalCase, snake_case, kebab-case, Reverse, Trim, Remove Extra Spaces, Sort Lines, Remove Duplicates, Wrap in quotes/brackets/braces/backticks

### Encode/Decode
URL encode/decode, Base64 encode/decode, HTML encode/decode

### Search Engines
13 built-in engines: Google, Bing, DuckDuckGo, YouTube, Twitter/X, Reddit, GitHub, StackOverflow, Wikipedia, Amazon, IMDb, npm, NuGet

- **Language filtering** — filter results by language (13+ languages supported)
- **Twitter/X** uses `lang:xx` in the search query so it works across Top/Latest tabs
- **Wikipedia** switches subdomain by language (en.wikipedia.org, ar.wikipedia.org, etc.)
- **Add custom search engines** with URL templates (`{0}` = query, `{1}` = language)

### Paste Mode
- **Long-press** (hold 500ms) in any text input to get a paste toolbar
- Hover the paste button to see transform options (paste as UPPERCASE, camelCase, etc.)
- Only appears in editable fields (text inputs, rich text editors, code editors)

### Pin & Customize
- Click the **gear icon** in any expanded sub-menu to enter edit mode
- **Left-click** an action to show/hide it
- **Right-click** an action to pin it to the main toolbar
- **Arrow buttons** to reorder actions
- **Right-click** pinned toolbar buttons to rearrange or unpin

### Settings
Double-click the system tray icon to open Settings:

| Setting | Options | Default |
|---------|---------|---------|
| Show delay | Instant, 100ms, 200ms, 300ms, 500ms, 1s | Instant |
| Auto-dismiss | 3s, 5s, 8s, 15s, 30s, Never | 8 seconds |
| Replace on transform | On/Off | On |
| Search language | 13+ languages or no filter | No filter |
| Action categories | Toggle Transform, Encode, Search | All on |
| Excluded apps | Process names to ignore | KeePass, 1Password, Bitwarden |

Settings stored in `%AppData%\SnapActions\settings.json`.

## How It Works

### Text Capture
SnapActions captures selected text without interfering with other apps:

1. **WM_COPY message** — sent directly to the focused window (no keyboard events)
2. **Ctrl+Insert fallback** — for browsers that don't respond to WM_COPY

Uses Ctrl+Insert instead of Ctrl+C to avoid triggering browser extensions (like [h5player](https://github.com/nicedoc/h5player)) that bind to letter keys. The original clipboard content is saved and restored after capture.

### Editable Field Detection
Transform actions and paste mode use a multi-layer detection:
- **Win32 caret** — native text controls (Notepad, etc.)
- **UI Automation ControlType.Edit** — browser text inputs (`<input>`, `<textarea>`)
- **ControlType.Group + TextPattern** — rich text editors (ProseMirror, CodeMirror in Electron apps like Claude Desktop, Slack, VS Code)

## Build from Source

```bash
git clone https://github.com/roko-tech/SnapActions.git
cd SnapActions/SnapActions
dotnet build -c Release
```

### Publish Self-Contained

```bash
cd SnapActions/SnapActions
build.bat
```

Produces a single `SnapActions.exe` in `bin\publish\` (~163MB, includes .NET runtime).

## Architecture

```
SnapActions/
  Core/           Global mouse hook, text capture (WM_COPY + Ctrl+Insert),
                  selection tracking, foreground app detection
  Detection/      14 text type detectors + classifier pipeline
  Actions/        Context actions, transforms, encode/decode, search
  UI/             WPF floating toolbar, settings window, system tray
  Config/         JSON settings with migration for built-in search engines
  Helpers/        Math expression parser, screen utilities, process launcher
```

## Compared to SnipDo

| | SnapActions | SnipDo |
|---|---|---|
| Price | Free, open-source | Free (2 actions), Pro $0.39/mo |
| Actions limit | Unlimited | 2 free, unlimited with Pro |
| Custom search engines | Yes, with language filter | Yes (Pro) |
| Smart text detection | 14 types auto-detected | Manual extension selection |
| Pin actions to toolbar | Yes | No |
| Preview on hover | Yes | No |
| Clipboard preservation | Yes (save/restore) | No |
| Browser extension safe | Yes (Ctrl+Insert, not Ctrl+C) | No |

## License

MIT License — see [LICENSE](LICENSE) file.
