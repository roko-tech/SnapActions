# September 8 release validation

The remaining Brave checks passed against the stabilization candidate. The VS Code investigation found a concrete reason for its misleading read-only capability, but no trustworthy replacement signal; that behavior remains an explicit compatibility limitation. No production source changed during this follow-up.

This report extends the [September 7 compatibility pass](compatibility-validation-2026-09-07.md) and [stabilization pass](stabilization-validation-2026-09-07.md). Local evidence is under `artifacts/release-20260908-0318/`. The candidate's executable SHA-256 is `73a9382e4f26bd7eeeada6496b9f6f8085555bbdf85fc9c4a2e3f8d09f4b84ec`; its source was the reviewed working tree based on `20140ba03ae8cb03b7e82217817a64b5d9f259c4`. Both the earlier installed build and this candidate carry version 2.4.2, so the version alone does not identify them.

## Live Brave checks

Windows 10 Pro 19045, 3840×2160 at 250% display scaling; Brave executable version 152.1.94.121. Physical mouse and keyboard input used Windows-MCP 0.8.5 with Pillow screenshots and the reported coordinate factor of two. The browser tool prepared synthetic text and read the actual textarea values and selection offsets. The tested controls were existing examples on [MDN's textarea page](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/textarea), including an embedded editable example and a separate read-only example.

The existing installed companion connected to a native host launched from the same candidate path as the desktop app. The native-host manifest was temporarily pointed at the candidate; the original was backed up before modification. The normal five-second toolbar dismissal worked, but two delayed test clicks missed it. Dismissal was temporarily set to Never for the remaining physical checks, with every other preference retained. These setup changes were restored after testing.

| Check | Observed result | Local receipt |
| --- | --- | --- |
| Automatic mixed-direction selection | Dragging over `  English داخل العربية 👩‍💻  ` displayed the toolbar. Selection and toolbar display did not advance the clipboard sequence. | `desktop/011-Screenshot-1.png` and SDK request metadata |
| Exact Copy | Toolbar Copy produced exactly that text, including emoji and both pairs of boundary spaces. The guard confirmed stable clipboard ownership by the candidate process. | `desktop/013-Screenshot-1.png`, `clipboard-events.jsonl` |
| Changed selection | Capture the first `Alpha `, then Ctrl+A without a new capture gesture, then click the old Delete. SnapActions displayed cancellation; the entire 48-UTF-16-unit value and selection remained unchanged. | `desktop/023-Screenshot-1.png` through `027-Screenshot-1.png`, `brave-dom-results.json` |
| Valid Delete | Capture `Alpha` at offsets 31–36, then Delete. Only those five units were removed; the caret collapsed to 31. | `desktop/031-Screenshot-1.png`, `033-Screenshot-1.png`, `brave-dom-results.json` |
| Read-only field | The actual DOM had `readOnly=true`. Automatic selection showed Copy while Delete/Paste were disabled. Clicking disabled Delete left the field unchanged. | `desktop/036-Screenshot-1.png`, `038-Screenshot-1.png`, `brave-dom-results.json` |
| Restart/reconnect | The candidate restarted at 03:30:03 UTC; hooks were installed at 03:30:04.344 and the existing Brave process reconnected at 03:30:04.556. Mixed-text capture and Copy then succeeded without restarting Brave or reloading the extension. | `candidate-restarted.json`, production log timestamps, screenshots 011/013 |

The stale/valid Delete and read-only checks retained clipboard sequence 500 throughout. Copy was an explicit clipboard mutation. The live pass establishes these browser paths; it does not claim every contenteditable implementation, cross-line BiDi gesture, or browser version. An earlier stale-selection attempt had focus interference from a test helper and is excluded from the clean result above.

## VS Code read-only follow-up

The earlier physical pass used VS Code 1.113.0. With screen-reader support enabled, a session-read-only editor exposed writable ValuePattern and TextPattern attributes. SnapActions therefore offered an edit, which VS Code rejected without changing the document. With default accessibility settings, the tested editor lacked usable selection ranges; physical Ctrl+C supplied exact text while native edit pins stayed disabled.

Inspection of that installed version's workbench bundle found:

- `domReadOnly` defaults to false and is separate from the editor's `readOnly` option.
- The textarea's native `readonly` attribute is set when IME is disabled, or when both `domReadOnly` and `readOnly` are true. A session-read-only configuration sets `readOnly` and its message, without enabling `domReadOnly`.
- The bundle contains no `aria-readonly` attribute. Its autocomplete state is also changed by suggestion handling, so it is not a trustworthy writability signal.

The installed bundle SHA-256 is `2890a6506fe8f5e43166f38a07875b3630cb49a8de10275a69a6e2a76c8a81ea`; exact offsets and excerpts are in `vscode-readonly-source.json`. Microsoft's [textarea implementation](https://github.com/microsoft/vscode/blob/main/src/vs/editor/browser/controller/editContext/textArea/textAreaEditContext.ts) and [editor options](https://github.com/microsoft/vscode/blob/main/src/vs/editor/common/config/editorOptions.ts) explain the separate flags; those links track upstream, while the local hash pins the inspected build.

This is consistent with the observed UIA mismatch. No reliable new native capability signal was established, so no VS Code-specific title or autocomplete heuristic was added. The already-tested document-boundary guard remains: it rejects the unrelated out-of-document glyph returned during an automatic VS Code drag. It does not repair the editor's provider geometry or promise automatic capture in all Electron controls.

## Release build boundary

The live-tested candidate passed 523 .NET tests, 12 mocked browser tests, 10 compiled WPF check categories and three real native-host subprocess cases in the September 7 package gate. This follow-up changes documentation only. Final publication requires a fresh `tools/package.py` run from the committed source and a successful hosted Windows workflow for that same commit. The [v2.4.2 GitHub release](https://github.com/roko-tech/SnapActions/releases/tag/v2.4.2) records the final commit, workflow and downloadable package checksums; its executable embeds the release commit and is distinct from the pre-commit live-test candidate.

The earlier physical WPF/Notepad checks cover exact native selection validation, wrong-field rejection, valid Delete/Replace/Paste and known Copy-failure retry. Mixed-monitor transitions, sustained latency/leak measurements, clipboard-manager races, and physical partial-SendInput failures remain unverified. Provider checks and subsequent Windows input cannot form an atomic transaction across processes.

## Restoration and test-infrastructure incident

The desktop automation service timed out during a full Snapshot and terminated the clipboard guard that held the original clipboard backup only in memory. That backup was lost and the original clipboard could not be restored. The user was informed during the test. The last verified clipboard value was synthetic mixed-language test text; the original payload was not logged. A later isolated SDK session completed the remaining native input and screenshot checks. No successful clipboard-restoration claim is made for this pass.

The candidate desktop and helper were stopped, the temporary browser page and SDK session were closed, and the normal desktop application was left stopped as it was at the start. Production settings and the native-host manifest were restored byte-for-byte; startup and Chrome/Brave registry values retained their original contents, with Edge registration absent. The installed executable was not replaced. `runtime-before.json`, `runtime-after.json`, `restored-state.json` and the final integrity receipt record that boundary.
