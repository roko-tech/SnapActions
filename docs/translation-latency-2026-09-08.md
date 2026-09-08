# Translation latency investigation — September 8, 2026

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

## Timeout experiment

A temporary change gave translation one thirty-second deadline covering both response headers and body. The production lookup then returned a valid Arabic translation in 18.23 seconds. Another attempt returned a connection error after 10.53 seconds, so increasing the deadline does not guarantee availability or improve latency.

A nine-second streamed-response regression failed with the old body limit. The experiment passed six tests covering slow headers/body, deadline expiry and caller cancellation. It was removed from production source after the user asked to investigate the slowness: waiting longer is a mitigation, not a speed repair. Its source and before/after TRX receipts remain in the ignored artifact directory for comparison. It was not installed or published.

## Independent default change and provider decision

`MaxInlineContextActions` now defaults to 8. Explicit saved values remain intact; the user's saved setting was already 8. The README default is updated accordingly.

A fast replacement requires choosing a different translation backend or opening Google Translate from the toolbar. The supported Google Cloud Translation API requires a Cloud project, credentials and enabled billing; opening the existing Google Translate website requires no API setup. These choices differ in popup behavior and configuration, so changing the provider is pending the user's choice. The current inline translation feature has not been replaced with a browser handoff.

References: [MyMemory API request contract](https://mymemory.translated.net/doc/spec.php), [Google Cloud Translation setup](https://docs.cloud.google.com/translate/docs/setup), [Google Cloud Translation authentication](https://docs.cloud.google.com/translate/docs/authentication).

Local receipts: `artifacts/translation-20260908-0404/`, including `baseline-results.json`, provider response bodies/headers, `provider-comparison.json`, `transport-comparison.json`, production lookup results and `tests/before.trx` / `tests/after.trx`.
