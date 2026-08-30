# SnapActions

A free, open-source smart text-selection toolbar for Windows. Select text anywhere and a small toolbar appears with the right actions for what you selected — no limits, no subscription.

![.NET 10](https://img.shields.io/badge/.NET-10.0-purple) ![WPF](https://img.shields.io/badge/WPF-Windows-blue) ![License](https://img.shields.io/badge/License-MIT-green) ![build](https://img.shields.io/github/actions/workflow/status/roko-tech/SnapActions/build.yml?branch=master)

## Install

**[Download SnapActions.exe](https://github.com/roko-tech/SnapActions/releases/latest)** — single file (~75 MB, includes .NET runtime), no installer.

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

In editable text fields, transforms apply in-place: select text → click `Aa` → `lowercase` / `UPPERCASE` / `camelCase` / `snake_case` / etc. To bring up a paste menu without an existing selection, **long-press** the left mouse button (500 ms by default) inside any text input — or switch the trigger to double-click (on an empty editable field), or off, in Settings.

Automatic highlight capture is clipboard-free: leave **Show toolbar automatically when I select text** on and SnapActions reads the selection through UI Automation without running a copy command or touching the clipboard. If an app does not expose its selection through UIA, turn on **Show toolbar when I press Ctrl+C** and copy explicitly to summon the toolbar there.

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
| Color | `#89B4FA`, `rgba(255, 0, 0, 0.5)`, `rgb(255 0 0 / 50%)`, `hsl(120, 50%, 50%)` | Preview, cycle hex/rgb/hsl with alpha preserved |
| UUID | `550e8400-e29b-41d4-a716-446655440000` | Generate new |
| Base64 | `SGVsbG8gV29ybGQh` | Decode |
| Date/Time | `2026-04-11T12:00:00+05:00`, Unix timestamps | Convert (Local / UTC / Unix) |
| Currency | `$33`, `100 SAR`, `€1,500.50`, `€1.500,50` | Convert (handles American & European number formats) |
| JWT | `eyJhbGciOiJI...`, including `alg=none` unsigned tokens | Decode header / payload / signature |
| Unit | `5 ft`, `100 km/h`, `5 fl oz`, `20°C`, `2 cups` | Convert to all common units |

Detection runs entirely in-process — no network calls, classification under 1 ms on typical selections.

## Inline popups

Translate, Dictionary, and Currency Converter open small popups near the cursor with results from MyMemory, dictionaryapi.dev, and open.er-api.com (all over HTTPS). The first time you use one, SnapActions asks before anything is sent — toggle it anytime via **Allow online lookups** in Settings.

Popups stay open until you press **Esc**, click the **X**, click **Copy**, click anywhere outside, or trigger another lookup (which replaces the current popup). They never auto-dismiss on cursor-leave.

Translations are cached for 30 minutes (MyMemory has a 5k chars/day per IP free quota); currency rates for 6 hours per source currency.

## Transforms (in editable fields)

UPPERCASE · lowercase · Title Case (locale-invariant) · camelCase · PascalCase · snake_case · kebab-case · Reverse (grapheme-aware — emoji and combining marks survive) · Trim · Remove Extra Spaces · Remove Line Breaks · Sort Lines · Remove Duplicates (case-insensitive) · Wrap in quotes / brackets / braces / backticks

## Encode / Decode

URL · Base64 · HTML · Hex · ROT13 · MD5 / SHA-1 / SHA-256 / SHA-512 (under Encode; MD5/SHA-1 are checksum-only — never security)

## Search

13 built-in engines — 9 enabled by default (Google, Bing, DuckDuckGo, YouTube, Twitter/X, Reddit, GitHub, StackOverflow, Wikipedia) and 4 opt-in (Amazon, IMDb, npm, NuGet — toggle in Settings).

- **Per-engine language filter** — apply the global Language only to engines where you want it
- **Twitter/X** uses `lang:xx` in the search query (works across Top/Latest)
- **Wikipedia** switches subdomain by language code
- **Custom engines** via URL templates: `{0}` is the URL-encoded query, `{1}` is the language code

## Customize

- **Pin** an action to the main toolbar: click the `…` overflow or any submenu, click the gear icon to enter edit mode, right-click an action.
- **Hide** an action: edit mode, left-click to toggle visibility.
- **Reorder** pinned actions: drag them on the toolbar, or right-click → Move Left/Right.
- **Reorder** search engines: edit mode in the Search submenu, use ▲ ▼ arrows.
- **Custom actions**: Settings → Custom Actions — build your own from a URL template (`{0}` = the selection) that either opens in the browser or fetches and shows the result (optionally a single JSON field). Scope it to any detected type or all selections.
- **Per-app profiles**: Settings → App Profiles — hide specific actions when a chosen app is in the foreground.
- **Settings**: double-click the tray icon. All settings auto-save.

| Setting | Options | Default |
|---|---|---|
| Toolbar show delay | Instant, 100 ms – 1 s | Instant |
| Multi-click delay | Instant, 100 – 400 ms | 200 ms |
| Paste mode trigger | Long-press / Double-click / Off | Long-press |
| Show toolbar automatically when I select text | On / Off | On |
| Show toolbar when I press Ctrl+C | On / Off | Off |
| Long-press duration | 300 ms – 1 s | 500 ms |
| Auto-dismiss after | 3 / 5 / 8 / 15 / 30 s, Never | 8 s |
| Replace selection on transform | On / Off | On |
| Restore previous clipboard after copy action | On / Off | Off |
| Max inline context actions | 1 / 2 / 3 / 4 / 6 / 8 (rest fall into `…` overflow) | 4 |
| Language (search filter + Translate/Dictionary target) | 13+ languages or no filter | No filter |
| Target currency | 15 (USD, EUR, SAR, GBP, JPY, …) | USD |
| Allow online lookups (Translate / Dictionary / Currency) | On / Off | Off — asks on first use |
| Action categories | Transform / Encode / Search | All on |
| Excluded apps | Process names — use **Add running app...** to pick from running processes | Password managers (KeePass, 1Password, Bitwarden, Dashlane, Enpass, LastPass, RoboForm, NordPass, ProtonPass, Keeper) |

Settings live at `%AppData%\SnapActions\settings.json`. Writes are crash-safe (serialize to `settings.json.tmp`, then atomic rename) so a process crash mid-write can't blank the file; the write is not fsync'd, so a hard power loss between the rename and the disk flush can still resurrect the previous file content. If the file gets corrupted on load it's renamed to `settings.json.broken-<timestamp>` and defaults are used — never silent data loss. The 5 most recent backups are kept.

Logs go to `%AppData%\SnapActions\logs\YYYY-MM-DD.log`, capped at 10 MB per file (older content rotates to `.log.1`, `.log.2`, …) with files older than 7 days pruned every 24 h of process uptime.

## Privacy

- **Detection is local.** All detectors run in-process. No network calls for detection.
- **Inline cloud popups (opt-in).** Translate, Dictionary, and Currency Converter send the selected text to MyMemory, dictionaryapi.dev, and open.er-api.com over HTTPS — the SnapActions process makes the request and shows the result inline. These run only after you allow online lookups; you're asked the first time, and any custom "fetch" action you add is gated the same way.
- **Browser-handoff actions.** QR Code (api.qrserver.com) and IP Lookup (ipinfo.io) open a URL containing your selection in your default browser; SnapActions itself never makes the request. Web search engines work the same way.
- **Everything else stays local.** Format/minify, transform, encode/decode, hash, color/unit/timezone/JWT/Base64 — none of these touch the network.
- **Password managers excluded by default.** No toolbar appears when the foreground process is a known password manager. Add your own via Settings → Excluded apps.
- **Risky-extension prompt.** Opening files with code-bearing extensions (`.exe`, `.bat`, `.ps1`, `.iso`, `.docm`, `.lnk`, …) requires explicit confirmation. Without this, a malicious selection like `C:\Users\you\Downloads\invoice.exe` could be one click away from running.
- **UNC path prompt.** Opening `\\server\share\…` paths prompts before contacting the remote host. Without the prompt, opening a UNC path on an attacker-controlled network could initiate an SMB connection that leaks your Windows NTLM hash to the named server.
- **No telemetry.** No analytics, no auto-update, no account.

## How it works

**Dedicated mouse-hook thread.** The low-level Windows mouse hook runs on its own STA background thread with its own dispatcher. UI thread work — WPF rendering, GC, layout — never delays mouse callbacks. Selection debounce uses `Environment.TickCount64` so NTP sync, hibernation resume, or manual clock changes never spuriously suppress or re-fire the hook.

**Automatic text capture is UI Automation-only.** Mouse drag, double-click, and triple-click selection use `TextPattern.GetSelection` through the accessibility tree. SnapActions walks up to 6 parents of the focused element and also checks the element under the cursor, which covers browser content whose focus stays on a container. For Chromium, same-line drags reconstruct the characters from their on-screen geometry and map visual bidi runs back to logical text order; double-click reconstructs the clicked word and requires the same UTF-16 length as the provider selection. This avoids adjacent-run results in mixed LTR/RTL content. The path never sends `WM_COPY`, never injects `Ctrl+Insert`, and never reads, clears, or writes the clipboard.

UI Automation coverage is not universal. Java Swing, some browser/Electron contexts, and custom text renderers may expose no selected text, so the automatic toolbar cannot appear there without a copy operation. A Chromium gesture also fails closed when its geometry cannot be mapped safely (including cross-line bidi drags), or when a double-click word cannot confirm the provider-reported selection length, rather than showing possibly adjacent text. Enable **Show toolbar when I press Ctrl+C** for those cases: your physical copy supplies the exact text, and SnapActions only validates and reads the resulting clipboard value.

**Clipboard behavior is explicit.** Automatic highlighting never touches it. A physical Ctrl+C changes it because you requested a copy. Toolbar actions that intentionally copy a result show a confirmation toast; **Restore previous clipboard after copy action** can put the prior contents back after about 3 seconds.

**Editable-field detection.** Transforms and paste-mode use a multi-layer check:
- **Win32 caret presence** — covers Notepad and other native text controls
- **UI Automation `ControlType.Edit`** — covers `<input>` / `<textarea>` in browsers
- **`ControlType.Group + TextPattern`** — covers ProseMirror, CodeMirror, and similar rich-text editors in Electron apps (Claude Desktop, Slack, VS Code)

**Per-monitor DPI throughout.** Toolbar positioning, hit-testing, and the sub-menu popup each look up the DPI of the monitor they're rendering on, including when the popup spills onto a different-DPI monitor than the toolbar.

**Foreground-shift-safe synthetic input.** Every path that injects input back into the user's app — transforms in editable fields, long-press paste-mode, Paste Plain Text, Delete — snapshots the foreground HWND when the toolbar *appears* and aborts if focus has moved by injection time. An Alt-Tab before or after the button click can't redirect a paste (or a destructive Delete keystroke) into the wrong app.

**When the toolbar appears (and when it doesn't).** Mouse-up after a drag, double/triple-click, or long-press *can* trigger the toolbar — but several gates have to agree before it shows. In order:

1. **NCHITTEST gate** (gesture-fire time) — gestures that started on a window's title bar, resize border, or native scrollbar are dropped. The hook can't tell those drags from a text-selection drag at the OS level, so we ask the receiving window via `WM_NCHITTEST` — deferred to fire time so only candidate selection gestures (not every click system-wide) pay the cross-process round-trip.
2. **Scrollbar-edge heuristic** (mouse-up) — a drag with both endpoints within ~25 px of the right (or left, in RTL layouts) edge AND primarily vertical is treated as a custom-scrollbar drag (Chrome, VS Code, Slack, Electron apps). Same with bottom edge + horizontal motion.
3. **Cursor-shape gate** (mouse-down + mouse-up) — the OS shows the text (I-beam) cursor over selectable text, a more universal signal than UIA TextPattern. I-beam at either point permits capture. A *hard* non-text cursor (resize, crosshair, wait, no-drop, …) at both points — resizing a window, a busy app, dragging a slider — is dropped before UIA work. Arrow, link-hand, custom, and unreadable cursors remain eligible because browsers and custom controls can display them over real selectable text.
4. **Excluded-app + self-PID checks** — anything in your Settings → Excluded apps list never sees a toolbar, and clicks on SnapActions's own toolbar are ignored.
5. **UIA-only selection read** — SnapActions checks the focused element's tree and then the element under the cursor. A non-empty range supplies the toolbar text directly. A known non-text item (Explorer file, desktop icon, list row) stops capture, while empty or unavailable UIA data fails closed with no toolbar and no clipboard fallback.

If a suppression case is misbehaving in your app, check the log file (`%AppData%\SnapActions\logs\YYYY-MM-DD.log`) — every gate that fires writes a line with the cursor position and reason. As an escape hatch, add the app's process name to **Settings → Excluded apps**.

## Build from source

```bash
git clone https://github.com/roko-tech/SnapActions.git
cd SnapActions
dotnet build SnapActions/SnapActions.csproj -c Release
dotnet test SnapActions.Tests/SnapActions.Tests.csproj
```

For the single-file release exe:

```bash
cd SnapActions
build.bat
```

Output: `bin\publish\SnapActions.exe` (~75 MB, includes .NET runtime, no install required).

## Tests & CI

334 xUnit tests cover every detector, the math evaluator (including the recursion-depth guard), unit converter, color conversion (alpha preservation, hue normalization, CSS Color Module 4), the locale-agnostic number parser, all transform / encode / wrap actions, hash known-vectors, action `CanExecute` predicates, registry ID consistency, `WebSearchAction.BuildUrl` substitution, the capture-gate policies (probe-outcome plans incl. the ambiguous-cursor drag keystroke and its file-manager exclusion, cursor-shape aggressiveness with the hard/ambiguous split, probe-safe drive detection), and the custom-action / per-app-profile logic.

GitHub Actions runs build + tests on every push and PR — see [`.github/workflows/build.yml`](.github/workflows/build.yml).

## Architecture

```
SnapActions/
  Core/             Mouse hook (dedicated thread), UIA selection capture + explicit Ctrl+C observation,
                    selection tracking, foreground-app + editable-field detection
  Detection/        Text-type detectors + classifier pipeline
  Actions/          Context, transform, encode, search, popups
  UI/               WPF floating toolbar, result popup, settings window, system tray
  Config/           JSON settings with migration, atomic writes, broken-file recovery
  Helpers/          Math evaluator, unit converter, locale-agnostic number parser,
                    screen / DPI utilities, file logger, shared P/Invoke
SnapActions.Tests/  xUnit tests covering pure-function surfaces
.github/workflows/  CI: build + test on push and PR
```

## Highlights

- **Free and open-source**, MIT-licensed — unlimited actions, no subscription.
- **Smart detection** — 13 text types recognized automatically.
- **Translate / Dictionary / Currency** — results shown inline, no browser.
- **Custom search engines** with a language filter, plus your own URL / fetch actions.
- **Per-app profiles** and pin-and-reorder actions on the toolbar.
- **Hover preview** on every button; live color swatch with an alpha-safe cycle.
- **Clipboard-free automatic capture** — highlighting never invokes copy or touches the clipboard.
- **Tested** — unit tests with GitHub Actions CI.

## License

MIT — see [LICENSE](LICENSE).
