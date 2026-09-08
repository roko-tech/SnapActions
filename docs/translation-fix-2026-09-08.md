# Inline translation repair — September 8, 2026

The chosen repair displays Google's normal, visible Translate website inside a native SnapActions popup through Microsoft Edge WebView2. It keeps translation beside the selected text and uses the service that worked in the live browser comparison, without opening an external browser tab.

This decision followed historical request replay, transport measurements, hosted-provider checks, a real WebView2 prototype, native on-device API detection and an isolated offline-model feasibility test. The measurements below describe those probes; final release validation is recorded separately at the end.

## What caused the reported timeout

Both the older v2.3.10 translation helper and the current MyMemory implementation used an eight-second request timeout. For English→Arabic they sent the same encoded query. Replaying both against the live service produced timeouts at approximately **8.01 seconds**. Giving the old helper a diagnostic thirty-second budget returned Arabic after **17.99 seconds**.

Other successful raw MyMemory requests took **17–24 seconds**, with most time spent waiting for the first response byte. DNS and TLS completed quickly; switching HTTP/1.1 to HTTP/2, adding an application User-Agent, requesting a single word or disabling machine translation did not remove the delay. A subsequent control received no response bytes within **25.00 seconds**, despite TLS completing at **0.36 seconds**.

The recent application refactor made timeouts visible instead of silently leaving `Loading...`; it did not introduce a thirty-second wait. Extending the timeout merely waited longer for the same slow request and was discarded. The evidence identifies a slow MyMemory response path on this PC at the time of testing. It does not establish MyMemory's internal cause or when the service changed from the user's earlier fast experience. The [earlier historical investigation](translation-latency-2026-09-08.md) contains the exact commits, requests and replay scope.

## Alternatives investigated

| Direction | Evidence and decision |
| --- | --- |
| Unofficial Google `client=gtx` endpoint | Six focused English/Arabic, multiline, Unicode, Japanese and Chinese probes all returned HTTP 429 with an automated-query rejection. Probing stopped; no successful translation or cross-language correctness was established. No proxy, identity rotation or alternate-host workaround was adopted. |
| Legacy Microsoft anonymous translation route | The old authentication host was blocked by the PC's existing local configuration, which was left intact. Separately, Microsoft documents that translation stopped working for Edge versions earlier than 136 after July 30, 2026. The legacy route was not a demonstrated working replacement. |
| Public/free services | Two documented LibreTranslate mirrors returned an administrative HTTP 403 or a connection timeout before any text was sent for translation. TartuNLP's live model list lacked Arabic. Unverified third-party relays did not provide sufficient evidence of trustworthy operation and the required language coverage. |
| Native WebView2 `Translator` API | The installed runtime exposed the real native function, but fresh-profile checks reported English→Arabic and Arabic→English as `unavailable`. API presence alone did not make these models usable. |
| Local Argos Translate | English↔Arabic worked offline with a preinstalled MiniSBD configuration and low warm inference latency. The dependency/model footprint, observed wording errors and licensing obligations made it unsuitable for this focused release. No offline assets or Python dependencies are bundled. |
| Visible Google Translate page in WebView2 | A fresh-profile native prototype loaded the normal Google website successfully and its Arabic result was observed on screen. This was the selected direction; Google's page owns translation and its controls. |

Microsoft's [known-issues notice](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-known-issues) supports the legacy-service limit. LibreTranslate publishes its [community mirror list](https://docs.libretranslate.com/community/mirrors/), and TartuNLP documents its [translation API](https://github.com/TartuNLP/translation-api). These documents establish intended interfaces; the probe receipts establish what was reachable and supported during this investigation.

The embedded page is not a hidden API relay or a scraper feeding translated strings into the old result control. It displays the service's actual website and normal interactions. A service rejection remains visible; the integration does not suppress anti-automation challenges or retry the rejected unofficial endpoint through another route.

## WebView2 and native-model evidence

The prototype used WebView2 Runtime **152.0.4191.66**, a separate fresh application profile and synthetic text: “The amber lantern is beside the window.” Its event log recorded successful HTTP 200 `NavigationCompleted` at **2.63 seconds from process startup**. Navigation began at approximately 0.66 seconds. The completed Arabic translation was observed in a desktop screenshot afterward.

**2.63 seconds is a page-navigation milestone, not a precisely measured translation-completion time.** The screenshot confirms visible translation for that case but cannot supply the missing exact timestamp. This probe does not establish a latency guarantee, every language pair or the behavior of the final packaged app.

