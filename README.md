# Jot

A personal Windows notes app built with .NET 10. The UI uses the existing design system's IRANSansX fonts, 19 accent presets, neutral light/dark surfaces, and Lucide icons.

## Current build

**Development build: `dist\Jot-dev-theme\Jot.exe` (v1.6.0).** Keep its adjacent files together and quit an older running Jot before opening it. This iteration is development-only: do not build another installer unless the user explicitly requests one. The existing installer below remains v1.5.0 and does not contain these changes.

This development build upgrades SQLite settings storage to schema 2 and keeps `jot.db.before-v2.bak` before upgrading an existing schema-1 database. After using it, continue with v1.6 or newer; v1.5 will refuse the newer database schema instead of overwriting it.

### Last packaged release

For end users, run **dist\installer\Jot-Setup-1.5.0-win-x64.exe** (approximately 334 MiB). It bundles .NET/WPF, SQLite and a private WebView2 runtime, installs per-user without elevation, and requires no separate prerequisite installation or setup-time download. Quit an existing Jot before installing. Uninstall preserves notes and backups. The installer is currently unsigned, so Windows may show an unknown-publisher/SmartScreen warning.

For repeatable releases, run **scripts/release.ps1**; use `-Verify -TestInstaller` for full offscreen app and isolated installation checks. Runtime downloads are cached locally. A cached `-NoRestore` build took about 112 seconds in validation. See [releasing](docs/releasing.md) for prerequisites on the developer machine, cache behavior, runtime updates and verification limits.

Normal launches open the notes index with a taskbar entry, while the system tray stays available. Launching Jot again reveals the existing index. Use Ctrl+Alt+J for a note. Hiding a window removes its taskbar entry without quitting. Explicit `--tray` startup remains available if you deliberately want a background-only launch. The running version was not replaced or interrupted during development. The first launch migrates existing JSON notes to SQLite; after switching, do not return to the older JSON-based app.

The raw developer publish bundles .NET but uses the machine's WebView2 installation; the installer above bundles both. The technical OS minimum is Windows 10 build 19041 or later on an x64-compatible system. This app uses WebView2's WPF composition control with anti-aliased clipping, a subtle border, and a soft shadow. It no longer uses a jagged Win32 region for its corners.

## Home and work windows

The tray opens a compact notes index with search, New note, group filters, and note rows. There is no promotional heading. Use **Title and group** on a row to name or group the note; typing a new group name creates it. Search includes titles, content, and groups.

Settings replaces the index in the same window. **Back to notes** (or Escape) returns to the index, retaining its search and group filter. Creating/opening a note still gives it a separate small writing window. Each editor saves only its own content, preserving titles/groups changed in the index.

The app interface is English-only; notes still fully support Persian and English together, with automatic paragraph direction and a keyboard-aware empty caret. Notes have no placeholder, sidebar, title field, or saved/saving badge. Autosave continues quietly; a real failure remains visible.

Mixed-language handling now recognizes Persian sentences that start with English technical identifiers, and isolates English/technical expressions so their internal order remains readable. Read-only titles/previews use the same rules. **More → Direction → Auto / LTR / RTL** provides a per-paragraph override for ambiguous cases, with Undo and save/reload support. This does not change the app language, reorder stored characters, or add hidden direction characters to plain-text Copy. See [mixed-language writing](docs/mixed-language-writing.md) for coverage and limits.

The note header has **+ New note** on the left and **More → Pin → X (close note)** on the right. The app-logo/Home button, old grip, and shutdown controls are removed from notes. Open the index through **More → All notes** or Ctrl+K. X saves and hides only its note. More borrows the Windows Sticky Notes visual style—an edge-to-edge color palette and flat icon/text rows—with **All notes**, small **Light mode / Dark mode** text, **Font size**, **Export**, **Copy**, and **Delete**. The Appearance and plain-text-copy shortcuts are removed from notes; app settings remain in the index. All 19 colors are visible in one row, ordered from warm reds through yellows/greens/blues to purples/pinks, with neutral last. Swatches narrow with the window; none are hidden behind scrolling. Left/right arrows navigate them; Home/End reach the first/last color. In short windows only the options list scrolls, keeping every color visible and leaving room outside the panel to dismiss it. Escape also dismisses it. Pin remains opt-in and uses a filled glyph when active, without a solid background. The bottom formatting/image controls stay neutral-colored.

