# September 7 stabilization validation

Historical status at the end of this pass: the prioritized repairs were implemented in the working tree and validated locally. The installed executable, production settings, startup entry and native-host registration remained unchanged. This was an unreleased build using the existing **2.4.2** version, including the user's pre-existing pending toolbar work. No commit, push or installation was performed during this pass.

This is the earlier stabilization receipt. The subsequent [compatibility validation](compatibility-validation-2026-09-07.md) records the newer package, Notepad/VS Code coverage and additional Chromium range repair. The [September 8 release validation](release-validation-2026-09-08.md) records the later Brave checks and VS Code follow-up.

## Repairs

| Problem | Resulting behavior | Main implementation |
| --- | --- | --- |
| A native selection changed inside the same control while its old toolbar stayed open | Native edits require the captured UIA selection endpoints and exact per-range text to match. Identical text at a different position is rejected. Provider work remains bounded, and the final target identity is captured after that work. | [UIA capture](../SnapActions/Core/UiaSelectionProvider.cs), [operation](../SnapActions/Core/SelectionOperation.cs), [input](../SnapActions/Core/InputExecutor.cs), [target boundary](../SnapActions/Core/ForegroundGuard.cs) |
| Read-only native text exposed Delete and Paste | Enabled/writable evidence from ValuePattern or TextPattern replaces the caret/control-type assumptions. Missing or inconsistent evidence permits captured-text actions but cannot authorize target edits. Explicit Ctrl+C and empty-field paste mode bind the same evidence. | [editability](../SnapActions/Core/ForegroundApp.cs), [coordinator](../SnapActions/Core/SelectionCoordinator.cs), [capture triggers](../SnapActions/Core/SelectionTracker.cs) |
| Setup buttons sent filesystem paths to the web-URL opener | Both buttons use the existing local-path opener. | [setup buttons](../SnapActions/UI/SettingsWindow.Sections.cs) |
| XML declarations and indentation could expand small input substantially | DTD declarations are prohibited, external resolution is disabled, input is limited to 32,768 characters, and serialization stops before output exceeds 65,536 characters. | [XML formatting](../SnapActions/Actions/ContextActions/FormatXmlAction.cs) |
| Browser fallback removed selected boundary whitespace | Logical mapping and visual fallback retain selected spaces and tabs. Whitespace-only selections remain ineligible. | [UIA fallback](../SnapActions/Core/UiaSelectionProvider.cs), [regressions](../SnapActions.Tests/CapturePolicyTests.cs) |
| A failed result Copy consumed its one-shot UI gate | A current operation may retry a known Copy failure. Cancelled/uncertain replacements remain consumed. | [action result](../SnapActions/Actions/IAction.cs), [runner](../SnapActions/Actions/ActionRunner.cs), [result popup](../SnapActions/UI/ResultPopup.xaml.cs) |
| Protocol rejection tests omitted the required version, so they never reached the intended guard | Each negative case now has valid prerequisite fields. Removing the status check makes the inactive-reply test fail. | [protocol tests](../SnapActions.Tests/BrowserSelectionTests.cs) |
| No matching actions suppressed the entire toolbar | Automatic and explicit-copy capture still show Copy and customization. This was found during the physical stabilization checks. | [capture triggers](../SnapActions/Core/SelectionTracker.cs) |

Automatic selection capture remains clipboard-free. No clipboard fallback, OCR, AI rewriting, QR Code or Inspect Text was added. Providers that cannot reliably expose writable selection ranges can consequently lose Replace/Paste/Delete availability while retaining Copy.

## Automated evidence

- `rtk proxy python tools/package.py --output artifacts/stabilization-20260907-1543/package-final` passed.
- **510 .NET tests passed**, with zero failures or skipped cases. The package gate builds with warnings as errors and locked dependencies.
- **12 browser tests passed**. These exercise the selection reader and mocked extension lifecycle; they do not establish live installed-extension behavior.
- Compiled WPF checks passed for Settings in both themes, narrow layouts, palette preview/read-only destinations, toolbar width, customization, hover-preview lifecycle, lookup timeout/retry state and recipe resources.
- The published executable passed all three native-host process tests: immediate connection, 400 ms delayed connection, and desktop disconnection while browser stdin remains open. UTF-8 text round-tripped and hosts exited.
- The intentional protocol mutant failed exactly the inactive-reply case; its other seven malformed-reply cases passed. Only a disposable source copy was mutated.
- Before the XML and whitespace repairs, six targeted regression cases failed. New native-operation tests cover missing/changed capability evidence, exceptions, invalidation, rebinding and single admission after retry. Physical tests below exercise real UIA ranges in addition to these unit seams.

## Physical Windows evidence

