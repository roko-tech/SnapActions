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

Prefer an explicit trigger? Turn on **Capture on Ctrl+C** in Settings — then copying text the normal way also pops the toolbar for it, with no synthetic keystroke and no clipboard clearing.

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
| Capture on Ctrl+C | On / Off | Off |
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

**Text capture in three layers, keys last.** SnapActions tries the quietest mechanism first and only escalates when it has to:
1. **`WM_COPY`** to the focused window. No keystrokes, no clipboard read needed beyond the post-copy result. Works for native Win32 apps.
2. **UI Automation `TextPattern.GetSelection`** — probes the selection through the accessibility tree, no keystrokes. It walks up to 6 parents of the focused element and also checks the element *under the cursor* — some apps put keyboard focus on a container rather than the text (the X/Twitter feed focuses the tweet cell). In quiet captures, focused-tree text can be used directly. When an exact browser copy is available, UIA only confirms that text is selected and the returned string is discarded, because Chromium providers can report an adjacent run in mixed LTR/RTL content; WM_COPY or Ctrl+Insert supplies the exact visual selection instead.
3. **Ctrl+Insert** via `SendInput` — last resort for apps where neither WM_COPY nor UIA work (Java Swing, some older Edge contexts, certain custom Electron renderers). Insert, not C, so browser extensions that hook letter keys don't see it. If a specific app still misbehaves on the synthetic key, add its process name to **Settings → Excluded apps** to suppress capture there entirely.

**The clipboard is never cleared.** SnapActions snapshots it (the round-trippable formats — text, HTML, RTF, CSV, file drops, bitmaps), then watches the clipboard *sequence number* to tell whether a capture step actually wrote to it, and restores the prior contents only if nothing else has touched it since. So a gesture that captures nothing leaves the clipboard completely untouched, a copy you make in another app mid-capture is never clobbered, and the restore is guarded so a transient error or app shutdown can't wipe your data.

**Editable-field detection.** Transforms and paste-mode use a multi-layer check:
- **Win32 caret presence** — covers Notepad and other native text controls
- **UI Automation `ControlType.Edit`** — covers `<input>` / `<textarea>` in browsers
- **`ControlType.Group + TextPattern`** — covers ProseMirror, CodeMirror, and similar rich-text editors in Electron apps (Claude Desktop, Slack, VS Code)

**Per-monitor DPI throughout.** Toolbar positioning, hit-testing, and the sub-menu popup each look up the DPI of the monitor they're rendering on, including when the popup spills onto a different-DPI monitor than the toolbar.

**Foreground-shift-safe synthetic input.** Every path that injects input back into the user's app — transforms in editable fields, long-press paste-mode, Paste Plain Text, Delete — snapshots the foreground HWND when the toolbar *appears* and aborts if focus has moved by injection time. An Alt-Tab before or after the button click can't redirect a paste (or a destructive Delete keystroke) into the wrong app.

**When the toolbar appears (and when it doesn't).** Mouse-up after a drag, double/triple-click, or long-press *can* trigger the toolbar — but several gates have to agree before it shows. In order:

1. **NCHITTEST gate** (gesture-fire time) — gestures that started on a window's title bar, resize border, or native scrollbar are dropped. The hook can't tell those drags from a text-selection drag at the OS level, so we ask the receiving window via `WM_NCHITTEST` — deferred to fire time so only candidate selection gestures (not every click system-wide) pay the cross-process round-trip.
2. **Scrollbar-edge heuristic** (mouse-up) — a drag with both endpoints within ~25 px of the right (or left, in RTL layouts) edge AND primarily vertical is treated as a custom-scrollbar drag (Chrome, VS Code, Slack, Electron apps). Same with bottom edge + horizontal motion.
3. **Cursor-shape gate** (mouse-down + mouse-up) — the OS shows the text (I-beam) cursor over selectable text, a more universal signal than UIA TextPattern. I-beam at either point → full capture. A *hard* non-text cursor (resize, crosshair, wait, no-drop, …) at *both* points — resizing a window, a busy app, dragging a slider — is dropped before any clipboard or keystroke work. The **arrow and link-hand** cursors are treated as *ambiguous*, because click-to-open web content shows them over genuinely selectable text (an X/Twitter feed tweet shows the hand; an App Store description shows the plain arrow). Rather than drop those, a quiet capture runs — and a *drag* (a strong selection signal, unlike a click) additionally gets the Ctrl+Insert keystroke that reliably reads a browser selection UIA can't. That keystroke is self-gating (nothing selected → nothing copied) and withheld in Explorer / file managers, where it would copy files rather than text; a double-click under arrow/hand stays quiet. A *custom* cursor we can't classify (some apps draw their own I-beam) also falls back to quiet capture: WM_COPY and UIA may run, but no synthetic keystroke. Permissive when the cursor can't be read (touch, full-screen) so real selections still show.
4. **Excluded-app + self-PID checks** — anything in your Settings → Excluded apps list never sees a toolbar, and clicks on SnapActions's own toolbar are ignored.
5. **Probe-planned three-layer capture** — described above. A UIA probe first classifies the moment from the focused element's tree *or the element under the cursor*. During a full capture, a non-empty UIA range proves that text is selected but does not supply the final string; the exact clipboard path does, avoiding adjacent-run errors in mixed LTR/RTL browser text. Quiet captures keep the direct focused-tree UIA fallback. A non-text item (Explorer file, desktop icon, list row) stops capture outright — except for an arrow/hand drag over web content, where an item is often selectable text (an X/Twitter tweet is exposed as a list row but holds text), so the self-gating keystroke runs anyway (never in Explorer / file managers). A TextPattern that reports *no selection* restricts the cascade — WM_COPY still runs silently, and the synthetic Ctrl+Insert stays available for drag gestures (some accessibility providers report an empty selection even when text really is selected, and a drag that passed the cursor gate is the strongest signal they're wrong).

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
  Core/             Mouse hook (dedicated thread), text capture (WM_COPY → UIA → Ctrl+Insert),
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
- **Clipboard-safe capture** — your clipboard is preserved, and capture runs on a dedicated hook thread for zero input lag.
- **Tested** — unit tests with GitHub Actions CI.

## License

MIT — see [LICENSE](LICENSE).