If no title is set in the index, the first line supplies it. The writing surface stays neutral: the active note uses a colored sticky-note header, while an inactive note shows a more visible 8px colored top strip. The colored header expands/collapses over 160ms without shifting the writing area. More visibly reveals its full height over 260ms and closes over 200ms, with an opaque surface so text does not bleed through. Interrupted motion continues from the current frame. Closing controls become non-interactive immediately; reduced-motion preferences disable these effects. Pin hides with the other inactive header icons, even when pinned; the whole group returns on hover/focus. Header hover targets sit below the 8px strip in both themes. Font size is displayed in pixels and stays within 13–24px. At either limit, the disabled button uses a normal cursor and a minimum/maximum tooltip, not a loading cursor. Only explicitly busy actions use the wait cursor. Native window corners are reduced to 6px, with matching 5px content clipping and compact 6px popovers.

## Writing and images

- Right-click inside a note for a Jot-styled editing menu: Undo/Redo, Cut, Copy, plain-text Paste, and Select all. Right-click an image for Open image, Copy image, and undoable Remove image. Shift+F10 opens it from the keyboard; arrows/Home/End navigate, and Escape dismisses it. Clipboard reads happen only on Paste. Cut removes content only after successful copying; a failed, cancelled, or stale operation leaves it intact.
- Generic browser context menus are disabled throughout Jot. Headers, buttons and blank app chrome do not open a menu. Search/title/group fields have a compact Cut/Copy/Paste/Select all menu that preserves selection and works inside dialogs. The image viewer has a content-only menu for Copy, zoom, Fit/100%, and fullscreen.
- The More panel now uses a distinct neutral surface in both themes, keeping its existing options, colors and transitions.
- Bold, italic, underline, strikethrough, bullet/numbered lists, and text color are directly available in one bottom row. Clear formatting and the More tools submenu are removed. Text color uses a filled circle showing the selected color; its compact swatch picker opens upward. The separate upright T toggle or Ctrl+Shift+F still shows/hides the row with a 220/180ms slide/fade, without moving the editor. Image insertion remains beside the toggle. Every footer glyph uses the same centered 16×16 SVG layout. The preference is remembered.
- The Text paragraph-style dropdown is removed. Existing saved headings, quotes, and code are retained; new pasted text is unformatted. Strikethrough and per-note undo/redo remain available. Bold/italic/underline/strikethrough can each be toggled off directly.
- **Font size, line height, writing-toolbar visibility and Pin are remembered independently by each note.** These controls in More and the footer change only that note. **Light/Dark mode is app-wide**, whether changed from a note or Settings; it updates existing notes, image windows, the index, Settings and the tray menu. Line height uses 1.2×, 1.5×, 1.75×, 1.95×, 2.2×, and 2.5× presets. Existing notes inherit their previous writing settings once during migration; later changes to writing defaults do not change them. View settings do not rewrite note HTML or paragraph direction.
- **Normal paste is plain text by default.** Ctrl+V prefers the clipboard's text/plain version and does not import source colors, fonts, highlighting, code-block backgrounds, headings, or table styling. Line breaks, blank lines, indentation, literal code characters, and Persian/English text are preserved. HTML-only clipboard text is flattened into ordinary paragraphs (table cells use tab separators); active content is discarded. An empty clipboard does not change the note. Existing notes are not restyled or rewritten.
- Images appear as tiny inline previews (up to 72 × 48), without forcing a separate paragraph. Click one to open the original in a separate viewer: zoom in/out with a percentage, Fit, 100%, original-image Copy, and fullscreen. Scroll to zoom, drag to pan, or double-click to toggle Fit/100%. Manual zoom runs from 10% to 800%; Fit can go smaller for large images.
- F11 or the fullscreen button fills the image window's current monitor, without enabling always-on-top. Escape exits fullscreen and restores the previous bounds; another Escape closes the image viewer. +/− zoom, 0 fits, and 1 selects 100%. Viewer zoom never changes the stored or copied original.
- Image Copy feedback is an overlay, not a replacement for the dimensions footer. Its busy/success/error states do not resize the image viewport, alter zoom, or clamp either scroll position. A delayed scroll notification from Fit/focus cannot dismiss a newly opened image menu unless the viewport actually moved.
- Original image resolution is retained. PNG, JPEG, WebP, and GIF are supported, up to 8 MB each.
- Ctrl+C retains selected rich HTML, embedded images, and plain-text fallback. “Copy” copies the whole document. A selection containing one image supplies native PNG and Bitmap formats alongside its text, using the original resolution. There is no separate plain-text-copy action; normal Copy still supplies a plain-text fallback alongside rich content.
- The receiving application chooses which clipboard formats it supports; plain-text-only apps cannot retain images or formatting.
- For mixed text/images, HTML is used only to retain image placement: the surrounding text is unformatted and original images remain inline thumbnails. Image-only clipboard content still pastes as an image. This does not change outgoing Copy, which retains the note's current rich content and original images.
- Pasted public HTTPS image references are downloaded without cookies and embedded locally. Unavailable images get a visible placeholder and notification instead of silently disappearing. Sources requiring login may not be retrievable.
- Copy the complete note, change text size or line height, or export a standalone HTML note from the **note header → More** menu. The theme action states which mode it will switch to. Appearance settings are available from the index or tray Settings action. Strikethrough remains directly on the bottom toolbar.

