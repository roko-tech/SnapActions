# Translation card — September 27, 2026

Before v2.5.0, Translate embedded Google's complete website in a 440 × 440 popup at 80% zoom. Google's header, **Sign in**, the Text/Images/Websites tabs, the source box and its microphone row took most of the space. The translation sat at the bottom of a white page, often below the fold. SnapActions v2.5.0 shows only the translation in a native card and keeps Google's page one click away.

## Sources re-checked

The [September 8 investigation](translation-fix-2026-09-08.md) found that only Google's full page worked on this PC. Before changing the display, the other routes were re-probed once each with synthetic English text:

| Route | Result on September 27 |
| --- | --- |
| Google `translate_a/single?client=gtx` | HTTP 429 "Sorry…" page after 1.47 s, unchanged. No workaround was attempted. |
| Google's basic page, `translate.google.com/m` | HTTP 302 to Google's CAPTCHA page. |
| MyMemory | HTTP 200 in 2.0 s, but it returned a fuzzy translation-memory match (0.86) that means "her book is on the table" for "The blue notebook is on the table." |
| Google's full Translate page in WebView2 | Works, as in v2.4.3. |

Three displays were offered: a trimmed Google page, a native card fed by that page, or a native card fed by an official API with the user's own key (DeepL or Azure). A browser test of the trimmed page left an empty band where the header had been, stayed light in dark mode, and still showed romanization. The user chose the native card fed by Google's page.

## How the card works

- **Visible to the page, not to the user.** Google only finishes translating in a page it considers visible. In a hidden browser tab, the page stayed on "Getting translation..." for 22 seconds. The card therefore lays out the WebView2 control at full size below its own window, where Windows clips it. The page reports `visibilityState: visible` at 650 × 550 CSS pixels.
- **Reading the result.** A script finds the first `span[jsname="W297wb"]` and its enclosing element with a `lang` attribute. It joins the segment spans' text with the spaces and line breaks between them. It excludes each segment's hidden alternatives text and the later dictionary entries shown for single words. Two identical reads 250 ms apart are required before the card shows the text.
- **Fallback.** If no stable result arrives within six seconds of the page loading, the script fails, or Google shows a non-Translate page such as consent, the popup shows Google's page, as in v2.4.x. The log records the reason without any text.
- **Selection handoff.** Actions implementing `ISelectionPresenter` receive the whole `SelectionSnapshot`. `ActionRunner` reports `SelectionTransferred`, so the toolbar and palette don't revoke the operation; the card revokes it when it closes. **Copy** and **Replace selection** use `ActionRunner.ApplyTextAsync`, like transform results. Replace keeps the selection's leading and trailing whitespace. The card uses `WS_EX_NOACTIVATE`, so the selected app keeps focus; Google's page view clears it so you can type.
- **⇄** swaps the saved source and target, then navigates the already-loaded page. It is hidden while the source is **Detect language**, because Google exposes the detected language only as display text.

## Measurements

Each harness run used the production popup, a fresh isolated WebView2 profile and synthetic text. Results were reviewed on screen and in the recorded events.

| Case | Result |
| --- | --- |
| Two English lines → Arabic | Both lines, with the line break. |
| Two sentences in one paragraph | Sentences separated by a space. An early build joined them; the reader was fixed. |
| Three paragraphs, Detect language | Blank lines kept; ⇄ hidden. |
| `lantern` → Arabic | `فانوس`. An early build appended dictionary alternatives; the reader was fixed. |
| `مكتبة`, Arabic → English | `library` |
| Arabic sentence with English → Arabic, then ⇄ | "The library is open today." 2.2 s after the swap; the swapped pair was saved. |
| Result element renamed by an injected script | Google's page shown after the deadline; reason logged. |

In three instrumented runs, the page finished loading 2.7–4.0 s after the card appeared and the result followed 0.5–1.1 s later: 3.4–4.3 s in total. Other runs took 3.9–5.2 s, with one 9.0 s outlier. The v2.4.3 popup was recorded as visible by 2.8–3.8 s. These are samples, not guarantees; loading Google's page dominates. The card never became the foreground window.

The installed candidate was then checked in Notepad on Windows 10 at 250% scaling. Toolbar Translate showed the card and **Replace selection** pasted the Arabic translation. Translate from the keyboard palette handed over the selection, and ⇄ produced English. After a swap, Notepad was still the foreground window and Replace worked. **Open in Google Translate** showed the page in place, and Esc closed it. Saved settings were unchanged afterward. Copy was not clicked live, to leave the clipboard untouched.

## Limits

- The reader depends on undocumented Google markup. If Google changes it, every translation shows Google's page after the deadline until SnapActions is updated; the log line identifies this state.
- The card is as fast as Google's page. It does not cache translations or pre-load the browser.
- Google's page view keeps the v2.4.x frame, including a thin line along its top edge on Windows 10.
