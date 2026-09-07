# Improvement pass — implementation and validation

This records the five-stage implementation and pre-release validation for [v2.4.0](releases/v2.4.0.md). OCR and AI rewriting are deferred at the user's request. Existing browser-companion and RTL work was retained and extended. Live testing temporarily switched the executable and native-host manifest, then restored the original setup. The version bump and release preparation followed that validation; the live-test report below preserves its historical build identity.

## Implemented scope

| Stage | Delivered behavior |
| --- | --- |
| Correctness | Shared signed-money parsing and supported currency table; strict UTF-8 Base64/hex failures; typed lookup results and timeout/retry handling; explicit translation language pairs and byte limit; semantic settings recovery; stable pin overflow. |
| Architecture | Immutable selection snapshot/coordinator; separate UIA reader, clipboard transaction and input executor; centralized action runner; lookup service independent of WPF; retired inactive synthetic-copy capture planner. |
| Browser and performance | Setup/health view, local selection sample, protocol version validation, document-targeted browser revalidation, bounded UIA/HTTP data, timing samples and UIA busy/timeout counters, isolated settings/log/mutex/pipe paths, package and CI gates. |
| UI | Ctrl+Shift+Space searchable palette, source/result preview, explicit Copy/Replace, useful transforms on read-only selections, searchable/resizable Settings, light/dark/system colors, accessible names, visible autosave outcomes and retry. |
| Local features | Offline QR images with copy/export, conservative link cleaning, saved/reorderable pure-text recipes, per-app presets including dynamic actions, Unicode/text inspection, independent language preferences. |

Paste As now uses the same enabled/app-filtered pure actions as the registry. It excludes destructive actions and applies no intermediate recipe result. Replacement retains native target, operation generation, clipboard sequence/ownership, partial-input and browser range checks. A failed settings save keeps Settings open instead of silently losing edits.

## Evidence

- Baseline: 463 .NET tests. Eleven new regressions failed before the lookup, currency, decoding and translation corrections.
- Final pure/contract suite: **483 passed, zero failed/skipped**, Release with warnings as errors. This includes 37 added cases across the regression/feature work and the retirement of 17 tests for the removed, unreachable capture planner. The active geometry, ownership, generation, partial-input and single-flight tests remain.
- Browser suite: **12 passed**, including document-targeted validation, changed ranges and incompatible protocol. These tests mock the extension APIs.
- Complete self-contained win-x64 publish: passed using locked NuGet dependencies, .NET SDK 10.0.303 and Node 22.23.1.
- Published executable: isolated native-host stdio/pipe tests passed for immediate connection, a 400 ms delayed connection after input arrived, and desktop disconnection while browser stdin remains open. Fragmented UTF-8 text survived exactly and hosts exited cleanly. The final case reproduces a hang found during the live app-restart test and failed against the preceding package.
- Published executable `--self-test`: all six Settings sections rendered in light/dark; narrow Settings render and search checked; palette filter and exact mixed-language preview checked; read-only Replace disabled; toolbar fits 400/600 DIPs with accessible More; timeout leaves Loading and offers Retry; late results cannot overwrite a retry; recipe editor and local QR render.
- QR PNGs were independently decoded with ZXing.Net and matched the original URL and Arabic/English/emoji payloads.
- Live local browser: the selection-reader function returned the exact 58-character multiline Arabic/English textarea value, exact formatted-editor selection, null for the parent document while the frame was focused, and the frame's exact selected input value from an isolated world. This used the actual DOM in the Codex browser; it did **not** validate installation of the packaged companion in Brave. A browser-tool MutationObserver error was observed; the sample contains no MutationObserver code.
- Compiled renders were inspected. An inherited tab accent affecting content text and a dark Expander label were corrected. The temporary browser tab and owned fixture server were closed.
- [Live Windows validation on September 7](live-validation-2026-09-07.md) confirmed palette opening without a clipboard change and real Brave native-host recovery after a desktop restart. It found and fixed a host shutdown hang. That validation used `artifacts/live-reviewed/SnapActions-2.3.10-win-x64.zip`; the earlier `roadmap-reviewed` package predates the fix. Release packaging uses version 2.4.0.

The package command writes the authoritative test TRX, `checks/ui/self-test.json`, WPF PNG renders, ZIP and SHA-256 manifests into its fresh output directory. Checks do not read production settings or write the user's clipboard.

## Limits and follow-up measurements

Physical selection-to-toolbar behavior, focus return and guarded replacement in external apps, clipboard invariance under real mouse gestures, and mixed-monitor DPI transitions remain unverified. Native keyboard controls were available for the live pass, but native screenshots and mouse input failed, and SnapActions' floating windows were omitted from the tool's target list. This prevented operating the palette's Copy/Replace controls. Browser policy also blocked the extension-management page, so the installed companion was not reloaded through its UI. See the release's linked GitHub Actions run for hosted verification of the release commit; the pre-release local checks alone do not establish its result.

The browser path now validates the captured document rather than every frame and avoids redundant pre-display reads. No numerical end-to-end speedup is claimed. The diagnostic view records real runtime samples for target identification, queue wait, provider reads, validation, matching and render-ready latency, plus busy/timeout counts. Render-ready excludes physical display paint. A 20.022-second live idle sample recorded 0 ms CPU time, 288.0 MiB working set and 170.1 MiB private memory; this is a single-process spot check without a baseline or leak/stress coverage. Production-provider latency still needs measurements.

A restartable UIA helper was conditional in the review, not an unconditional rewrite. There is no new evidence of a permanently hung provider in this pass. The existing single-flight gate continues to hold until the underlying call exits, preventing abandoned-worker accumulation; process isolation remains a future response if live measurements justify it.

## Provider and build contracts

- [MyMemory request specification](https://mymemory.translated.net/doc/spec.php): explicit source/target pair and 500-byte UTF-8 request budget.
- [Dictionary provider documentation](https://dictionaryapi.dev/): the documented English endpoint is offered; unsupported saved preferences are visible and do not silently substitute English.
- [Chrome scripting InjectionTarget](https://developer.chrome.com/docs/extensions/reference/api/scripting#type-InjectionTarget): document targeting requires Chromium 106 or newer.
- [QRCoder](https://github.com/codebude/QRCoder) and [ZXing.Net](https://github.com/micjahn/ZXing.Net): local encoding and independent round-trip verification.
