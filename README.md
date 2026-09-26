# Jot

A personal Windows notes app built with .NET 10. The UI uses the existing design system's IRANSansX fonts, 19 accent presets, neutral light/dark surfaces, and Lucide icons.

## Current build

Run **dist\Jot-stack\Jot.exe** (v1.1.0), keeping its adjacent files together. Jot starts quietly in the system tray; click its icon for the notes index or use Ctrl+Alt+J for a note. Open windows also appear in the taskbar. Hiding a window removes its taskbar entry while the tray remains available. Quit the previous Jot release first. The running version was not replaced or interrupted during development.

The .NET runtime is bundled. Windows 10 version 2004 or later and Microsoft Edge WebView2 Runtime are required. This build uses WebView2's WPF composition control with anti-aliased clipping, a subtle border, and a soft shadow. It no longer uses a jagged Win32 region for its corners.

## Home and work windows

The tray opens a compact notes index with search, New note, group filters, and note rows. There is no promotional heading. Use **Title and group** on a row to name or group the note; typing a new group name creates it. Search includes titles, content, and groups.

Settings replaces the index in the same window. **Back to notes** (or Escape) returns to the index, retaining its search and group filter. Creating/opening a note still gives it a separate small writing window. Each editor saves only its own content, preserving titles/groups changed in the index.

The app interface is English-only; notes still fully support Persian and English together, with automatic paragraph direction and a keyboard-aware empty caret. Notes have no placeholder, sidebar, title field, or saved/saving badge. Autosave continues quietly; a real failure remains visible.

The note header has **+ New note** on the left and **More → Pin → X (close note)** on the right. The app-logo/Home button, old grip, and shutdown controls are removed from notes. Open the index through **More → All notes** or Ctrl+K. X saves and hides only its note. More borrows the Windows Sticky Notes visual style—an edge-to-edge color palette and flat icon/text rows—with **All notes**, small **Light mode / Dark mode** text, **Font size**, **Export**, **Copy**, and **Delete**. The Appearance and plain-text-copy shortcuts are removed from notes; app settings remain in the index. All 19 colors are visible in one row, ordered from warm reds through yellows/greens/blues to purples/pinks, with neutral last. Swatches narrow with the window; none are hidden behind scrolling. Left/right arrows navigate them; Home/End reach the first/last color. In short windows only the options list scrolls, keeping every color visible and leaving room outside the panel to dismiss it. Escape also dismisses it. Pin remains opt-in and uses a filled glyph when active, without a solid background. The bottom formatting/image controls stay neutral-colored.

If no title is set in the index, the first line supplies it. The writing surface stays neutral: the active note uses a colored sticky-note header, while an inactive note shows a more visible 8px colored top strip. The colored header expands/collapses over 160ms without shifting the writing area. More visibly reveals its full height over 260ms and closes over 200ms, with an opaque surface so text does not bleed through. Interrupted motion continues from the current frame. Closing controls become non-interactive immediately; reduced-motion preferences disable these effects. An inactive pinned note keeps its filled Pin visible, and header hover targets sit below the 8px strip in both themes. Font size is displayed in pixels and stays within 13–24px. At either limit, the disabled button uses a normal cursor and a minimum/maximum tooltip, not a loading cursor. Only explicitly busy actions use the wait cursor. Native window corners are reduced to 6px, with matching 5px content clipping and compact 6px popovers.

## Writing and images

- Bold, italic, underline, strikethrough, bullet/numbered lists, text color, and clear formatting are directly available in one bottom row. No More tools submenu remains. The color T itself follows the selected text color, with no underline indicator; its compact swatch picker opens upward. The original upright T toggle or Ctrl+Shift+F shows/hides the row with a 220/180ms slide/fade, without moving the editor. Image insertion remains beside the toggle. Every footer glyph uses the same centered 16×16 SVG layout; the color T has no extra inline wrapper. The preference is remembered.
- The Text paragraph-style dropdown is removed. Existing/pasted headings, quotes, and code are retained; Strikethrough, Clear formatting, and per-note undo/redo remain available.
- Paste formatted content including headings, tables, lists, links, images, and mixed Persian/English text. Active scripts and unsafe elements are stripped.
- Images appear as tiny inline previews (up to 72 × 48), without forcing a separate paragraph. Click one to open the original in a separate image viewer, with Fit, 1:1, and Copy.
- Original image resolution is retained. PNG, JPEG, WebP, and GIF are supported, up to 8 MB each.
- Ctrl+C retains selected rich HTML, embedded images, and plain-text fallback. “Copy” copies the whole document. A selection containing one image supplies native PNG and Bitmap formats alongside its text, using the original resolution. There is no separate plain-text-copy action; normal Copy still supplies a plain-text fallback alongside rich content.
- The receiving application chooses which clipboard formats it supports; plain-text-only apps cannot retain images or formatting.
- HTML clipboard content is preferred over its bitmap representation, preserving text/image order.
- Pasted public HTTPS image references are downloaded without cookies and embedded locally. Unavailable images get a visible placeholder and notification instead of silently disappearing. Sources requiring login may not be retrievable.
- Copy the complete note, change text size, or export a standalone HTML note from the **note header → More** menu. The theme action states which mode it will switch to. Appearance settings are available from the index only. Strikethrough and Clear formatting are directly on the bottom toolbar.

## Appearance

The application stays neutral in both themes. Each note’s **More → color palette** independently selects from the 19 existing design-system accents; only that note’s header/top strip changes. Existing notes retain their previous header accent during migration. New note headers now start crimson. App surfaces remain neutral, and existing notes retain their chosen colors. Colors survive editing, reloads, and global theme changes.