A separate fresh-profile, app-owned HTTPS test page reported a secure context and native `Translator` function. `Translator.availability()` then returned `unavailable` for both `en-ar` and `ar-en`. No model download or native translation ran. Microsoft's [Translator API documentation](https://learn.microsoft.com/en-us/microsoft-edge/web-platform/translator-api) distinguishes API detection from language/model availability; the local result is specific to the tested runtime and profile, not a claim that all Edge/WebView2 installations lack Arabic support.

## Offline feasibility and why it was not bundled

Published Argos Translate 1.11.0 and official-index English/Arabic 1.0 models worked in an isolated Python 3.12 environment on Windows 10 build 19045. Selecting MiniSBD and preinstalling its two sentence-boundary models produced all six synthetic translations while a Python audit hook blocked network operations; the successful run attempted none. The stock Stanza-based setting attempted an online resource update and failed under that block.

The fresh process imported its runtime in **1.86 seconds**. First-model inference took **158–169 ms** and subsequent distinct inputs **22–52 ms**. This was process-cold startup after installation, not a machine-cold disk-cache test. The dependency/model downloads totaled about **386 MB**, installed files about **1.19 GB excluding Python itself**, and peak working set about **529 MiB**. “Notebook” became “book” in both directions, and one Arabic save-before-closing instruction changed meaning. Six examples are not a broad quality benchmark.

