# Live Windows validation — September 7, 2026

**Result: partial live coverage, with one reproduced and fixed runtime bug.** The desktop and real Brave native-messaging connection were exercised. Mouse gestures, visual appearance and complete Copy/Replace workflows could not be certified because of native automation failures.

## Runtime bug and correction

Restarting the desktop left the existing browser helper alive but disconnected. The replacement desktop had no connected peer, and the extension retained its old native port instead of reconnecting.

`BrowserNativeHost.RunAsync` waited for both forwarding tasks after one completed. The redirected browser stdin read stayed pending despite cancellation while Brave kept its port open. The host now exits its dedicated process when either forwarding task completes, with cancellation in `finally`, instead of waiting indefinitely for the other read.

The published-executable regression leaves browser stdin open after the test desktop pipe closes. It failed against `artifacts/roadmap-reviewed/publish/SnapActions.exe` with a five-second process-exit timeout, while the two existing round-trip cases passed. All three cases pass against the corrected package.

The real Brave restart was then repeated:

| Observation | Receipt |
| --- | --- |
| Fixed desktop and actual browser host connected | Desktop PID 28892; helper PID 42580; browser PID 20904 |
| Desktop stopped while Brave remained running | Original helper exited in 46 ms |
| New desktop launched | PID 12580; startup logged at 12:30:07.000 UTC |
| Fresh browser helper connected | PID 42572; connection logged at 12:30:08.120 UTC |
| Browser restart required | No |

Recovery took about 1.4 seconds from the previous desktop's shutdown log. This is one observed recovery, not a latency guarantee. Process identities and executable paths were checked before stopping test instances.

## Other live checks

- Launched the unreleased package under the existing settings, with a backed-up native-host manifest temporarily pointing to its executable. The companion's installed source path remained `D:\MyScripts\SnapActions\browser-extension`; its JavaScript files matched the package on disk.
- Typed `SnapActions live test: Hello World العربية 😀` into a newly opened Notepad window. Native accessibility later returned that exact document and selected text. The disposable editor was closed after testing.
- Verified that the new app owns Ctrl+Shift+Space: registration was occupied while it ran and available while it was stopped. The actual shortcut brought `SnapActions — Action palette` to the foreground. The final check verified that the foreground PID belonged to the corrected executable.
- Opened the local selection sample in the actual Brave profile and selected its exact 58-character multiline Arabic/English textarea value. Windows accessibility returned that selection. Invoking the shortcut opened the palette from Brave, but its source/result fields were inaccessible to the tool; this does not prove the installed companion's end-to-end selection result.
- Clipboard sequence remained **115** from the initial baseline through the final hotkey check. These keyboard/browser selection operations did not change the clipboard. No explicit Copy/Replace action or physical mouse gesture was successfully tested.
- No application error entries appeared in the captured test interval.

## Idle spot check

The corrected desktop, with its browser helper connected, was sampled without invoking actions:

| Metric | Observed value |
| --- | --- |
| Interval | 20.022 seconds |
| Process CPU time added | 0 ms |
| Working set at end | 288.0 MiB |
| Private memory at end | 170.1 MiB |
| Handles / threads | 1,648 / 23 |

There is no comparable baseline, long-running leak test, physical paint measurement or provider-latency distribution. These numbers do not establish an improvement over the installed version.

## Automated verification of the correction

The complete package gate passed with locked dependencies and warnings as errors:

- 483 .NET tests, zero failed/skipped.
- 12 extension tests, zero failed.
- Published native-host round trips: immediate connection, delayed connection, and desktop EOF with browser stdin still open.
- Published WPF self-test: Settings sections/themes/search, palette filtering/preview, read-only destination, toolbar overflow, lookup retry state, recipe editor and QR rendering.

Package used for this pre-release live session: `artifacts/live-reviewed/SnapActions-2.3.10-win-x64.zip`.

SHA-256: `dd24c712ddc3806a820ea044318c774a9ea8f3d5b976c7ecfed621218445fbde`.

The preceding `roadmap-reviewed` package does not contain the restart fix. No version bump, commit, push or release was performed during this live-test session. Version 2.4.0 was prepared afterward; see its [release notes](releases/v2.4.0.md).

## Coverage limits

The native screenshot call failed twice, including after fresh window selection, with `SetIsBorderRequired failed: No such interface supported (0x80004002)`. Mouse input failed with `coordinate input geometry is unavailable`. Direct value editing also failed with `Requested property was not in the CacheRequest (0x80070057)`.

Native keyboard input and accessibility reads worked on Notepad and Brave. However, the tool's app/window lists omitted SnapActions' floating palette even while the operating system reported it as the foreground window. Consequently its controls could not be targeted. Browser policy rejected the extension-management page; no alternate route around that restriction was used.

Still requiring live verification: physical mouse capture, correct palette source/preview, Copy and Replace outcomes, focus restoration and stale-target refusal, clipboard ownership during explicit effects, Settings interaction/autosave, local feature interaction/export, and multiple-monitor DPI behavior. Compiled renders and unit tests cover narrower contracts and do not replace these checks.

## Restoration and receipts

The original desktop and browser helper were restored from `SnapActions/bin/publish/SnapActions.exe`; the restored desktop log confirms a fresh Brave connection. Original settings bytes and native-host manifest bytes were verified unchanged/restored. Startup and browser registry entries were not edited. Temporary browser tabs, the disposable editor and the owned localhost fixture server were closed.

Local receipts are under `artifacts/live-20260907/`: `restart.json`, `hotkey.json`, `idle-sample.json`, `runtime.log` and the configuration backups. Packaged test results and WPF renders are under `artifacts/live-reviewed/checks/`.
