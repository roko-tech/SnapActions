# September 7 compatibility validation

Historical status at the end of this pass: the stabilization candidate had stronger automated and real-app evidence, and one additional capture defect was repaired. It remained local and unreleased; nothing was installed, committed or published during this pass. The subsequent [September 8 release validation](release-validation-2026-09-08.md) completes Brave selection/reconnection checks and documents the VS Code read-only limitation.

This pass builds on the [stabilization receipt](stabilization-validation-2026-09-07.md). HEAD remains `20140ba03ae8cb03b7e82217817a64b5d9f259c4`. Existing pending customization and intentional QR Code / Inspect Text removals are included and preserved. Artifacts below are local, ignored files under `artifacts/compatibility-20260907-1650/`.

## Changes and automated coverage

### Production FetchText coverage

[FetchTextTests](../SnapActions.Tests/FetchTextTests.cs) adds 18 cases using `LookupService.FetchText` with an injected HTTP handler and real response streams:

- Nested JSON fields; string, number, object and array results; missing fields and non-object parents.
- Invalid JSON through the execution wrapper used by the result popup.
- Raw text beyond the removed helper's 4,000-character limit, preserving boundary whitespace and mixed Unicode.
- Exactly 65,536 UTF-8 bytes accepted and 65,538 rejected, both with and without Content-Length. Two-byte Arabic characters distinguish a byte budget from a character budget.
- Invalid URLs rejected before a request; cancellation during body reading; partial-body I/O failure; response-stream disposal.

The unused `UserRecipeAction.ExtractField` helper and its five helper-only cases were removed. Production fetch behavior did not need changing. Tests use synthetic responses; this is not an availability check of external services.

### Settings-save failures

[PackageSelfTest](../SnapActions/Diagnostics/PackageSelfTest.cs) now exercises production SettingsManager, ToolbarPreferences and toolbar error feedback on the WPF dispatcher, within an isolated data directory. A directory at `settings.json.tmp` blocks the write; a file handle without delete sharing blocks replacement of the saved file.

Both cases must report failure, display the save error, retain the pending in-memory pin order and hidden actions, and preserve the previous saved bytes. Reload must recover the complete last successful preferences. Removing the blocker and retrying must clear the error, remove the temporary file and persist the new preferences across reload. These checks passed; persistence production code did not need changing.

### Chromium ranges outside the document

A physical drag over `  English داخل العربية 👩‍💻  ` in VS Code returned that exact selection through UIA. However, `RangeFromPoint` returned the unrelated private-use glyph `U+EC21`. Its end compared after the editor's document end. The automatic drag path accepted the glyph because it allowed a reconstructed drag length to differ from the reported selection length. Clicking Copy copied the glyph; native edits were already disabled for that mismatched capture.

[UiaSelectionProvider](../SnapActions/Core/UiaSelectionProvider.cs) now checks expanded mouse-derived word and line ranges against both document endpoints. An out-of-document range returns no gesture text, so capture is rejected without a clipboard fallback. The existing mixed-direction reconstruction rules remain in place.

The disposable compiled `selection-probe` calls the production provider against the live VS Code control and sets PerMonitorV2 DPI awareness. `vscode-outside-document-before.json` records `HasText` with `U+EC21`; `vscode-outside-document-after.json` records `UntrustedText` with null text against the same selected document and coordinates. A real drag in the final published executable also produced no toolbar and left the synthetic clipboard sequence unchanged. Explicit Ctrl+C followed by toolbar Copy then returned the exact mixed-text line, including emoji and both pairs of boundary spaces. This is a real-provider regression receipt; the xUnit suite does not recreate VS Code's faulty provider.

## Package gate and identity

`rtk proxy python tools/package.py --output artifacts/compatibility-20260907-1650/package-final` passed:

| Check | Result |
| --- | --- |
| .NET Release tests | 523 passed, zero failed or skipped |
| Browser companion tests | 12 passed; extension APIs are mocked |
| Compiled WPF self-test | 10 categories passed, including settings write/replace failures |
| Published native-host processes | Immediate pipe connection, 400 ms delayed connection, and desktop EOF while browser stdin remains open all passed; UTF-8 text survived and hosts exited |
| Restore/build | Locked dependencies and warnings as errors |

| Artifact | SHA-256 |
| --- | --- |
| Final executable | `73a9382e4f26bd7eeeada6496b9f6f8085555bbdf85fc9c4a2e3f8d09f4b84ec` |
| Final `SnapActions-2.4.2-win-x64.zip` | `9a06af5a8826d73caa6d230c062fc41f3ea9a6d8df1a00294069e1508554b1a4` |
| Installed executable, preserved | `e16afdb74667a3e9948883eb976590f2385b07c68d2daf49dffc46a1774c9cff` |

`package-initial` passed the same automated counts but predates the document-boundary repair. Its ZIP hash is `14462903cc8514cc52bb5e71b8b8f5a06629e9d2f343662d9e823600905e7ddb`. The final candidate differs in that Chromium geometry guard; the Notepad/native edit and explicit Ctrl+C paths exercised below are unchanged between the two packages. Documentation was updated after packaging and is not included in the ZIP.

## Physical application checks

