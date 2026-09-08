# Bundled license notices

These license texts come from the dependency packages used by this release (the runtime notices final blank line is normalized):

| File | Package source |
| --- | --- |
| `NET-Runtime-LICENSE.txt` | `Microsoft.NETCore.App.Runtime.win-x64` 10.0.11, `LICENSE.TXT` |
| `NET-Runtime-THIRD-PARTY-NOTICES.txt` | `Microsoft.NETCore.App.Runtime.win-x64` 10.0.11, `THIRD-PARTY-NOTICES.TXT` |
| `NET-WindowsDesktop-LICENSE.txt` | `Microsoft.WindowsDesktop.App.Runtime.win-x64` 10.0.11, `LICENSE` |
| `WebView2-SDK-LICENSE.txt` | `Microsoft.Web.WebView2` 1.0.4191.47, `LICENSE.txt` |
| `WebView2-SDK-NOTICE.txt` | `Microsoft.Web.WebView2` 1.0.4191.47, `NOTICE.txt` |

The pinned .NET SDK 10.0.303 supplies runtime 10.0.11. Refresh the corresponding notices when upgrading the bundled runtime. The build copies this folder and the project's root `LICENSE` into the release package.

The WebView2 SDK assemblies and loader use Microsoft's WebView2 SDK license. The separately installed Microsoft Edge WebView2 Runtime is not bundled in the ZIP.
