# Jot workspace verification

The current `dist/Jot-tray` build passed **71/71 offscreen checks** and a Release publish with no warnings. It retains **Ctrl+Alt+J**.

Evidence for this release is under **test-results/tray-01/**: results.json, UI captures, light/dark monochrome icon renders, and diagnostic test logs.

Current checks verify: no note placeholder or save badge; keyboard-aware Persian/English caret behavior; the exact five left-aligned header controls; an English-only UI even when legacy preferences request Persian; hidden tray startup without initializing a WebView; monochrome glyph contrast in both Windows themes; clipboard lock recovery/bounded retries/cancelled stale requests; clipboard failures not changing save state; safe logs that exclude private-content sentinels; bounded log rotation; and failure-tolerant logging.

New checks cover settings reusing the index's native window handle without increasing the window count; Back restoring the group filter; removal of promotional headings; a visible grouping action; neutral note surfaces with colored headers/top strips; Quit saving both pending note drafts; cancellation/retry after a failed save; and the pending spinner/label.

Existing checks cover editable titles/groups surviving concurrent editor saves; group filtering; English UI without rewriting Persian note content; active-keyboard empty caret direction; compact toolbar menus; tiny inline image insertion without paragraph breaks; original 600×200 clipboard image dimensions despite a 72×48 preview limit; native icon synchronization.

All checks run against the real .NET host and WebView2 renderer, with isolated notes and offscreen windows. The desktop remains untouched and the user's clipboard is never overwritten. Quit-button tests exercise the real save path with shutdown deferred; after a successful report, the shared production Quit method closes the isolated test windows and process.

Coverage:
- Home screen creates independent windows; reopening a note reuses its existing window.
- Concurrent editors preserve each other's data.
- Preferences update across windows, including all 19 accents and icon styling.
- The writing toolbar defaults to visible, can be hidden, and persists.
- Automatic Persian/English paragraph direction.
- Bold, italic, underline, colors, headings, lists, undo/redo.
- Rich paste keeps tables, spans, heading hierarchy, code whitespace, images, and text order.
- HTML-plus-bitmap clipboard content does not discard the text.
- Rich copy round-trips text, a table, and an embedded image.
- Native UTF-8 CF_HTML fragment offsets and standalone-image PNG/Bitmap/HTML formats.
- Thumbnail bounds with original image resolution preserved.
- Full image viewer, including 1:1 mode.
- Native resize hit zones, small-window layout, and anti-aliased composition surface.
- Reload persistence, cross-note isolation, failed-save retry, backups, corrupt-file preservation.
- Local font/icon loading and absence of renderer exceptions.

Visual checks used WebView2 captures and an offscreen WPF render of the actual composed window. The native surface render verifies the rounded border and shadow together with the actual UI. The hard-edged Win32 window-region code has been removed.

The harness does not manipulate the user's actual clipboard, manually drag a live desktop window, press the global hotkey, or interact with another app's paste behavior. Receiving applications determine which clipboard representations they support. Remote image downloading is guarded in code but is not tested against arbitrary external/private websites.

Run instructions are in README.md. The prior live app was left untouched; the new build is in dist/Jot-tray. Real mixed-content paste into Codex and live Windows tray visibility/theme changes were not exercised. Clipboard-lock tests inject the actual HRESULT through the production retry routine without locking or changing the user's clipboard. DataObjects, generated native icon pixels, and hidden-window lifecycle were tested offscreen.