Windows 10 Pro 19045, 3840×2160 at 250% display scaling. Windows-MCP used Pillow screenshots with a reported coordinate factor of two. Application input was physical mouse/keyboard automation. Separate read-only probes verified the owned target HWND/PID before recording synthetic document text, selection positions, clipboard sequence and focus. Notepad used bounded Win32 Edit messages; VS Code used UIA. No application buffer was edited through those probes.

| Application and condition | Observed result |
| --- | --- |
| Notepad 10.0.19041.1, automatic double-click then Ctrl+A then old Delete | Old selection rejected; whole document and clipboard sequence 294 unchanged |
| Notepad, fresh selection then Delete | Only selected `Alpha ` removed; clipboard sequence unchanged |
| Notepad, UPPERCASE then Replace | Only selected `Alpha ` changed to `ALPHA ` |
| Notepad, selected Gamma then Paste | Only the selected word replaced with the accepted clipboard text |
| Notepad, physical mixed-text drag then toolbar Copy | Exact `  English داخل العربية 👩‍💻  ` copied; automatic capture left the clipboard unchanged |
| VS Code 1.113.0, isolated profile, screen-reader support on | First automatic word capture appeared. Ctrl+A then the old Delete rejected the changed selection; document and clipboard sequence 337 unchanged |
| VS Code, later automatic double-clicks | Rejected when point-based word geometry disagreed with the selected range; automatic capture was not consistently available |
| VS Code, physical Ctrl+C then Delete / UPPERCASE→Replace / Paste | Gamma deleted; only the second Alpha uppercased; selected first Alpha replaced with a multiline Arabic/English/emoji clipboard sentinel. Document receipts verify each result |
| VS Code, mixed-text automatic drag | Initial package copied an unrelated glyph. Final package rejected the out-of-document geometry without touching the clipboard |
| VS Code, final package, explicit Ctrl+C then toolbar Copy | Exact mixed text and boundary spaces copied, even after the clipboard was deliberately reseeded before clicking Copy |
| VS Code, default accessibility setting, session-read-only editor | UIA exposed an accessibility-help field instead of usable editor selection ranges. Ctrl+C supplied exact text; Delete/Paste pins were disabled, clicking Delete changed nothing, and toolbar Copy remained usable |
| VS Code, screen-reader support on, session-read-only editor | Both UIA ValuePattern and TextPattern reported writable. Ctrl+C exposed enabled Delete/Paste; clicking Delete caused VS Code's own read-only message. Exact document and selection stayed intact. This remains a compatibility defect |

Notepad and the first VS Code editing checks used `package-initial`. The repeated faulty-geometry capture, exact Unicode Copy and read-only checks used `package-final`. The tested VS Code profile disabled extensions, updates and telemetry and used a disposable sample; no user editor settings were changed. The default-setting writable editing path was not certified.

## Remaining compatibility limits

1. **Brave end-to-end selection/reconnection is unverified for this candidate.** Automatic approval review previously rejected browser-tool access to the local sample and extension-management pages under its URL policy. The restriction was not bypassed. Browser unit tests and native-host subprocess tests do not establish the installed companion's behavior. The isolated test desktop used a separate pipe and was not connected to the installed browser host.
2. **VS Code's session-read-only status is unreliable in its UIA provider.** In screen-reader mode it exposed writable patterns for a locked editor. SnapActions therefore offered an edit that VS Code rejected. No document mutation was observed, but disabled-action accuracy is not established. A follow-up needs trustworthy capability evidence; guessing from a localized window title or removing valid editing support for all Electron controls would not be a supported repair.
3. **Automatic Electron coverage remains limited.** The document-boundary fix rejects the reproduced unrelated range; it does not repair the provider's geometry or promise that the automatic toolbar always appears. Ctrl+C provides the verified text path. No new live Chromium BiDi or valid-browser-geometry claim is made.
4. Mixed-monitor transitions, sustained latency/leak measurements, clipboard-manager races, physical partial-input failures and hosted CI for these uncommitted changes remain outside this pass. UIA validation and subsequent Windows input cannot be atomic across processes.

These were the outstanding release checks at the end of September 7. Their follow-up is recorded in the [September 8 report](release-validation-2026-09-08.md).

## Preservation and receipts

Three clipboard-testing rounds restored the original clipboard from an in-memory snapshot, only while the accepted test payload still owned it. The original payload was never written to the evidence log. The wrong glyph is logged separately because it was produced by the test candidate and was part of the reproduced defect. A synthetic format-hash probe once returned an empty payload despite subsequent exact-text acceptance; that probe is not used as evidence of rich-format preservation.

The owned test candidate, clipboard guard, synthetic Notepad buffer and isolated VS Code process tree were closed. The normal desktop application was not running at the start and was left stopped. The installed browser host was not stopped or re-registered; its PID changed while the browser was running. Production settings, the installed executable, startup value and Chrome/Brave native-host manifest data were preserved; Edge registration stayed absent.

Receipts include `baseline.json`, `working-tree.diff`, the pre-task `snapshot/`, the pass-specific `implementation.diff`, `final-integrity.json`, `runtime-before.json`, `runtime-after.json`, `cleanup.json`, native screenshots with coordinate metadata, `notepad-*.json`, `vscode-*.json`, `clipboard-events.jsonl`, and the compiled live-provider probe. `package-final/checks/` contains the test TRX, WPF self-test JSON and renders. The combined pending toolbar/settings/action changes were rechecked for integration with native input validation and the new failure coverage; existing unrelated pending bytes and deletion states were retained.