Tests used a disposable WPF fixture on Windows 10 Pro 19045, a 3840×2160 display at 250% scaling, and Windows-MCP with Pillow screenshots. Screenshot coordinates were scaled by the reported factor of two. The fixture recorded its own text, selection positions, focus and clipboard sequence every 100 ms when state changed. Production settings were cloned into an isolated data directory; automatic dismissal was disabled to permit observation.

Most fixed-state checks used `package-initial`. After adjusting the final-target capture order and removing the empty-action early returns, `package-final` repeated changed/valid native Delete and both empty-toolbar capture triggers. The Copy retry, XML, setup-button and customization implementations were unchanged between those packages.

| Check | Observed result |
| --- | --- |
| Pre-fix changed selection | Select `Alpha `, then Ctrl+A, then click the old Delete action: the installed binary deleted the whole `Alpha Beta Gamma` field. |
| Fixed changed selection | The same operation was rejected and both fixture fields stayed intact. The final package also rejected a 0+6 → 0+17 selection change. |
| Identical text at another position | Moving the native selection from the first `Alpha ` to the second, without a new capture gesture, caused Delete to reject. |
| Unchanged selection | Delete removed only `Alpha `, leaving `Beta Gamma`. This passed on both the initial and final fixed packages. |
| Read-only selection | Copy stayed available; saved Delete and Paste pins became disabled. |
| Wrong replacement target | Open UPPERCASE preview for field A, press Tab to focus field B, then Replace: cancellation, both fields unchanged. |
| Valid replacement | UPPERCASE → Replace changed only the selected word in field A, leaving field B intact. |
| Clipboard-free capture | Automatic selections and Delete did not advance the clipboard sequence. The final package's changed/valid Delete checks held sequence 275 throughout. |
| Rich clipboard on cancellation | Synthetic UnicodeText, HTML and RTF retained the same content hashes and sequence after the rejected replacement. This does not claim that a successful explicit Copy/Replace preserves the previous clipboard. |
| Busy Copy retry | A separate fixture held OpenClipboard. Copy displayed an error and enabled retry. After unlocking, the same popup copied exactly `ALPHA `; both fields stayed unchanged. |
| Preview reuse | Reopening the UPPERCASE result after Copy and after a rejected replacement produced usable new result controls. |
| Customization | A palette action was dragged onto the toolbar, reordered, hidden, and retained after restart. A disabled Delete pin was also dragged successfully. Saved settings were byte-identical across the restart. |
| Empty toolbar | The initial fixed package reproduced complete suppression. The final package showed Copy/customization after both automatic selection and real Ctrl+C, and its customization menu opened. |
| Setup buttons | Open extension folder launched Explorer at the packaged companion directory. Open selection sample launched the packaged HTML in Brave. |

The original clipboard was snapshotted in memory and restored successfully after each of two clipboard-testing rounds, only while the accepted test payload still owned it. Its contents were not written to the evidence log. Owned fixture/test processes and the Explorer window were closed. The opened sample browser tab remains available.

## Artifact identity and preservation

Local receipts are under `artifacts/stabilization-20260907-1543/`: the pre-task source snapshot and diff, `implementation.diff`, native screenshots, `events.jsonl`, `clipboard-events.jsonl`, `pin-persistence.json`, the protocol-mutant result and `final-integrity.json`. The final package directory contains the test TRX, WPF renders, self-test JSON, executable, ZIP and checksum manifests.

| Artifact | SHA-256 |
| --- | --- |
| Installed executable, preserved | `e16afdb74667a3e9948883eb976590f2385b07c68d2daf49dffc46a1774c9cff` |
| Final executable | `e6432dd47640ef0bba936d6f5110828439de50cfc93f2cb2b5ea9085895c9bdb` |
| Final `SnapActions-2.4.2-win-x64.zip` | `b45d3b0b155dabc190060c1298bc74beba841b3c78b7052d959591c3bd88b14b` |

HEAD remains `20140ba03ae8cb03b7e82217817a64b5d9f259c4`. Final integrity checks confirmed the installed executable and user settings hashes, startup value, Chrome/Brave native-host registration and manifest contents, and absent Edge registration. Files outside the explicitly reviewed repair scope retain their pre-task bytes or deletion state. Existing pending toolbar customization and intentional feature removals were retained.

## Remaining limits

Automatic approval review blocked browser-tool access to the local sample page under its URL policy. The restriction was not bypassed. A real browser-to-new-package selection/reconnection run remains unverified; native-host process tests and browser unit tests are separate evidence. Extension-management access was also blocked during the preceding review.

The physical checks cover this WPF provider, not every Win32, Electron or browser editor. Mixed-monitor DPI transitions, sustained latency/leak measurements, clipboard-manager races and physical partial-SendInput failures remain outside this validation. Pure fallback tests cover boundary whitespace and mixed Unicode; this pass does not claim new live Chromium BiDi coverage. UIA checks reduce stale-target risk but cannot make an external application's selection and Windows input atomic. No release-wide readiness or hosted-CI claim is made.
