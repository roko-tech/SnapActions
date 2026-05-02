# SnapActions

A free, open-source alternative to [SnipDo](https://snipdo-app.com/). Select text anywhere on Windows and a small toolbar appears with the right actions for what you selected — no limits, no subscription.

![.NET 9](https://img.shields.io/badge/.NET-9.0-purple) ![WPF](https://img.shields.io/badge/WPF-Windows-blue) ![License](https://img.shields.io/badge/License-MIT-green) ![build](https://img.shields.io/github/actions/workflow/status/roko-tech/SnapActions/build.yml?branch=master)

## Install

**[Download SnapActions.exe](https://github.com/roko-tech/SnapActions/releases/latest)** — single file (~74 MB, includes .NET runtime), no installer.

Requires Windows 10 version 19041 or higher. Run the exe; a tray icon appears. That's it.

## Use

Select text anywhere — drag-select, double-click a word, triple-click a line. A floating toolbar appears above the cursor with actions tailored to what you picked.

```
Select  https://example.com           →  Open, QR code, search
Select  2+3*4                         →  Calculate (= 14)
Select  5 ft                          →  Convert (1.524 m | 60 in | 1.667 yd | …)
Select  #89B4FA                       →  Preview color (with swatch), cycle to rgb/hsl
Select  eyJhbGciOiJI...               →  Decode JWT header/payload
Select  {"a":1,"b":2}                 →  Format / Minify JSON
Select  a sentence                    →  Translate, Dictionary, Search
```

**Hover any toolbar button to see the result before clicking.** Color hovers show a live swatch alongside the text.

In editable text fields, transforms apply in-place: select text → click `Aa` → `lowercase` / `UPPERCASE` / `camelCase` / `snake_case` / etc. To bring up a paste menu without an existing selection, **long-press** the left mouse button (500 ms by default) inside any text input.

When an action writes to the clipboard, a "Copied to clipboard" toast confirms it before the toolbar fades.

## What it detects

| Type | Example | Actions |
|---|---|---|
| URL | `https://example.com`, `ftp://files.example.com` | Open, QR code |
| Email | `user@example.com` | Send via mailto |
| File path | `C:\folder\file.txt`, `\\server\share\file` | Open file, reveal in Explorer |
| JSON | `{"key":"val"}`, `[1, 2, 3]` | Format, minify |
| XML/HTML | `<div>text</div>` | Format, strip tags |
| Math | `2+3*4`, `sqrt(16)`, `pi*2` | Calculate |
| IP address | `192.168.1.1`, `2001:db8::1` | Lookup |
| Color | `#89B4FA`, `rgba(255, 0, 0, 0.5)`, `rgb(255 0 0 / 50%)`, `hsl(120deg, 50%, 50%)` | Preview, cycle hex/rgb/hsl with alpha preserved |
| UUID | `550e8400-e29b-41d4-a716-446655440000` | Generate new |
| Base64 | `SGVsbG8gV29ybGQh` | Decode |
| Date/Time | `2026-04-11T12:00:00+05:00`, Unix timestamps | Convert (Local / UTC / Unix) |
| Currency | `$33`, `100 SAR`, `€1,500.50`, `€1.500,50` | Convert (handles American & European number formats) |
| JWT | `eyJhbGciOiJI...`, including `alg=none` unsigned tokens | Decode header / payload / signature |
| Unit | `5 ft`, `100 km/h`, `5 fl oz`, `20°C`, `2 cups` | Convert to all common units |

Detection runs entirely in-process — no network calls, classification under 1 ms on typical selections.

## Inline popups

Translate, Dictionary, and Currency Converter open small popups near the cursor with results from MyMemory, dictionaryapi.dev, and open.er-api.com (all over HTTPS).

Popups stay open until you press **Esc**, click the **X**, click **Copy**, click anywhere outside, or trigger another lookup (which replaces the current popup). They never auto-dismiss on cursor-leave.

Translations are cached for 30 minutes (MyMemory has a 5k chars/day per IP free quota); currency rates for 6 hours per source currency.

## Transforms (in editable fields)

UPPERCASE · lowercase · Title Case (locale-invariant) · camelCase · PascalCase · snake_case · kebab-case · Reverse (grapheme-aware — emoji and combining marks survive) · Trim · Remove Extra Spaces · Remove Line Breaks · Sort Lines · Remove Duplicates (case-insensitive) · Wrap in quotes / brackets / braces / backticks

## Encode / Decode

URL · Base64 · HTML · Hex · ROT13 · MD5 / SHA-1 / SHA-256 / SHA-512 (under Encode; MD5/SHA-1 are checksum-only — never security)

## Search

13 built-in engines — 9 enabled by default (Google, Bing, DuckDuckGo, YouTube, Twitter/X, Reddit, GitHub, StackOverflow, Wikipedia) and 4 opt-in (Amazon, IMDb, npm, NuGet — toggle in Settings).

- **Per-engine language filter** — apply the global Search Language only to engines where you want it
- **Twitter/X** uses `lang:xx` in the search query (works across Top/Latest)
- **Wikipedia** switches subdomain by language code
- **Custom engines** via URL templates: `{0}` is the URL-encoded query, `{1}` is the language code

## Customize

- **Pin** an action to the main toolbar: click the `…` overflow or any submenu, click the gear icon to enter edit mode, right-click an action.
- **Hide** an action: edit mode, left-click to toggle visibility.
- **Reorder** pinned actions: drag them on the toolbar, or right-click → Move Left/Right.
- **Reorder** search engines: edit mode in the Search submenu, use ▲ ▼ arrows.
- **Settings**: double-click the tray icon. All settings auto-save.

| Setting | Options | Default |
|---|---|---|
| Toolbar show delay | Instant, 100 ms – 1 s | Instant |
| Multi-click delay | Instant, 100 – 400 ms | 200 ms |
| Long-press duration | 300 ms – 1 s | 500 ms |
| Auto-dismiss after | 3 / 5 / 8 / 15 / 30 s, Never | 8 s |
| Replace selection on transform | On / Off | On |
| Max inline context actions | 1 / 2 / 3 / 4 / 6 / 8 (rest fall into `…` overflow) | 4 |
| Search language | 13+ languages or no filter | No filter |
| Target currency | 15 (USD, EUR, SAR, GBP, JPY, …) | USD |
| Action categories | Transform / Encode / Search | All on |
| Excluded apps | Process names — use **Add running app...** to pick from running processes | Password managers (KeePass, 1Password, Bitwarden, Dashlane, Enpass, LastPass, RoboForm, NordPass, ProtonPass, Keeper) |

Settings live at `%AppData%\SnapActions\settings.json` with atomic writes. If the file gets corrupted it's renamed to `settings.json.broken-<timestamp>` and defaults are loaded — never silent data loss. The 5 most recent backups are kept.

Logs go to `%AppData%\SnapActions\logs\YYYY-MM-DD.log`, capped at 10 MB per file (older content rotates to `.log.1`, `.log.2`, …) with files older than 7 days pruned every 24 h of process uptime.

## Privacy

- **Detection is local.** All 13 detectors run in-process. No network calls for detection.
- **Cloud actions are clearly scoped.** Translate, Dictionary, and Currency Converter send the selected text to MyMemory, dictionaryapi.dev, and open.er-api.com over HTTPS. The other actions never send your selection anywhere.
- **Password managers excluded by default.** No toolbar appears when the foreground process is a known password manager. Add your own via Settings → Excluded apps.
- **Risky-extension prompt.** Opening files with extensions that can run code (`.exe`, `.ps1`, `.iso`, `.docm`, `.lnk`, etc.) requires explicit confirmation.
- **UNC path prompt.** Opening `\\server\share\…` paths prompts before contacting the remote host (avoids leaking NTLM hashes via SMB).
- **No telemetry.** No analytics, no auto-update, no account.

## How it works

**Dedicated mouse-hook thread.** The low-level Windows mouse hook runs on its own STA background thread with its own dispatcher. UI thread work — WPF rendering, GC, layout — never delays mouse callbacks. Selection debounce uses `Environment.TickCount64` so NTP sync, hibernation resume, or manual clock changes never spuriously suppress or re-fire the hook.

**Text capture without keyboard interference.** SnapActions sends `WM_COPY` directly to the focused window first; on browsers that don't respond it falls back to **Ctrl+Insert** — not Ctrl+C — to avoid triggering browser extensions like [h5player](https://github.com/xxxily/h5player) that hook letter keys. The user's clipboard is snapshotted across all formats and restored after capture, with `null` snapshots distinguished from empty-clipboard snapshots so transient errors don't wipe the user's data.

**Editable-field detection.** Transforms and paste-mode use a multi-layer check:
- **Win32 caret presence** — covers Notepad and other native text controls
- **UI Automation `ControlType.Edit`** — covers `<input>` / `<textarea>` in browsers
- **`ControlType.Group + TextPattern`** — covers ProseMirror, CodeMirror, and similar rich-text editors in Electron apps (Claude Desktop, Slack, VS Code)

**Per-monitor DPI throughout.** Toolbar positioning, hit-testing, and the sub-menu popup each look up the DPI of the monitor they're rendering on, including when the popup spills onto a different-DPI monitor than the toolbar.

**Foreground-shift-safe paste.** When an action pastes back into the user's app (transforms in editable fields, long-press paste-mode), the foreground HWND is snapshotted at click time and the paste is aborted if focus moved between click and `SimulatePaste` — so an Alt-Tab during the click window can't redirect the paste into the wrong app.

## Build from source

```bash
git clone https://github.com/roko-tech/SnapActions.git
cd SnapActions
dotnet build SnapActions/SnapActions.csproj -c Release
dotnet test SnapActions.Tests/SnapActions.Tests.csproj
```

For the single-file release exe:

```bash
cd SnapActions/SnapActions
build.bat
```

Output: `bin\publish\SnapActions.exe` (~74 MB, includes .NET runtime, no install required).

## Tests & CI

214 xUnit tests cover every detector, the math evaluator, unit converter, color conversion (alpha preservation, hue normalization, CSS Color Module 4), the locale-agnostic number parser, all transform / encode / wrap actions, hash known-vectors, action `CanExecute` predicates, registry ID consistency, and `WebSearchAction.BuildUrl` substitution paths.

GitHub Actions runs build + tests on every push and PR — see [`.github/workflows/build.yml`](.github/workflows/build.yml).

## Architecture

```
SnapActions/
  Core/             Mouse hook (dedicated thread), text capture (WM_COPY + Ctrl+Insert),
                    selection tracking, foreground-app + editable-field detection
  Detection/        13 text type detectors + classifier pipeline
  Actions/          Context, transform, encode, search, popups
  UI/               WPF floating toolbar, result popup, settings window, system tray
  Config/           JSON settings with migration, atomic writes, broken-file recovery
  Helpers/          Math evaluator, unit converter, locale-agnostic number parser,
                    screen / DPI utilities, file logger, shared P/Invoke
SnapActions.Tests/  xUnit tests covering pure-function surfaces
.github/workflows/  CI: build + test on push and PR
```

## Compared to SnipDo

| | SnapActions | SnipDo |
|---|---|---|
| Price | Free, open-source | Free (2 actions), Pro $0.39/mo |
| Action limit | Unlimited | 2 free, unlimited with Pro |
| Smart detection | 13 types, automatic | Manual extension selection |
| Translate / Dictionary / Currency | Inline popup, no browser | Opens browser |
| Custom search engines | Yes, with language filter | Pro only |
| Pin actions to toolbar | Yes, drag to reorder | No |
| Hover preview | Yes, on every button | No |
| Color preview | Live swatch + alpha-safe cycle | No |
| Clipboard preservation | Yes (all formats snapshotted) | No |
| Browser-extension-safe capture | Ctrl+Insert | Ctrl+C |
| System impact | Dedicated hook thread, zero lag | UI-thread hook |
| Tests + CI | 214 unit tests, GitHub Actions | — |

## License

MIT — see [LICENSE](LICENSE).