## Appearance

The application stays neutral in both themes. Each note’s **More → color palette** independently selects from the 19 existing design-system accents; only that note’s header/top strip changes. Existing notes retain their previous header accent during migration. New note headers now start crimson. App surfaces remain neutral, and existing notes retain their chosen colors. Colors survive editing, reloads, and global theme changes.

Settings controls app appearance and **new note defaults**. Theme and icon weight are application-wide. Font size, line height and toolbar visibility supply initial defaults for new notes; existing notes retain their own writing choices. Old per-note theme values from early development builds are ignored. There is no interface-language selector: the interface is English and the note editor remains bilingual.

## Custom tray menu

Right-click Jot's tray icon for a compact, custom-templated light/dark menu with the app logo, All notes, New note, Settings, Tray icon visibility, and Quit Jot. It is a lightweight WPF window, not a native Windows context menu or another WebView. It has no taskbar entry, supports Escape and arrow-key navigation, dismisses on deactivation, and clamps its position to the current monitor's work area. Settings reuses the index window. New note and Quit show a pending indicator; a failed save keeps Jot running and shows a retryable error.

Jot keeps its tray icon registered while running, but cannot promise placement outside Windows' overflow area. [Microsoft documents that only the user can promote an icon out of overflow](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area). Drag Jot's icon beside the clock, or use **Tray icon visibility…** / **Settings → Tray icon → Windows settings…**. Those controls open [Windows Taskbar settings](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings#personalization) only when clicked; the app does not change Explorer registry settings or other apps' icon preferences.

Jot uses the selected **B — folded stack** mark: yellow, teal, and blue papers for the executable, native windows/taskbar, and app branding. The generated PNG masters are packaged into 16, 20, 24, 32, 48, 64, 128, and 256px ICO frames. Native windows select the high-resolution color frame; the tray selects a matching system-size monochrome frame. Light/dark tray inks reuse one alpha master, so their silhouettes stay identical. Other interface icons remain Lucide. The source lock and actual-size review sheet are in docs/icon-design. Re-pin a shortcut to this build if Windows still shows an older icon; the running older version is not modified.

## Responsiveness and initial paint

Typing updates direction only in changed blocks, coalesces undo snapshots after a short typing pause, and defers layout-dependent plain-text capture until saving. Undo, formatting, and Save capture pending typing immediately, so those commands never use a stale snapshot. Selection-toolbar updates are coalesced into one animation frame. Unchanged notes do not rewrite storage. Autosaving content no longer reapplies preferences or recolors all open editors. The index updates existing rows rather than rebuilding every control, batches search/refresh work, and defers autosave refreshes while hidden.

The app remains C#/.NET 10. See [the measured responsiveness assessment](docs/responsiveness-assessment.md) for the shipped improvements and a direct-rendering experiment. Window movement/resize parity with Telegram has **not** been established; the production capture-based renderer is unchanged, and the experimental host is test-only.

SQLite reads and transactions share a per-store background-worker gate, so SQL, serialization and durable disk flushes do not run on the window dispatcher. Autosave updates only the edited note. The index queries summaries without reading rich HTML or original images; each editor queries its own note, and preferences are queried separately. Failed transactions roll back. There is no stale in-memory data cache hiding external database damage. Original image handling and deleted-note JSON recovery remain unchanged.

New note windows remain transparent and non-activating until the editor has applied its actual theme and crimson/chosen header color. Initial header transitions are disabled; normal transitions resume afterward. This prevents the neutral-white-to-crimson flash without changing the note design. Initialization failures reveal the error state instead of leaving an invisible window.

App/taskbar artwork has a tighter crop (about 1.5% padding, rounded to pixels, versus 6% previously). Tray padding is unchanged. Color and light/dark tray ICOs are packaged at build time and loaded directly at runtime; opening Jot no longer rerasterizes the source artwork. Keep all three ICO assets with the build.

See TESTING.md and the isolated `performance.json` reports for measured results and limitations. The native scheduling changes follow [Microsoft's WebView2 performance guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance); measurements here are local synthetic workloads, not guarantees for every device or document.

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

Notes now use **%LOCALAPPDATA%\Jot\jot.db**, a local SQLite database with separate notes, groups, settings, and application-state tables. It runs inside Jot; no database server or service is required. The interface remains compatible with the existing version-2 note model. Rich HTML and original embedded images remain together in each note's HTML; this release does not redesign image storage.

On first access, an existing **notes.json** is validated and imported into a temporary database, checked, and only then published as **jot.db**. IDs, order, titles, groups, colors, timestamps, preferences, bilingual content and embedded images are retained. The original **notes.json** and **notes.json.bak** are not changed or removed. Existing databases are authoritative: corrupt or unsupported databases are not silently replaced with older JSON. The earlier prototype's localStorage import remains available when its profile is present.

**jot.db.bak** is a verified SQLite snapshot: migration creates one, an existing store is backed up before its first mutation in a new session, and Quit refreshes it after all notes are saved. Backups use SQLite's backup API, not a live file copy. WAL mode may also create **jot.db-wal** and **jot.db-shm**; never delete or separate them from the live database. Quit Jot before copying its data folder manually. See [SQLite storage and recovery](docs/sqlite-storage.md).

Core editing and all UI assets work locally. Network access is only used to retrieve an HTTPS image explicitly included in pasted content; no account or sync service is used.

## Build and test

Keep the source, test code, assets, scripts, documentation, Git history, and current `dist/Jot-dev-theme` development build. `bin` and `obj` are generated by .NET and can be rebuilt; the unused Electron dependencies are not needed. Current verification reports are in `test-results/global-theme-01`; earlier build/reports are retained for reference. Publish to a different directory if the target executable is running.

Two old prototype browser profiles found inside build output were preserved as verified ZIPs in the ignored `recovery` directory before cleanup. Keep these recovery copies until their legacy data is no longer needed. Current notes remain under `%LOCALAPPDATA%\Jot`, outside the repository.

    dotnet publish .\Jot.csproj -c Release -r win-x64 --self-contained true -p:JotOfflinePackage=false -o dist/Jot-dev-theme
    node --check .\renderer.js
    Start-Process .\dist\Jot-dev-theme\Jot.exe -WindowStyle Hidden -Wait -ArgumentList '--self-test','--test-output','D:\Platform\Jot\test-results\manual'

The self-test mode uses isolated data and offscreen non-activating windows, registers no global hotkey, and creates no tray icon. It never writes the user's clipboard. Clipboard formats are tested as in-memory DataObjects and renderer DataTransfer objects. Reports and UI renders stay inside the specified test directory. Use a fresh output directory for each run: failure-injection fixtures deliberately leave broken paths and files behind.

See TESTING.md for coverage and practical limits.

## Assets

IRANSansX fonts are reused from the existing local project for this personal app; their original licensing applies. Lucide licensing is in assets/icons/LICENSE. Regenerate the selected raster-based Jot ICO using scripts/make-icon.ps1. It builds the app and invokes its headless --export-icon path; no app window, tray icon, user-data access, or existing-instance activation occurs during export. Build outputs, test artifacts, runtime profiles, logs, and notes are excluded from Git.
