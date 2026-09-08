# SnapActions

A free, open-source smart text-selection toolbar for Windows. Select text anywhere and a small toolbar appears with the right actions for what you selected — no limits, no subscription.

![.NET 10](https://img.shields.io/badge/.NET-10.0-purple) ![WPF](https://img.shields.io/badge/WPF-Windows-blue) ![License](https://img.shields.io/badge/License-MIT-green) ![build](https://img.shields.io/github/actions/workflow/status/roko-tech/SnapActions/build.yml?branch=master)

Version **2.4.2** adds drag-to-pin toolbar customization, restores hover previews after reopening menus, and gives Paste a compact clipboard icon. It also guards native edits against changed selections, bounds XML formatting, permits retry after a known Copy failure, and rejects invalid Electron selection geometry. QR Code and Inspect Text have been removed. See the [release notes](docs/releases/v2.4.2.md).

The [release validation](docs/release-validation-2026-09-08.md) records live Brave selection, exact mixed-language Copy, stale-selection rejection, read-only fields and reconnect after restart, alongside the earlier Notepad and VS Code checks and their limits.

## Install

**[Download the latest release](https://github.com/roko-tech/SnapActions/releases/latest)** — includes the .NET runtime, no installer. Extract the complete ZIP and keep `SnapActions.exe` and `browser-extension` together in a permanent location.

Requires Windows 10 version 19041 or higher. Run the exe; a tray icon appears. That's it.

For the optional Brave/Chrome/Edge companion, follow [browser setup](browser-extension/README.md). Settings → Browser → **Open extension folder** locates the bundled companion beside your executable. When upgrading an existing companion installation, update its files and reload the extension; v2.4.0 requires protocol version 1. Register the helper again in Settings → Browser if the executable's location changed. Existing settings are retained.

## Use

Select text anywhere — drag-select, double-click a word, triple-click a line. A floating toolbar appears above the cursor with actions tailored to what you picked.

```
Select  https://example.com           →  Open, clean tracking links, search
Select  2+3*4                         →  Calculate (= 14)
Select  5 ft                          →  Convert (1.524 m | 60 in | 1.667 yd | …)
Select  #89B4FA                       →  Preview color (with swatch), cycle to rgb/hsl
Select  eyJhbGciOiJI...               →  Decode JWT header/payload
Select  {"a":1,"b":2}                 →  Format / Minify JSON
Select  a sentence                    →  Translate, Dictionary, Search
```

**Hover any toolbar button to see the result before clicking.** Color hovers show a live swatch alongside the text.

Transforms now open a result preview with **Copy result** and, for a verified editable target, **Replace selection**. They also work on read-only selections. The source excerpt stays beside the result; replacement revalidates the original target before input. Native Delete, Paste and Replace require writable capability plus the same captured selection endpoints and text. When a provider cannot supply that evidence, captured text remains available for Copy. To bring up a paste menu without an existing selection, **long-press** the left mouse button (500 ms by default) inside any text input — or switch the trigger to double-click (on an empty editable field), or off, in Settings.

A busy clipboard leaves **Copy result** available for a safe retry. A cancelled or uncertain replacement requires a fresh selection. If every matching action is hidden, the toolbar still offers Copy and the customization menu.

Automatic highlight capture is clipboard-free: leave **Show toolbar automatically when I select text** on. The optional [browser companion](browser-extension/README.md) reads the browser's actual selected text, including mixed Arabic/English and selections spanning multiple lines. Other apps use UI Automation. Neither automatic path runs a copy command or touches the clipboard. For unsupported surfaces, turn on **Show toolbar when I press Ctrl+C** and copy explicitly to summon the toolbar there.

Press **Ctrl+Shift+Space** for the searchable action palette. Use Up/Down to choose, Enter to run, and Esc to close. Pure actions preview their result before Copy or Replace. If no selection is readable, enter text in the palette. An unavailable shortcut is reported in Settings → Browser → Capture health.

Mixed Arabic/English hover previews use the browser selection's text direction when available, with the selected phrase displayed separately from the English search label. Long previews trim within the popup. Leaving a toolbar action closes its hover-only popup; an open action menu stays available. This affects display only; copied text stays unchanged.

## What it detects

| Type | Example | Actions |
|---|---|---|
| URL | `https://example.com`, `ftp://files.example.com` | Open, clean tracking links |
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

XML formatting rejects DTD declarations and limits input to 32,768 characters and formatted output to 65,536 characters, including indentation.

Detection runs entirely in-process — no network calls, classification under 1 ms on typical selections.

## Inline popups

Translate, Dictionary, and Currency Converter open small popups near the cursor with results from MyMemory, dictionaryapi.dev, and open.er-api.com (all over HTTPS). The first time you use one, SnapActions asks before anything is sent — toggle it anytime via **Allow online lookups** in Settings.

Popups stay open until you press **Esc**, click the **X**, click **Copy**, click anywhere outside, or trigger another lookup (which replaces the current popup). They never auto-dismiss on cursor-leave.

Successful translations are cached for 30 minutes; currency rates for 6 hours per source currency. Translation accepts up to 500 **UTF-8 bytes**, with explicit source and target language controls plus Swap. Search language is independent. The current dictionary endpoint supports English; an unsupported preference produces an error rather than silently looking up another language. Timeouts and service failures offer Retry and cannot be copied as successful results.

## Transforms

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

- **Pin** an action: drag it from any menu onto the toolbar. A blue insertion line shows where it will land. You can also right-click → Pin to toolbar.
- **Hide or unpin**: right-click any action, including items in `…`. Use the toolbar gear to see all actions and restore hidden ones; hiding keeps their saved pin order.
- **Reorder** pins: drag to the left or right half of another pin, or right-click → Move left/right. Dragging works even when a pin is disabled for the current selection.
- **Paste** uses a compact clipboard icon on the toolbar; its tooltip and accessible name still identify Paste Plain Text.
- **Reorder** search engines: edit mode in the Search submenu, use ▲ ▼ arrows.
- **Custom actions**: Settings → Custom — build your own from a URL template (`{0}` = the selection) that either opens in the browser or fetches and shows the result (optionally a single JSON field). Scope it to any detected type or all selections.
- **Per-app profiles**: Settings → Apps — hide specific actions when a chosen app is in the foreground.
- **Settings**: double-click the tray icon. Changes auto-save and refresh the current toolbar in browsers and other apps. Pinned actions stay first and do not count toward the suggested-action limit. The toolbar uses the available monitor width; actions that cannot fit remain in `…`. Pinned Paste/Delete remain visible but disabled on read-only text, with an explanatory tooltip.

| Setting | Options | Default |
|---|---|---|
| Toolbar show delay | Instant, 100 ms – 1 s | Instant |
| Multi-click delay | Instant, 100 – 400 ms | 200 ms |
| Paste mode trigger | Long-press / Double-click / Off | Long-press |
| Show toolbar automatically when I select text | On / Off | On |
| Show toolbar when I press Ctrl+C | On / Off | Off |
| Long-press duration | 300 ms – 1 s | 500 ms |
| Auto-dismiss after | 3 / 5 / 8 / 15 / 30 s, Never | 8 s |
| Prefer Replace in the keyboard palette (editable selections) | On / Off | On |
| Restore previous clipboard after copy action | On / Off | Off |
| Suggested actions on toolbar | 1 / 2 / 3 / 4 / 6 / 8 (rest fall into `…` overflow) | 4 |
| Search language filter | Supported search languages or no filter | No filter |
| Translation languages | Explicit source and target, 23 choices | Choose source; target English |
| Dictionary language | English | English |
| Theme | System / Light / Dark | System |
| Target currency | 15 (USD, EUR, SAR, GBP, JPY, …) | USD |
| Allow online lookups (Translate / Dictionary / Currency) | On / Off | Off — asks on first use |
| Action categories | Transform / Encode / Search | All on |
| Excluded apps | Process names — use **Add running app...** to pick from running processes | Password managers (KeePass, 1Password, Bitwarden, Dashlane, Enpass, LastPass, RoboForm, NordPass, ProtonPass, Keeper) |

Settings live at `%AppData%\SnapActions\settings.json`. Writes are crash-safe (serialize to `settings.json.tmp`, then atomic rename) so a process crash mid-write can't blank the file; the write is not fsync'd, so a hard power loss between the rename and the disk flush can still resurrect the previous file content. If the file gets corrupted on load it's renamed to `settings.json.broken-<timestamp>` and defaults are used — never silent data loss. The 5 most recent backups are kept.

Logs go to `%AppData%\SnapActions\logs\YYYY-MM-DD.log`, capped at 10 MB per file (older content rotates to `.log.1`, `.log.2`, …) with files older than 7 days pruned every 24 h of process uptime.

## Privacy

- **Detection is local.** All detectors run in-process. No network calls for detection.
- **Inline cloud popups (opt-in).** Translate, Dictionary, and Currency Converter send the selected text to MyMemory, dictionaryapi.dev, and open.er-api.com over HTTPS — the SnapActions process makes the request and shows the result inline. These run only after you allow online lookups; you're asked the first time, and any custom "fetch" action you add is gated the same way.
- **Browser-handoff actions.** IP Lookup (ipinfo.io) opens a URL containing your selection in your default browser; SnapActions itself never makes the request. Web search engines work the same way.
- **Everything else stays local.** Format/minify, transform, encode/decode, hash, color/unit/timezone/JWT/Base64 — none of these touch the network.
- **Password managers excluded by default.** No toolbar appears when the foreground process is a known password manager. Add your own via Settings → Excluded apps.
- **Risky-extension prompt.** Opening files with code-bearing extensions (`.exe`, `.bat`, `.ps1`, `.iso`, `.docm`, `.lnk`, …) requires explicit confirmation. Without this, a malicious selection like `C:\Users\you\Downloads\invoice.exe` could be one click away from running.
- **UNC path prompt.** Opening `\\server\share\…` paths prompts before contacting the remote host. Without the prompt, opening a UNC path on an attacker-controlled network could initiate an SMB connection that leaks your Windows NTLM hash to the named server.
- **No telemetry.** No analytics, no auto-update, no account.

## How it works

**Dedicated mouse-hook thread.** The low-level Windows mouse hook runs on its own STA background thread with its own dispatcher. UI thread work — WPF rendering, GC, layout — never delays mouse callbacks. Selection debounce uses `Environment.TickCount64` so NTP sync, hibernation resume, or manual clock changes never spuriously suppress or re-fire the hook.

**Automatic text capture is clipboard-free.** With the browser companion connected, mouse selections come directly from the focused page's Selection API or input selection offsets. SnapActions verifies the browser window, tab, document, frame and range before using the text. This path supports mixed Arabic/English and selections across lines without reconstructing character geometry.

Without the companion, mouse drag, double-click, and triple-click selection use `TextPattern.GetSelection` through the accessibility tree. SnapActions walks up to 6 parents of the focused element and also checks the element under the cursor. For Chromium, same-line drags reconstruct characters from their on-screen geometry and map visual bidi runs back to logical text order; double-click reconstructs the clicked word and requires the same UTF-16 length as the provider selection. This workaround has limits around mixed-direction content. Neither automatic path sends `WM_COPY`, injects `Ctrl+Insert`, or reads, clears, or writes the clipboard.

UI Automation coverage is not universal. Java Swing, some browser/Electron contexts, and custom text renderers may expose no selected text, so the automatic toolbar cannot appear there without a copy operation. A Chromium gesture fails closed when its geometry cannot be mapped safely (including cross-line bidi drags), when its range extends outside the provider's document, or when a double-click word cannot confirm the provider-reported selection length. Enable **Show toolbar when I press Ctrl+C** for those cases: your physical copy supplies the exact text, and SnapActions validates and reads the resulting clipboard value.

**Clipboard behavior is explicit.** Automatic highlighting never touches it. A physical Ctrl+C changes it because you requested a copy. Result previews close after a successful explicit copy; **Restore previous clipboard after copy action** can put the prior contents back after about 3 seconds.

**Editable-field detection.** Native Delete, Paste and Replace require an enabled control with affirmative writable evidence from UI Automation, plus the captured selection's exact text and endpoints at execution. Missing evidence keeps captured-text actions available but disables edits. Browser capture additionally checks the companion's editable flag and revalidates the captured document and selection. A caret or control type alone cannot authorize an edit.

Provider accuracy remains a limit: in the tested VS Code 1.113.0 screen-reader mode, a session-read-only editor reported writable text patterns. SnapActions offered Delete, which VS Code rejected without changing the document. With the default accessibility setting, the tested editor did not expose usable selection ranges; explicit Ctrl+C supplied text while editing pins stayed disabled. The [VS Code follow-up](docs/release-validation-2026-09-08.md#vs-code-read-only-follow-up) explains why this remains a known limitation.

**Per-monitor DPI throughout.** Toolbar positioning, hit-testing, and the sub-menu popup each look up the DPI of the monitor they're rendering on, including when the popup spills onto a different-DPI monitor than the toolbar.

**Guarded synthetic input.** Every path that injects input back into the user's app — transforms in editable fields, long-press paste-mode, Paste Plain Text, Delete — carries the original target's foreground and focused HWND, process/thread, and available UIA identity. It rechecks that identity and the captured selection before committing input. These checks reject observed target changes; Windows does not provide an atomic transaction covering another application's selection and subsequent keyboard input.

**When the toolbar appears (and when it doesn't).** Mouse-up after a drag, double/triple-click, or long-press *can* trigger the toolbar — but several gates have to agree before it shows. In order:

1. **NCHITTEST gate** (gesture-fire time) — gestures that started on a window's title bar, resize border, or native scrollbar are dropped. The hook can't tell those drags from a text-selection drag at the OS level, so we ask the receiving window via `WM_NCHITTEST` — deferred to fire time so only candidate selection gestures (not every click system-wide) pay the cross-process round-trip.
2. **Scrollbar-edge heuristic** (mouse-up) — a drag with both endpoints within ~25 px of the right (or left, in RTL layouts) edge AND primarily vertical is treated as a custom-scrollbar drag (Chrome, VS Code, Slack, Electron apps). Same with bottom edge + horizontal motion.
3. **Cursor-shape gate** (mouse-down + mouse-up) — the OS shows the text (I-beam) cursor over selectable text, a more universal signal than UIA TextPattern. I-beam at either point permits capture. A *hard* non-text cursor (resize, crosshair, wait, no-drop, …) at both points — resizing a window, a busy app, dragging a slider — is dropped before UIA work. Arrow, link-hand, custom, and unreadable cursors remain eligible because browsers and custom controls can display them over real selectable text.
4. **Excluded-app + self-PID checks** — anything in your Settings → Excluded apps list never sees a toolbar, and clicks on SnapActions's own toolbar are ignored.
5. **Browser or UIA selection read** — a connected browser companion supplies the current page selection. Otherwise, SnapActions checks the focused element's accessibility tree and then the element under the cursor. A known non-text item stops capture. Empty or unavailable data produces no toolbar and no clipboard fallback.

If a suppression case is misbehaving in your app, check the log file (`%AppData%\SnapActions\logs\YYYY-MM-DD.log`) — every gate that fires writes a line with the cursor position and reason. As an escape hatch, add the app's process name to **Settings → Excluded apps**.

## Build from source

```bash
git clone https://github.com/roko-tech/SnapActions.git
cd SnapActions
dotnet build SnapActions/SnapActions.csproj -c Release
dotnet test SnapActions.Tests/SnapActions.Tests.csproj
```

Build a complete verified package (Windows, .NET SDK 10.0.303, Node 22.23.1, and Python 3.11+):

```powershell
python tools/package.py
```

`SnapActions/build.bat` runs the same command. Each run writes a fresh directory under `artifacts`: a self-contained executable with companion sidecars, a ZIP, SHA-256 checksums, test receipts, and compiled WPF renders. It never replaces an existing installation. NuGet dependencies are locked; `global.json` and `.node-version` pin the toolchain.

For isolated manual testing, set `SNAPACTIONS_DATA_DIR` to an **absolute path** before starting the executable. Settings, logs, mutex and browser pipe then use that separate instance. Startup registration and browser registration are disabled for isolated instances. `--self-test` requires this override and runs without global hooks or clipboard writes.

## Tests & CI

The xUnit suite covers detection, transforms, native target/clipboard ownership, partial input, selection generations, UIA single-flight gates, lookup failures and caching, settings migrations, toolbar pinning/reordering/visibility, link cleaning, and recipe execution. Fetch action tests exercise the production service's JSON parsing, UTF-8 byte limits, cancellation and interrupted responses. Compiled WPF checks include settings write/replace failures, visible errors, retained saved preferences and retry/reload recovery. Retired WM_COPY/Ctrl+Insert capture-planner tests were removed with the inactive planner; explicit paste/delete safety tests remain.

The browser suite checks exact text, input/password restrictions, frames, navigation epochs, document-specific revalidation and protocol compatibility. CI runs the complete [package gate](tools/package.py), including the published executable's real UTF-8 native-host relay, desktop-disconnect recovery, and compiled WPF layout/state checks. See [the workflow](.github/workflows/build.yml), [CI runs](https://github.com/roko-tech/SnapActions/actions/workflows/build.yml), and [validation notes](docs/implementation-validation.md). Automated checks and compiled renders do not certify every live interaction; the remaining gaps are listed in the release notes.

## Local tools and customization

- **Clean tracking link:** previews removal of known tracking parameters while preserving remaining query bytes, duplicates and fragments. It does not remove generic parameters such as `ref` or `token`.
- **Saved text recipes:** Settings → Custom → Create text recipe. Add/reorder up to 12 existing pure text operations and preview sample text. Save once, then use it from Transform or the palette. Intermediates never touch the clipboard; oversized output or a failed step cancels the result.
- **App presets:** Settings → Apps → Configure app profiles. Reading, Writing and Development presets add hidden actions to the selected app, preserving existing choices. The editor includes built-in, search, custom and recipe actions.
- **Settings:** resizable sections with search, dark/light/system appearance, advanced timing controls and visible autosave status/failures. A failed save keeps the window open for retry.

OCR and AI rewriting remain deferred.

## Architecture

Selection events create an operation generation tied to the original target. `SelectionCoordinator` chooses the browser or UIA provider and returns an immutable `SelectionSnapshot`. The registry matches actions, the toolbar/palette presents them, and `ActionRunner` coordinates explicit effects. `ClipboardTransaction` owns native snapshots and rollback; `InputExecutor` owns guarded paste/delete. Automatic capture has no synthetic-copy branch.

`LookupService` owns HTTP response budgets, provider parsing and success-only caches. `ResultPopup` owns loading/success/empty/error/cancelled presentation and stale-retry suppression. Settings parsing normalizes semantic data before migrations; native host and test instances have explicit runtime paths.

Settings → Browser shows connection/capture health and a rolling 256-sample timing summary, without selected text. Timings separate event-time target identification, dispatcher queue, browser/UIA reads, validation, classification, matching and render-ready latency. Busy/timeout counters expose the cost of unavailable UIA providers. Render-ready excludes physical screen paint; these measurements are not a blanket performance claim. A hung UIA worker retains the single-flight gate to prevent thread accumulation. Process isolation remains conditional on measured provider hangs.

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

MIT — see [LICENSE](LICENSE). Release ZIPs also include the [license notices](licenses/README.md) for the bundled .NET runtime.
