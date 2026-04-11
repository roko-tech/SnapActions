# SnapActions

A smart text selection toolbar for Windows. Select text anywhere and get instant context-aware actions.

![.NET 9](https://img.shields.io/badge/.NET-9.0-purple) ![WPF](https://img.shields.io/badge/WPF-Windows-blue) ![License](https://img.shields.io/badge/License-MIT-green)

## Features

- **Smart Text Detection** - Automatically detects URLs, emails, file paths, JSON, XML, colors, IPs, UUIDs, dates, math expressions, and code snippets
- **Context-Aware Actions** - Shows relevant actions based on detected text type (Open URL, Format JSON, Calculate math, etc.)
- **Text Transforms** - UPPERCASE, lowercase, Title Case, camelCase, snake_case, kebab-case, PascalCase, and more
- **Encode/Decode** - URL, Base64, HTML encode and decode
- **Search Engines** - Google, Bing, DuckDuckGo, YouTube, Twitter/X, Reddit, GitHub, StackOverflow, Wikipedia, and more
- **Language Filtering** - Filter search results by language (supports 13+ languages)
- **Custom Search Engines** - Add your own search engines with URL templates
- **Paste Mode** - Long-press in text fields to paste with optional text transforms
- **Pin to Toolbar** - Pin frequently used actions to the main toolbar for quick access
- **Configurable** - Toggle action categories, reorder search engines, set auto-dismiss timeout, show delay, and more
- **Non-Intrusive** - Doesn't steal focus or modify your clipboard

## How It Works

1. **Select text** anywhere (drag, double-click, or triple-click)
2. A floating toolbar appears above the selection
3. Click an action or expand a category for more options
4. **Long-press** in a text field to access paste with transforms

## Installation

### Requirements
- Windows 10 version 19041 or higher
- .NET 9.0 Runtime (or use the self-contained build)

### Build from Source

```bash
git clone https://github.com/roko-tech/SnapActions.git
cd SnapActions/SnapActions
dotnet build -c Release
```

### Publish Self-Contained

```bash
cd SnapActions/SnapActions
build.bat
```

This produces a single `SnapActions.exe` in `bin\publish\` (~163MB, no .NET runtime needed).

## Usage

- **System Tray** - SnapActions runs in the system tray. Right-click for options, double-click for Settings
- **Enable/Disable** - Toggle from tray menu or Settings
- **Auto-Start** - Enable "Start with Windows" in Settings
- **Exclude Apps** - Add process names to the exclusion list in Settings

### Toolbar Actions

| Icon | Action | Description |
|------|--------|-------------|
| Copy | Copy selected text to clipboard |
| Aa | Text transforms (case, trim, sort, wrap) |
| </> | Encode/decode (URL, Base64, HTML) |
| Search | Search with configured engines |
| V | Paste (long-press mode) |

### Edit Mode

Click the gear icon in any expanded sub-menu to enter edit mode:
- **Left-click** an action to show/hide it
- **Right-click** an action to pin/unpin it to the main toolbar
- **Arrow buttons** to reorder actions

### Custom Search Engines

In Settings, scroll to "Add Custom Search Engine":
- **Name**: Display name (e.g., "My Wiki")
- **URL**: Search URL template using `{0}` for query and `{1}` for language code
- Example: `https://wiki.example.com/search?q={0}&lang={1}`

## Settings

Settings are stored in `%AppData%\SnapActions\settings.json`.

| Setting | Description | Default |
|---------|-------------|---------|
| Show delay | Delay before toolbar appears | Instant |
| Auto-dismiss | Time before toolbar auto-hides | 8 seconds |
| Replace on transform | Auto-paste transformed text | On |
| Search language | Filter search results by language | No filter |

## Architecture

```
SnapActions/
  Core/           Mouse hook, text capture, selection tracking
  Detection/      14 text type detectors + classifier
  Actions/        Context, transform, encode, search actions
  UI/             WPF toolbar, settings window, system tray
  Config/         Settings management
  Helpers/        Math evaluator, screen utilities
```

## How Text Capture Works

SnapActions uses a two-step approach to capture selected text without interfering with other applications:

1. **WM_COPY message** - Sent directly to the focused window (no keyboard simulation)
2. **Ctrl+Insert fallback** - Used for browsers that don't respond to WM_COPY (avoids Ctrl+C which can trigger browser extensions like h5player)

The original clipboard content is saved and restored after capture.

## License

MIT License - see [LICENSE](LICENSE) file.
