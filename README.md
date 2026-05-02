# SnapActions

A free, open-source alternative to [SnipDo](https://snipdo-app.com/). Select text anywhere on Windows and get instant context-aware actions — no limits, no subscription.

![.NET 9](https://img.shields.io/badge/.NET-9.0-purple) ![WPF](https://img.shields.io/badge/WPF-Windows-blue) ![License](https://img.shields.io/badge/License-MIT-green)

## Download

**[Download SnapActions.exe](https://github.com/roko-tech/SnapActions/releases/latest)** — single file (~74MB), no installation needed.

> Requires Windows 10 version 19041 or higher.

## Features

### Text Selection Toolbar
- **Drag-select**, **double-click** (word), or **triple-click** (line) text anywhere
- A floating toolbar appears above the selection with smart actions
- Hover over actions to preview the result before applying — color actions show a swatch alongside the hex/rgb/hsl text, not just text
- A `...` overflow button surfaces extra context actions when more apply than fit inline
- Toolbar stays open while your mouse is over it
- A "Copied to clipboard" toast confirms when an action writes to the clipboard

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
| Currency | `$33`, `100 SAR`, `€1.500,50` | Convert to target currency (parses American & European number formats) |
| JWT | `eyJhbGciOi...` | Decode header / payload (incl. `alg=none` unsigned tokens) |
| Unit | `5 ft`, `100 km/h`, `20°C`, `5 fl oz` | Convert to common units |

### Inline Popups (no browser needed)
- **Translate** — translates selected text using MyMemory API, shows result in popup
- **Dictionary** — word definition lookup via dictionaryapi.dev (1-3 word selections)
- **Currency Converter** — detects amounts with currency symbols/codes, converts using open.er-api.com with configurable target currency

Popups stay open until you dismiss them — press **Esc**, click the **X**, click **Copy**, or click anywhere outside. A new lookup replaces the existing popup instead of stacking.

### Text Transforms (in editable fields)
UPPERCASE, lowercase, Title Case, camelCase, PascalCase, snake_case, kebab-case, Reverse, Trim, Remove Extra Spaces, Remove Line Breaks, Sort Lines, Remove Duplicates, Wrap in quotes/brackets/braces/backticks

### Additional Actions
- **Delete** — remove selected text in editable fields
- **Paste Plain Text** — strip formatting and paste as plain Unicode text
- **Encode/Decode** — URL, Base64, HTML, Hex, ROT13
- **Hash** — MD5, SHA-1, SHA-256, SHA-512 (under Encode; MD5/SHA-1 are for checksums only, not security)
- **JWT decode** — split header/payload/signature for `eyJ…` tokens
- **QR code** — open a QR PNG for any selected URL
- **Color convert** — cycle a color between hex / rgb / hsl representations, preserving alpha (`#RRGGBBAA`, `rgba()`, `hsla()`). Accepts CSS Color Module 4 syntax (`rgb(255 0 0 / 50%)`)

### Search Engines
13 built-in engines — 9 enabled by default (Google, Bing, DuckDuckGo, YouTube, Twitter/X, Reddit, GitHub, StackOverflow, Wikipedia) and 4 opt-in (Amazon, IMDb, npm, NuGet — toggle in Settings).

- **Language filtering** — filter results by language (13+ languages supported)
- **Twitter/X** uses `lang:xx` in the search query so it works across Top/Latest tabs
- **Wikipedia** switches subdomain by language
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
- **Drag** pinned toolbar buttons to reorder them, or **right-click** to rearrange / unpin

### Settings
Double-click the system tray icon to open Settings:

| Setting | Options | Default |
|---------|---------|---------|
| Show delay | Instant, 100ms, 200ms, 300ms, 500ms, 1s | Instant |
| Multi-click delay | Instant, 100ms, 200ms, 300ms, 400ms | 200ms |
| Long-press duration | 300ms, 500ms, 750ms, 1s | 500ms |
| Auto-dismiss | 3s, 5s, 8s, 15s, 30s, Never | 8 seconds |
| Replace on transform | On/Off | On |
| Search language | 13+ languages or no filter | No filter |
| Target currency | 15 currencies (USD, EUR, SAR, etc.) | USD |
| Action categories | Toggle Transform, Encode, Search | All on |
| Excluded apps | Process names to ignore (use **Add running app...** to pick from currently-running processes) | KeePass(XC), 1Password, Bitwarden, Dashlane, Enpass, LastPass, RoboForm, NordPass, ProtonPass, Keeper |

Settings auto-save on every change. Stored in `%AppData%\SnapActions\settings.json`. If the file gets corrupted, it's renamed to `settings.json.broken-<timestamp>` and defaults are loaded — never silent data loss. Old `.broken-*` backups are pruned to the most recent 5.

Logs (errors and lifecycle events) are written to `%AppData%\SnapActions\logs\YYYY-MM-DD.log` and rotated after 7 days.

## How It Works

### Performance
The global mouse hook runs on a **dedicated background thread** with its own message pump. This ensures zero impact on system responsiveness — WPF rendering, garbage collection, and UI work on the main thread never delay mouse message delivery.

### Text Capture
SnapActions captures selected text without interfering with other apps:

1. **WM_COPY message** — sent directly to the focused window (no keyboard events)
2. **Ctrl+Insert fallback** — for browsers that don't respond to WM_COPY

Uses Ctrl+Insert instead of Ctrl+C to avoid triggering browser extensions (like [h5player](https://github.com/xxxily/h5player)) that bind to letter keys. The original clipboard content is saved and restored after capture.

### Editable Field Detection
Transform actions and paste mode use multi-layer detection:
- **Win32 caret** — native text controls (Notepad, etc.)
- **UI Automation ControlType.Edit** — browser text inputs (`<input>`, `<textarea>`)
- **ControlType.Group + TextPattern** — rich text editors (ProseMirror, CodeMirror in Electron apps like Claude Desktop, Slack, VS Code)

## Build from Source

```bash
git clone https://github.com/roko-tech/SnapActions.git
cd SnapActions
dotnet build SnapActions/SnapActions.csproj -c Release
dotnet test SnapActions.Tests/SnapActions.Tests.csproj
```

### Publish Self-Contained

```bash
cd SnapActions/SnapActions
build.bat
```

Produces a single compressed `SnapActions.exe` in `bin\publish\` (~74MB, includes .NET runtime).

## Tests & CI

The repo ships with an xUnit test suite covering every detector, the math evaluator, the unit converter, color conversion (including alpha-preservation and negative-hue normalization), the locale-agnostic number parser, all transform/encode/wrap actions, and `WebSearchAction.BuildUrl`. GitHub Actions runs build + tests on every push and PR — see `.github/workflows/build.yml`.

## Architecture

```
SnapActions/
  Core/             Mouse hook (dedicated thread), text capture (WM_COPY + Ctrl+Insert),
                    selection tracking, foreground app detection
  Detection/        13 text type detectors + classifier pipeline
  Actions/          Context actions, transforms, encode/decode, search, popups
  UI/               WPF floating toolbar, result popup, settings window, system tray
  Config/           JSON settings with migration for built-in search engines
  Helpers/          Math evaluator, locale-agnostic number parsing, screen utilities,
                    shared P/Invoke (NativeMethods)
SnapActions.Tests/  xUnit test project (200+ tests) covering pure-function surfaces
.github/workflows/  CI: build + test on push and PR
```

## Compared to SnipDo

| | SnapActions | SnipDo |
|---|---|---|
| Price | Free, open-source | Free (2 actions), Pro $0.39/mo |
| Actions limit | Unlimited | 2 free, unlimited with Pro |
| Translate | Inline popup (no browser) | Opens browser |
| Dictionary | Inline popup (no browser) | Opens browser |
| Currency converter | Inline popup, configurable target | Not available |
| Custom search engines | Yes, with language filter | Yes (Pro) |
| Smart text detection | 14 types auto-detected | Manual extension selection |
| Pin actions to toolbar | Yes | No |
| Preview on hover | Yes | No |
| Clipboard preservation | Yes (save/restore) | No |
| Browser extension safe | Yes (Ctrl+Insert, not Ctrl+C) | No |
| System performance | Dedicated hook thread, zero lag | Runs on UI thread |

## License

MIT License — see [LICENSE](LICENSE) file.
