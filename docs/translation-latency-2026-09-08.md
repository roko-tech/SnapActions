# Translation latency investigation — September 8, 2026

This records the diagnosis of the MyMemory implementation before v2.4.3. The subsequent repair and broader provider checks are documented in [Translation fix](translation-fix-2026-09-08.md).

The repeated popup timeout is explained by the current eight-second client limit and a much slower MyMemory API response. SnapActions uses MyMemory for inline translation. Google Translate's website uses a different service; its responsiveness does not establish the speed of MyMemory's API.

## Measurements on this PC

Requests used synthetic English text translated to Arabic. No user's selection or clipboard contents were read for the probes. The initial installed desktop was the older local build `2.4.2+20140ba03ae8cb03b7e82217817a64b5d9f259c4`; the current source baseline was release commit `5139a7310f91af8abc5be125985e7bf8ce12e016`. The translation request implementation was unchanged between them.

| Probe | Observed time/result |
| --- | --- |
| Production `LookupService.Shared` with its existing deadline | Timed out at 8.03 seconds |
| Direct MyMemory request, `Hello world.` | HTTP 200 with a valid translation after 23.74 seconds |
| Direct request for a longer sentence | HTTP 200 after 17.16 seconds |
| Same sentence, `mt=1` | 18.37 seconds; machine translation returned |
| Same sentence, `mt=0` | 18.30 seconds; no translation match returned |
| Single-word `Hello` | 17.94 seconds |
| Missing-query validation request | 10.58 seconds merely to return the API's missing-query error |
| HTTP/1.1, default headers | 18.38 seconds |
| HTTP/2, successfully negotiated | 18.38 seconds |
| HTTP/1.1 with an explicit SnapActions User-Agent | 18.37 seconds |
| Google Translate website HTML | 0.97 seconds; this is page download time, not a measured translation-completion time |

The controlled curl cases resolved DNS in about five milliseconds and completed TLS in 0.30–0.37 seconds. Almost all the delay occurred after the request was ready and before the first response byte. Body transfer and parsing were negligible. The same MyMemory URL also exceeded the browser tool's ten-second navigation wait in Brave. Google Translate was opened with the same synthetic sentence and its completed Arabic translation was observed.

These results locate the dominant delay in the MyMemory request path beyond initial connection setup, rather than in WPF rendering, body processing, HTTP version or a missing application User-Agent. They do not establish whether MyMemory's internal cause is queueing, regional routing, throttling or another upstream condition. Responses declared `quotaFinished=false`; a Retry-After header alone does not prove that the user's quota caused the delay.

## What changed from the earlier version

Git history and a replay of the historical translation helper distinguish application changes from the currently slow request path:

- The initial commit, `6d2179c` (June 29), already used MyMemory and an eight-second `HttpClient.Timeout`. SnapActions did not recently switch inline translation from Google to MyMemory.
- `25157ec` (July 5, v2.3.6) replaced `autodetect` with a source inferred from the text's script where the source and target scripts differ. English text translated to Arabic therefore already sent `en|ar` in v2.3.10.
- `d815e04` (September 7, v2.4.0) added explicit source/target controls and moved the request into `LookupService`. The old target came from `SearchLanguage`; the new target has its own setting. With English and Arabic selected, the encoded request URL is identical to v2.3.10. The connection/header timeout remains eight seconds. Bounded streaming adds an eight-second body limit, which is not the limiting stage in these probes because the first response byte arrives much later.
- The same v2.4.0 change made timeout errors visible. Previously, `ResultPopup.RunFetchAsync` swallowed every `OperationCanceledException`, including `HttpClient.Timeout`, leaving the popup on `Loading...`. The current code distinguishes user cancellation from timeout and displays the error plus Retry. This explains a change in failure presentation; it does not explain away the user's earlier successful, fast translations.
- Both versions cache translations in memory for thirty minutes. The new cache includes the source language and accepts only successful provider responses. A restart clears either version's cache. Previously fast repeated selections could have been cached, but no historical timing evidence establishes that this was the user's case.
- Between the installed `20140ba` build and release commit `5139a73`, the translation request, timeout and error mapping did not change. The popup diff only re-enabled retry for a failed copy/replace operation.

The historical probe copied the v2.3.10 translation/cache/source-detection methods verbatim into a disposable harness (original `ResultPopup.xaml.cs` lines 253–271 and 282–392). It did not launch the old WPF application. A fake transport first captured identical old/new URLs without contacting the provider. Separate synthetic live text then avoided warming either client cache:

| Historical comparison on this PC | Observed time/result |
| --- | --- |
| v2.3.10 helper, original eight-second timeout | `HttpClient.Timeout` at 8.012 seconds |
| Current production `LookupService.Shared`, original eight-second timeout | Timeout error at 8.009 seconds |
| v2.3.10 helper, diagnostic thirty-second timeout | Valid Arabic translation at 17.990 seconds |

This reproduces today's slow response with the old implementation, so reverting the recent translation UI/request refactor does not restore fast uncached translation for the tested English-to-Arabic case. The evidence points to a currently slow MyMemory request path. Without an earlier live latency receipt or provider-side diagnostics, the exact external change and its onset remain unknown. The user's report that translations were previously fast is consistent with this evidence.

## Timeout experiment

A temporary change gave translation one thirty-second deadline covering both response headers and body. The production lookup then returned a valid Arabic translation in 18.23 seconds. Another attempt returned a connection error after 10.53 seconds, so increasing the deadline does not guarantee availability or improve latency.

A nine-second streamed-response regression failed with the old body limit. The experiment passed six tests covering slow headers/body, deadline expiry and caller cancellation. It was removed from production source after the user asked to investigate the slowness: waiting longer is a mitigation, not a speed repair. Its source and before/after TRX receipts remain in the ignored artifact directory for comparison. It was not installed or published.

## Independent default change and provider decision

`MaxInlineContextActions` now defaults to 8. Explicit saved values remain intact; the user's saved setting was already 8. The README default is updated accordingly.

No provider change has been selected. Possible alternatives include another translation backend or opening Google Translate from the toolbar. The supported Google Cloud Translation API requires a Cloud project, credentials and enabled billing; opening the existing Google Translate website requires no API setup. These alternatives differ in popup behavior and configuration. The historical investigation establishes that the old request path is also slow today; it does not establish that MyMemory cannot recover its earlier responsiveness. The current inline translation feature has not been replaced with a browser handoff.

References: [MyMemory API request contract](https://mymemory.translated.net/doc/spec.php), [Google Cloud Translation setup](https://docs.cloud.google.com/translate/docs/setup), [Google Cloud Translation authentication](https://docs.cloud.google.com/translate/docs/authentication).

Local receipts: `artifacts/translation-20260908-0404/`, including `baseline-results.json`, provider response bodies/headers, `provider-comparison.json`, `transport-comparison.json`, production lookup results, `tests/before.trx` / `tests/after.trx`, and the extracted historical helper, probe source and `history/history-results.json`. With the timeout experiment removed and only the default changed, `tests/default-eight.trx` records all 523 tests passing.