Argos advertises MIT/CC0 licensing, while the tested MiniSBD dependency is **AGPLv3**. The Arabic model archives identify training corpora and bundled Stanza models but contain no independent model license field/file; a request for clarification remains open upstream. These are deployment obligations and unresolved scope, not a declaration that all models share the Argos code license. See [Argos](https://github.com/argosopentech/argos-translate#license), [MiniSBD](https://github.com/LibreTranslate/MiniSBD#license), and the [model-license question](https://github.com/argosopentech/argos-translate/issues/533).

## User-facing behavior

- Translate opens the Google Translate website inside its own SnapActions popup. The initial selection is bounded to **500 UTF-8 bytes**, and saved source/target settings initialize the page.
- Google's visible controls handle language changes, swapping, editing and copying. The native popup supplies Close and page-load Retry.
- Online-lookup permission is required before the selected text and language choices are sent to Google. Dictionary continues to send its word to dictionaryapi.dev; currency lookup sends only the source currency code to open.er-api.com.
- Google handles translation timing and caching. SnapActions no longer applies its MyMemory request/cache behavior to Translate.
- The [Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section) is required for translation; the app's .NET runtime remains bundled. No paid API credentials or account setup are required. Website availability and network access remain external dependencies.
- New settings use **8 inline suggestions** by default. Existing saved limits and pin order are retained.

Microsoft documents WebView2's [embedding and lifecycle APIs](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/overview-features-apis) and its separate [Runtime distribution requirement](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution).

## Installed palette handoff race

Installed verification reproduced a separate entry-path failure: select text in the browser, open the action palette with **Ctrl+Shift+Space**, then press Enter on Translate. The action canceled even though its selection operation was still current. Opening Translate from the normal selection toolbar produced the Arabic result, and Escape closed the popup.

The palette hides itself and calls `GlobalHotkey.ReturnToTarget` before running the action. That helper requests foreground activation through `SetForegroundWindow`; returning successfully does not establish that the original focused child is already restored. At the failing validation point, diagnostics showed mismatches for the foreground window, focused child, process and thread, while `operation.IsCurrent` remained `true`. The action checked the browser selection during this focus transition and rejected it before translation started. This is distinct from the earlier MyMemory response delay.

The repaired palette handoff polls native activation readiness every **25 ms**, for at most **one second**. It requires the original foreground window, focused child, process, thread and current selection generation, then performs the existing exact browser/range validation once. It does not retry a changed selection into validity or relax the existing guards. Six regression tests cover immediate and delayed activation, a different focused child, a newer selection, invalidation during the readiness check, and one-shot rejection of a changed provider selection.

Live verification of the repaired candidate at the registered installed path repeated **Ctrl+Shift+Space → Enter Translate**. The inline Google popup showed the exact source, “The amber lantern is beside the window.”, and its correct Arabic translation while the browser companion was connected. This confirms the repaired entry path for that case. Native checks also verified Escape and outside-click dismissal. Final release source identity and installed-byte receipts remain separate from this interaction result.

## Local candidate validation

The repaired candidate passed **551 .NET tests, 12 browser-companion tests, 11 compiled WPF checks and 3 native-host protocol checks**, including the six palette-readiness regressions and popup rendering repair. Source identity, hosted CI, package hashes and installed-byte verification are recorded separately in the release's `release-validation.json`; these local checks do not certify those later steps in advance.

Live testing exposed a separate rendering problem at **250% display scaling**: the WebView child window had the correct bounds, but the WPF header and margins were blank. Software rendering restored them. The repair obtains the popup's native handle during `SourceInitialized` and sets its `HwndTarget.RenderMode` to `SoftwareOnly`; it leaves the rest of the application and the embedded browser's rendering settings unchanged. The compiled WPF check also verifies that setting when creating a hidden window handle. Subsequent live captures from the actual popup showed its native title, Close button, dark margins and resize grip in every case below.

| Synthetic case | Locally reviewed result | Observation from harness startup |
| --- | --- | --- |
| Arabic → English | “المفكرة الزرقاء على الطاولة.” became “The blue notebook is on the table.” | Visible by **3.814 s** |
| French → English | “Le petit bateau attend près du vieux pont.” became “The small boat is waiting near the old bridge.” | Visible by **2.824 s** |
| Mixed English/Arabic and emoji → Arabic | Preserved the garden meaning, Arabic phrase, 🌿 emoji, separators and sunrise meaning. | Visible by **8.325 s** |
| Near-limit English → Arabic | All **12 sentences / 492 UTF-8 bytes** reached the page, and its output contained all 12 correctly translated sentences. | Complete output present in the DOM by **3.827 s**, below the initial viewport |

The four fresh-profile runs used WebView2 Runtime **152.0.4191.66**, returned HTTP 200, closed normally and left the clipboard sequence unchanged. Times are sampled observations, not exact provider latency or guarantees. In the mixed-text run the browser was first observed ready at 5.820 seconds, so its entire 8.325-second startup-to-result measurement cannot be attributed to translation. The long-text runner reached its 15-second **visible-candidate** limit because the source text filled the viewport; its translation had already completed below the fold. That was a test visibility limit, not an application timeout. A scrolled native capture is a separate check.

Additional live checks exercised the actual popup's recovery behavior with test-only browser response injection:

- An HTTP 503 response displayed the native error and Retry button. Clicking Retry recovered the page; edited Arabic input “المكتبة مفتوحة اليوم.” then produced the visible English result “The library is open today.”
- A document response held for 12 seconds reached the application's **10-second page-load deadline**. The late response did not replace the timeout error. Retry loaded Google successfully and restored the correct English→Arabic translation.
- Replacing a popup while its old document response was still pending closed that window and showed the new silver-compass selection correctly. The old response completed afterward without replacing the new result or showing a stale error.

Native mouse and keyboard checks also verified Google's Swap control updating the saved language pair, direct Arabic text editing, and Copy returning the exact visible Arabic result. A private clipboard guard restored and verified all seven original clipboard formats after the Copy check. Scrolling the long selection exposed all twelve translated sentences and the page's copy controls.

These harness results exercise the production popup; the installed palette and dismissal checks are recorded above. Neither set establishes every language pair. A public-safe `live-validation.json` summarizes the reviewed observations without local user paths or settings; release identity and installed-byte results are recorded separately.

## Local receipts

The following relative links refer to **ignored local investigation files**, not committed or published artifacts. They contain probe evidence and are not required to run SnapActions.

- [Historical replay receipts](../artifacts/translation-20260908-0404/history/history-results.json) and [transport comparison](../artifacts/translation-20260908-0404/transport-comparison.json).
- [Provider research summary](../artifacts/translation-fix-20260908/provider-research/research.md) and [measured provider results](../artifacts/translation-fix-20260908/provider-research/provider-results.json).
- [Visible WebView2 event log](../artifacts/translation-fix-20260908/webview-probe/events.jsonl) and [native API availability log](../artifacts/translation-fix-20260908/webview-probe/native-events.jsonl).
- [Offline feasibility report](../artifacts/translation-fix-20260908/offline-probe/feasibility.md), [model metadata/hashes](../artifacts/translation-fix-20260908/offline-probe/model-results.json) and [successful offline inference](../artifacts/translation-fix-20260908/offline-probe/inference-MINISBD.json).
- [Reviewed live-case summary](../artifacts/translation-fix-20260908/live-validation.json) and [isolated four-case capture receipt](../artifacts/translation-fix-20260908/live-cases-final/runner-receipt.json).
- [Installed palette-to-Translate capture](../artifacts/translation-fix-20260908/desktop-final/071-Screenshot-1.png), showing the repaired hotkey/Enter entry path with synthetic text.
