# Jot

A personal Windows notes app built with .NET 10. The UI uses the existing design system's IRANSansX fonts, 19 accent presets, neutral light/dark surfaces, and Lucide icons.

## Current build

Run **dist\Jot-tray\Jot.exe**, keeping its adjacent files together. Jot starts quietly in the system tray; click its icon for the notes index or use Ctrl+Alt+J for a note. No Jot window adds a taskbar button. Quit the previous Jot release first. The running version was not replaced or interrupted during development.

The .NET runtime is bundled. Windows 10 version 2004 or later and Microsoft Edge WebView2 Runtime are required. This build uses WebView2's WPF composition control with anti-aliased clipping, a subtle border, and a soft shadow. It no longer uses a jagged Win32 region for its corners.

## Home and work windows

The tray opens a compact notes index with search, New note, group filters, and note rows. There is no promotional heading. Use **Title and group** on a row to name or group the note; typing a new group name creates it. Search includes titles, content, and groups.

Settings replaces the index in the same window. **Back to notes** (or Escape) returns to the index, retaining its search and group filter. Creating/opening a note still gives it a separate small writing window. Each editor saves only its own content, preserving titles/groups changed in the index.

The app interface is English-only; notes still fully support Persian and English together, with automatic paragraph direction and a keyboard-aware empty caret. Notes have no placeholder, sidebar, title field, or saved/saving badge. Autosave continues quietly; a real failure remains visible. The header controls are left-aligned: **Home → Quit Jot → Hide → New note → Pin**. Pin remains opt-in.

If no title is set in the index, the first line supplies it. The writing surface stays neutral: the active note uses a colored sticky-note header, while an inactive note shows a thin colored top strip.

## Writing and images

- The writing toolbar stays visible by default. The type button or Ctrl+Shift+F toggles it, and the preference is remembered.
- Bold, italic, underline, strikeout, text colors, headings, lists, quotes, code, and per-note undo/redo.
- Paste formatted content including headings, tables, lists, links, images, and mixed Persian/English text. Active scripts and unsafe elements are stripped.
- Images appear as tiny inline previews (up to 72 × 48), without forcing a separate paragraph. Click one to open the original in a separate image viewer, with Fit, 1:1, and Copy.
- Original image resolution is retained. PNG, JPEG, WebP, and GIF are supported, up to 8 MB each.
- Ctrl+C retains selected rich HTML, embedded images, and plain-text fallback. “Copy complete note” copies the whole document. A selection containing one image supplies native PNG and Bitmap formats alongside its text, using the original resolution. “Copy text only” is available for apps that choose only one clipboard representation.
- The receiving application chooses which clipboard formats it supports; plain-text-only apps cannot retain images or formatting.
- HTML clipboard content is preferred over its bitmap representation, preserving text/image order.
- Pasted public HTTPS image references are downloaded without cookies and embedded locally. Unavailable images get a visible placeholder and notification instead of silently disappearing. Sources requiring login may not be retrievable.
- Export a standalone HTML note from the note menu.

## Appearance

Settings controls light/dark mode, the 19 accent colors, toolbar icon styling, font size, and toolbar visibility. Changes update open windows. There is no interface-language selector: the interface is English and the note editor remains bilingual.

The app icon is Lucide [notepad-text](https://lucide.dev/icons/notepad-text), used for the executable, native windows, tray, and app branding. It is monochrome. The live tray glyph follows Windows' system light/dark tray theme, independently of Jot's accent color; the fixed executable icon has a subtle contrast outline.

## Clipboard recovery and error logs

Native clipboard writes wait briefly for the embedded browser to release its copy operation, then retry temporary `CLIPBRD_E_CANT_OPEN / 0x800401D0` failures with bounded backoff. Newer Jot copy requests supersede older queued requests. A copy error is not a failed note save.

Future errors are logged locally in **%LOCALAPPDATA%\Jot\logs\jot-errors.jsonl**. Logs rotate through three numbered backups, at approximately 1 MB per file. Entries include UTC time, operation, exception type/HRESULT, source filenames and frame locations, retry/recovery events, and renderer error metadata. Note text, clipboard/image payloads, raw exception messages, URLs, and absolute source paths are not recorded. Logging failure cannot interrupt editing. The logs stay on this computer.

## Pasting into Codex

Text and images can be included in one prompt, but importing mixed rich HTML in a single paste depends on the receiving composer. Jot now supplies native image data as well as text when exactly one image is selected. End-to-end mixed paste into Codex was not verified because testing must not interrupt the live app. Reliable fallback: use Copy text only, paste it into the composer, then open each thumbnail and use Copy image, pasting it into the same prompt before sending. Image inputs are documented at https://learn.chatgpt.com/docs/image-inputs.

## Shortcuts

- Ctrl+Alt+J: show/hide a work note; create one if none exists.
- Ctrl+N: create a separate note window.
- Ctrl+K: open the home screen from a note, or focus search at home.
- Ctrl+B / I / U: bold / italic / underline.
- Ctrl+Shift+F: show/hide writing tools.
- Ctrl+S: save immediately or retry a failed save.
- Ctrl+Z / Ctrl+Y: undo / redo within the current note.
- Esc: dismiss the menu or hide the current window.
- Tray icon: home, new note, and Quit.

Hiding/closing a note flushes its draft. **Quit Jot** is available in the note header, index header, Settings, and tray menu. It waits for pending notes to save before exiting; a failed save cancels quitting.

## Data

Notes: %LOCALAPPDATA%\Jot\notes.json, with atomic replacement and notes.json.bak. Rich HTML and original embedded images are stored in versioned JSON. Existing version-2 notes remain compatible. The earlier prototype's localStorage notes are imported when its profile is available; legacy data is never deleted.

Core editing and all UI assets work locally. Network access is only used to retrieve an HTTPS image explicitly included in pasted content; no account or sync service is used.

## Build and test

    dotnet publish .\Jot.csproj -c Release -r win-x64 --self-contained true -o dist/Jot-tray
    node --check .\renderer.js
    Start-Process .\dist\Jot-tray\Jot.exe -WindowStyle Hidden -Wait -ArgumentList '--self-test','--test-output','D:\Platform\PersonalNotes\test-results\manual'

The self-test mode uses isolated data and offscreen non-activating windows, registers no global hotkey, and creates no tray icon. It never writes the user's clipboard. Clipboard formats are tested as in-memory DataObjects and renderer DataTransfer objects. Reports and UI renders stay inside the specified test directory.

See TESTING.md for coverage and practical limits.

## Assets

IRANSansX fonts are reused from the existing local project for this personal app; their original licensing applies. Lucide licensing is in assets/icons/LICENSE. Regenerate the notepad-text ICO using scripts/make-icon.ps1. Build outputs, test artifacts, runtime profiles, logs, and notes are excluded from Git.
