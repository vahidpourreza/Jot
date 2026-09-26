# Jot workspace verification

The current `dist/Jot-stack` build passed **160/160 background checks** and a Release publish with no warnings. It retains **Ctrl+Alt+J**.

Evidence for this release is under **test-results/stack-final/**: results.json, composed active/inactive note windows, smaller corner masks, color palettes, deletion confirmation, pinned-note light/dark states, header More menus at normal/minimum size, monochrome icon size samples, and diagnostic test logs.

New lifetime regression checks cover six image-viewer open/fit/actual-size/close cycles, replayed and queued focus callbacks after disposal, four closes during startup, undecodable images, and normal note save/hide/reopen. The original notes and shared session remain intact. The close-button test harness recognizes destruction of its DevTools target instead of waiting forever for a reply from a closed viewer. Startup awaits are cancelled per window; the shared environment is not cancelled. Closing-window errors are not hidden by a blanket dispatcher exception handler.

Icon checks verify colored native app frames, highest-resolution window selection, eight ICO sizes, identical alpha masks for the two tray inks, transparency, small-size visibility, and monochrome contrast. docs/icon-design/final-icon-board.png shows actual packaged pixels. Live Windows taskbar pin/cache behavior is still not manipulated.

The alignment regression checks measure the actual SVG rectangles—not just the button boxes. Every footer glyph is 16×16, centered in its button, and shares the same vertical center in dark/light themes and at the minimum window width. The original T show/hide icon is restored and remains upright in both expanded/collapsed states. The toolbar captures, including aligned-toolbar-minimum.png, were visually inspected.

The note header is checked for New note on the left and More → Pin → Close on the right, with matching DOM/tab order and no app-logo/Home button. Appearance is explicitly absent from notes, while Settings remains accessible through the existing index window. Menu-label tests cover All notes, compact 12px Light mode / Dark mode text, Font size with px units, and the exact Export / Copy / Delete labels. The separate plain-copy action and Text-style dropdown are absent. All 19 colors remain available.

Native pointer tests hover/click the disabled minus at 13px and plus at 24px: neither shows a wait cursor, has a loading animation, nor sends a preference write. The enabled opposite button moves away from each limit. Captures include both limit states. Explicitly busy actions still retain their pending spinner/label behavior.

Pointer-driven host checks exercise Copy and Export without touching the real clipboard or opening a save dialog. Theme and text-size actions are checked through the renderer.

Menu checks require all 19 distinct colors to be visible and hit-testable at once in one ordered row, with no palette scrolling. They cover left/right-arrow navigation, Home/End, selection retention, focus, and dismissal. Native pointer checks select the last color without scrolling at minimum size, then restore the original color. At the minimum 320×250 window, only the option list scrolls; the palette remains visible, Delete stays reachable, and opening/cancelling its confirmation preserves the note. Light/dark and minimum-size captures were visually inspected.

Bottom-toolbar checks verify bold/italic/underline, strikethrough, bullet/numbered lists, color, clear formatting, image insertion, and the toggle fit on a single footer row at minimum size. Strikethrough/clear-formatting actions work inline, with no More tools popup. The T glyph takes the selected text color and has no separate underline marker; the compact color picker fits above the row. Rich paste retains headings/code even though the Text-style dropdown is removed.

New-note checks verify crimson header defaults with a neutral app theme; a separate CSS-before-preferences check verifies neutral startup colors in both themes. Existing selected note colors remain intact. Pinned/inactive checks verify the filled foreground pin stays visible without a solid background, and all hovered header controls remain below the 8px color strip in light/dark mode. These tests never actually enable Topmost on the desktop.

Motion checks inspect real CSS transitions and Web Animations: intermediate header heights, 8px collapsed / 34px expanded states, stationary editor bounds, full-height menu reveal, inert closing controls, rapid reversal from the current frame, and cleanup. Paused start/middle/end frames are captured in more-opening-*.png and visually inspected; the new reveal is not tested merely by checking whether an Animation object exists. Toolbar and color-picker opening/closing plus reduced-motion behavior are also checked. DevTools motion emulation affects only the isolated test WebView and is reset afterward. Ordinary pointer checks wait until the target animation settles; dedicated motion checks inspect intermediate states.

Existing checks cover neutral global chrome with independent blue/rose note headers; all 19 note colors; legacy global-accent migration; color retention across saves, theme changes, and reloads; no header-color leakage into writing text; filled/unfilled Pin states; 6px outer/5px content corners; and an 8px inactive color strip without shifting the editor.

Deletion tests cover Cancel as default focus; cancel preserving the note; recovery-path and main-store-write failures preserving live notes; disabled/pending confirmation; saving the latest draft and original image before deletion; closing only the selected window; updating the index; rejection of stale saves, reopening a deleted ID, and cross-note deletion. Synthetic archived notes remain in the isolated test output, never the user's note store.

Icon checks cover eight ICO sizes from 16 to 256px and visible monochrome detail at 16, 20, 24, 32, 48, and 64px against light/dark backgrounds. Rendered small icons and native composition captures were visually inspected. In the final run, opening the offscreen test home took about 3.72 seconds and the 66,000-character write/save test took 363ms; these are local measurements, not startup or latency guarantees.

Existing checks cover: no shutdown/grip controls in notes; X wired to save/hide; New note aligned to the left; More covering the top of the note and constrained to small windows; transparent active Pin styling; neutral footer icons in both themes even when legacy preferences request colored icons; and taskbar eligibility for production home/note/image windows. Taskbar property checks construct but never display those windows; no live taskbar or foreground interaction is performed.

Existing checks verify: no note placeholder or save badge; keyboard-aware Persian/English caret behavior; an English-only UI even when legacy preferences request Persian; hidden tray startup without initializing a WebView; monochrome glyph contrast in both Windows themes; clipboard lock recovery/bounded retries/cancelled stale requests; clipboard failures not changing save state; safe logs that exclude private-content sentinels; bounded log rotation; and failure-tolerant logging.

New checks cover settings reusing the index's native window handle without increasing the window count; Back restoring the group filter; removal of promotional headings; a visible grouping action; neutral note surfaces with colored headers/top strips; Quit saving both pending note drafts; cancellation/retry after a failed save; and the pending spinner/label.

Existing checks cover editable titles/groups surviving concurrent editor saves; group filtering; English UI without rewriting Persian note content; active-keyboard empty caret direction; compact toolbar menus; tiny inline image insertion without paragraph breaks; original 600×200 clipboard image dimensions despite a 72×48 preview limit; native icon synchronization.

All checks run against the real .NET host and WebView2 renderer, with isolated notes and offscreen windows. The desktop remains untouched and the user's clipboard is never overwritten. Quit-button tests exercise the real save path with shutdown deferred; after a successful report, the shared production Quit method closes the isolated test windows and process.

Coverage:
- Home screen creates independent windows; reopening a note reuses its existing window.
- Concurrent editors preserve each other's data.
- Global theme, icon weight, and writing preferences update across windows without changing each note's chosen accent.
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

Run instructions are in README.md. The prior live app was left untouched; the new build is in dist/Jot-stack. Real mixed-content paste into Codex and live Windows tray/taskbar visibility/theme changes were not exercised. Clipboard-lock tests inject the actual HRESULT through the production retry routine without locking or changing the user's clipboard. DataObjects, generated native icon pixels, and hidden-window lifecycle were tested offscreen.
