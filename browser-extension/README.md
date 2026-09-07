# SnapActions Browser Selection

This optional companion extension reads the browser's actual selected text for the Windows toolbar. Mixed Arabic/English, punctuation, and selections spanning multiple lines do not pass through the Windows accessibility geometry workaround.

## Install locally

The v2.4.0 desktop ZIP includes this optional companion (extension version 1.0.0, protocol version 1). It is loaded unpacked; it has not been published to a browser store.

1. Keep `SnapActions.exe` in its permanent location and run it.
2. In **SnapActions Settings → Browser**, choose Brave, Chrome or Edge and click **Register this copy**. The panel shows the registered executable and companion connection status. Alternatively, register from PowerShell:

   ```powershell
   .\install-host.ps1 -SnapActionsPath 'D:\path\to\SnapActions.exe' -Browser Brave
   ```

   `-Browser Chrome` and `-Browser Edge` register the corresponding browsers. Registration applies to your Windows account and does not require administrator access. Repeat registration if you move the executable.
3. In Brave, open `brave://extensions`, enable **Developer mode**, select **Load unpacked**, and choose the folder containing this README and `manifest.json`. Use the corresponding extensions page in Chrome or Edge.
4. Keep **Show toolbar automatically when I select text** enabled in SnapActions. Highlight text on a normal web page; the existing toolbar uses the browser selection automatically.

The extension ID is `ccgckebadlhbplacbcjbfohpinbdoehh`. Its toolbar button reconnects the bridge. Reload the extension after changing its JavaScript files. The public manifest key keeps its ID stable; it is not a secret.

## What it reads

The extension reads the current selection only when the desktop toolbar requests it. It uses `window.getSelection().toString()` for web content and the selected substring for supported input fields and textareas. It passes the text, text direction, editability and range identity locally through native messaging and a Windows pipe restricted to the same user. It makes no network requests, records no selected text, and has no clipboard permissions or clipboard code. SnapActions logs connection status and character counts only.

Toolbar hover previews use the source element's text direction so an English search label does not reorder an Arabic/English phrase. The desktop and companion now require protocol version 1; reload the extension when updating the executable. Missing or incompatible protocol versions fail closed, and Settings reports the unavailable page/companion state. Preview formatting never changes the captured or copied text.

The desktop accepts a bridge only from its own executable launched by the foreground browser process. Requests have individual IDs and protocol versions. Initial capture visits frames; later validation targets the captured document ID rather than reinjecting into every frame (Chromium 106 or newer). The extension checks the focused tab before and after reading; SnapActions checks the window and revalidates the document/frame/range before using the selection. Empty, changed, oversized or unavailable selections from a connected browser produce no capture instead of falling back to potentially incorrect UIA text.

Without a connected companion, the existing clipboard-free UIA path remains available for other apps and browsers. Browser-owned pages, built-in PDF viewers, canvas-based editors, inaccessible frames and password fields are not covered. Local HTML files require enabling the browser's **Allow access to file URLs** option. Selections longer than 32,768 UTF-16 code units are rejected, not truncated. Explicit Ctrl+C remains the compatibility option for unsupported surfaces.

Remove the extension to stop browser integration. The native host exits when the extension or desktop app disconnects, allowing the companion to reconnect after a desktop restart. Its registration is stored in `%LocalAppData%\SnapActions\Browser\com.snapactions.selection.json` and the current user's browser `NativeMessagingHosts\com.snapactions.selection` registry key. Brave registration also writes the Chrome-compatible registry entry used by its Windows native-host lookup.

## Verification

From the repository root:

```powershell
dotnet test SnapActions.Tests/SnapActions.Tests.csproj -c Release
node --test browser-extension/tests/selection.test.cjs
```

With the desktop bridge still running, test the actual published executable's UTF-8 stdio/pipe relay:

```powershell
python browser-extension/tests/native-host-smoke.py path/to/published/SnapActions.exe
```

The relay test creates a unique `SNAPACTIONS_DATA_DIR` and derives a separate pipe; it never intercepts the desktop bridge. The complete package gate runs this automatically.

Open **Settings → Browser → Open selection sample** for a built-in test page (`selection-sample.html`). Use `tests/selection-fixture.html` through a localhost HTTP server for live checks: drag in both directions, double-click the English word inside Arabic, select across wrapped lines, and select within an input and iframe. Confirm the toolbar's copied result matches the fixture's browser-selection receipt. Observe clipboard sequence numbers before and after highlighting; only an explicit Copy action may change them. Switch tabs during capture and before using a toolbar action to check stale-selection rejection. These live checks supplement the automated tests; unit tests do not prove the installed extension connection.