Settings controls light/dark mode, icon weight, font size, and toolbar visibility. Changes update open windows. There is no interface-language selector: the interface is English and the note editor remains bilingual.

Jot uses the selected **B — folded stack** mark: yellow, teal, and blue papers for the executable, native windows/taskbar, and app branding. The generated PNG masters are packaged into 16, 20, 24, 32, 48, 64, 128, and 256px ICO frames. Native windows select the high-resolution color frame; the tray selects a matching system-size monochrome frame. Light/dark tray inks reuse one alpha master, so their silhouettes stay identical. Other interface icons remain Lucide. The source lock and actual-size review sheet are in docs/icon-design. Re-pin a shortcut to this build if Windows still shows an older icon; the running older version is not modified.

## Image-window stability

The recorded crash was a late activation/deactivation callback accessing a WebView2 control after disposal. The window now closes its message boundary and detaches callbacks before disposal; queued callbacks/replies are ignored, startup waits are cancelled when the window closes, and ordinary note closing remains save/hide. Image viewers load preferences only, retain original-resolution images, and show a local error for an undecodable image. This fixes the confirmed disposed-viewer path; it does not claim every possible future crash is impossible.

Microsoft documents that accessing [CoreWebView2 after disposal throws](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.wpf.webview2.corewebview2). Diagnostics now include process ID and version as well as the existing private-content-safe stack metadata.

## Clipboard recovery and error logs

Native clipboard writes wait briefly for the embedded browser to release its copy operation, then retry temporary `CLIPBRD_E_CANT_OPEN / 0x800401D0` failures with bounded backoff. Newer Jot copy requests supersede older queued requests. A copy error is not a failed note save.

Future errors are logged locally in **%LOCALAPPDATA%\Jot\logs\jot-errors.jsonl**. Logs rotate through three numbered backups, at approximately 1 MB per file. Entries include UTC time, operation, exception type/HRESULT, source filenames and frame locations, retry/recovery events, and renderer error metadata. Note text, clipboard/image payloads, raw exception messages, URLs, and absolute source paths are not recorded. Logging failure cannot interrupt editing. The logs stay on this computer.

## Pasting into Codex

Text and images can be included in one prompt, but importing mixed rich HTML in a single paste depends on the receiving composer. Jot now supplies native image data as well as text when exactly one image is selected. End-to-end mixed paste into Codex was not verified because testing must not interrupt the live app. For separate text/image pastes, select a text-only passage and copy it, then open each thumbnail and use Copy image. Receiving apps choose which representation they support. Image inputs are documented at https://learn.chatgpt.com/docs/image-inputs.

## Shortcuts

- Ctrl+Alt+J: show/hide a work note; create one if none exists.
- Ctrl+N: create a separate note window.
- Ctrl+K: open the home screen from a note, or focus search at home.
- Ctrl+B / I / U: bold / italic / underline.
- Ctrl+Shift+F (or the footer T button): show/hide writing tools.
- Ctrl+S: save immediately or retry a failed save.
- Ctrl+Z / Ctrl+Y: undo / redo within the current note.
- Esc: dismiss the menu or hide the current window.
- Tray icon: home, new note, and Quit.

Hiding/closing a note flushes its draft. **Quit Jot** is available only from the index, Settings, or tray menu; note windows cannot shut down the app. Quit waits for pending notes to save before exiting; a failed save cancels quitting.

## Deleting a note

Use **More → Delete** in its writing window. Confirmation defaults to Cancel. Deleting first saves the latest draft, writes a full recovery copy (including original images) to **%LOCALAPPDATA%\Jot\trash**, then removes the note from the index and closes only its window. Failed saving, recovery copying, or store replacement leaves the live note available. Other notes are unaffected, and stale saves cannot resurrect the deleted note. Recovery copies are retained locally; there is not yet an in-app restore screen.

## Data

Notes: %LOCALAPPDATA%\Jot\notes.json, with atomic replacement and notes.json.bak. Rich HTML and original embedded images are stored in versioned JSON. Existing version-2 notes remain compatible. The earlier prototype's localStorage notes are imported when its profile is available; legacy data is never deleted.

Core editing and all UI assets work locally. Network access is only used to retrieve an HTTPS image explicitly included in pasted content; no account or sync service is used.

## Build and test

    dotnet publish .\Jot.csproj -c Release -r win-x64 --self-contained true -o dist/Jot-stack
    node --check .\renderer.js
    Start-Process .\dist\Jot-stack\Jot.exe -WindowStyle Hidden -Wait -ArgumentList '--self-test','--test-output','D:\Platform\PersonalNotes\test-results\manual'

The self-test mode uses isolated data and offscreen non-activating windows, registers no global hotkey, and creates no tray icon. It never writes the user's clipboard. Clipboard formats are tested as in-memory DataObjects and renderer DataTransfer objects. Reports and UI renders stay inside the specified test directory.

See TESTING.md for coverage and practical limits.

## Assets

IRANSansX fonts are reused from the existing local project for this personal app; their original licensing applies. Lucide licensing is in assets/icons/LICENSE. Regenerate the selected raster-based Jot ICO using scripts/make-icon.ps1. It builds the app and invokes its headless --export-icon path; no app window, tray icon, user-data access, or existing-instance activation occurs during export. Build outputs, test artifacts, runtime profiles, logs, and notes are excluded from Git.
