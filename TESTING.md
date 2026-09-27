# Jot workspace verification

## Current development build (v1.6.0)

`dist/Jot-dev-theme/Jot.exe` passed **436/436 offscreen checks** in `test-results/global-theme-01/results.json`. No installer was built or updated for this iteration. Release publish, JavaScript syntax checks and `git diff --check` passed. The running `dist/Jot-dev` app was left untouched.

Added checks cover independent per-note font/line-height/toolbar/pin persistence, rapid setting changes, cross-note/global-writing-default rejection from note windows, unchanged siblings and note HTML, new-note defaults, schema-1 upgrade seeding, retained pre-upgrade backups, failed-upgrade recovery, validation of note-setting values, stale content saves preserving view settings, and cascading cleanup of deleted-note preferences. Settings UI now describes app appearance and new-note defaults explicitly.

Theme is global, not per-note. Both Light/Dark choices from a note update the index, sibling notes, open images and tray menu. Settings and the real More button synchronize each other; changes persist across note reloads. Rapid changes keep the last choice. Invalid theme requests cannot write other settings. Old per-note themes are ignored on database/renderer reads and import, and removed on settings saves. Content, header colors, writing preferences and Pin stay intact throughout.

Context checks verify that generic browser menus are disabled on index/note/image/settings surfaces, headers stay quiet, editable fields offer a styled menu without reading the clipboard on open, Persian selection Copy and plain Paste work, a failed Cut preserves text, modal menus remain in the top layer, and Escape dismisses the menu without closing its dialog. The image content menu offers working viewing actions. Input and image-menu captures were visually inspected.

Image Copy is sampled frame-by-frame during an injected clipboard delay: fit mode, a zoomed/panned view at its scroll limits, and a failed Copy retain the same zoom, viewport size, canvas size and horizontal/vertical scroll positions. Pending feedback remains visible but out of flow. A delayed pre-menu scroll notification from Fit/focus was reproduced and fixed without hiding real scrolling. The delete-recovery test fixture now preserves earlier synthetic trash instead of assuming its directory is absent.

The user’s live app, clipboard and note data were not used by these tests. Schema upgrades occur only when the user opens the new development build against their own data.

## Offline installer verification

The offline v1.5.0 installer has been produced at `dist/installer/Jot-Setup-1.5.0-win-x64.exe`. The acquired Microsoft Fixed Version runtime is 154.0.4258.37, with verified signature/version and pinned CAB SHA-256. The packaged app passed **384/384** full checks, **8/8** private-runtime smoke checks, and **9/9** isolated installer checks (fresh installation, installed-app smoke, repair/reinstall, no production uninstall registration, exclusion of test assets, removal of app/runtime, and preservation of isolated notes). Evidence: `artifacts/verification/1.5.0-20260927-084729-44dd50`.

The smoke test uses missing inherited .NET/WebView2 environment paths, then checks the actual loaded local .NET/WPF assemblies and bundled browser process, plus bilingual SQLite persistence. Tests remain hidden, offscreen, non-activating, non-topmost and use isolated data. Validation setup uses a distinct AppId, no icons, and no production uninstall registration. The user’s app/data and system WebView2 installation were not changed.

A cached repeat build with collected third-party notices passed the 8 smoke checks again under `artifacts/verification/1.5.0-20260927-085823-897b98`; it took **111.6 seconds** and produced a **333.93 MiB** installer. The full validation run took **412.5 seconds**. The latest installer hash is recorded in `dist/installer/release-info.json` and its `.sha256` companion. These are host-isolated tests, not a clean Windows VM test. The installer is unsigned; no SmartScreen bypass or signing certificate is implied. See [release instructions](docs/releasing.md).

## SQLite release (v1.5.0)

The current `dist/Jot-sqlite` build passed **384/384 offscreen checks**. Evidence: `test-results/sqlite-final/results.json`, SQLite 3.53.4 runtime assertion, performance reports, and offscreen captures. It uses Microsoft.Data.Sqlite 10.0.12 with the explicitly pinned native bundle. No production data was migrated by the test run, and the previous app was not opened, restarted or interrupted.

Release publish and `git diff --check` passed. Dependency versions are recorded in `packages.lock.json`; the final locked restore resolved the same provider/native versions exercised by the suite. The NuGet vulnerability check, including transitive dependencies, reported no known vulnerable packages from the public NuGet source at validation time.

New checks verify one-time JSON migration; byte-identical original JSON and its backup; retained note IDs/order/active note, groups/titles/colors/timestamps, extra metadata, preferences, Persian/English content and embedded images; summary-only index reads; parameterized group names; targeted note updates; metadata ownership; cross-window/store-instance writes; atomic rollback after an earlier statement succeeds; stale-save rejection; bounded write-lock contention; independent verified SQLite backups and retention after failed backup; unsupported/corrupt databases failing closed; and retryable migration failures. Child-process tests terminate only their own invisible synthetic-store process before or after commit, reopen the resulting WAL database, and check both recovered content and integrity.

The old JSON `.tmp` obstruction tests were replaced with real SQLite statement failures, preserving their data-retention assertions. Normal store work is on background workers, not the UI thread. In the final fixture, `performance.json` reports the measured 5.2 MiB database save/read workload; it now reads only the edited note, so it is not a like-for-like comparison with the previous full-collection JSON reload. No guarantee of general UI responsiveness or physical disk-failure recovery is implied. Image UI/storage behavior was intentionally not redesigned.

See [SQLite storage and recovery](docs/sqlite-storage.md) for schema, migration, backup semantics, known limits and dependency references. Older release results below are historical.

## Prior release history

Generated test artifacts were cleaned after the project directory was renamed to `D:\Platform\Jot`. The latest `test-results/context-final` reports and screenshots remain; its temporary profiles/data and older generated runs were moved to the Windows Recycle Bin. Historical artifact paths below describe prior runs and may no longer exist locally. Test source code is unchanged.

Obsolete releases and rebuildable `bin`, `obj`, and unused Electron dependencies were also cleaned, retaining `dist/Jot-context`. Legacy browser profiles discovered in `bin` were first archived under the ignored `recovery` directory and every archived file was hash-verified. Cleanup preserved source/tests, the current release, latest reports, and current notes byte-for-byte; it did not rerun the app or test suite.

The previous `dist/Jot-context` v1.4.0 build passed **341/341 offscreen checks** and a Release publish with no warnings. Evidence: **test-results/context-final/results.json** and its sibling PNG captures. JavaScript syntax checks and `git diff --check` passed. The running user app, user notes, real clipboard, foreground, tray, and taskbar settings were not changed.

New checks cover a real WebView right-click opening the custom note menu; retained text/backward selections; keyboard opening/navigation/dismissal; compact-window scrolling; separate menu/note surfaces in both themes; Undo/Redo, Copy, Cut and plain-text Paste; original-resolution image Copy, Open and undoable Remove; mixed text/image paste order and thumbnail size; empty paste; UTF-8 CF_HTML decoding and malformed-HTML fallback. Failed/superseded copying, edits, IME composition, or Escape during a pending Cut never remove the selection. Slow image paste cannot overwrite new typing. Native clipboard access is replaced with in-memory fixtures during these checks.

Image checks cover Fit, 100%, zoom buttons and percentage, 10–800% bounds, real wheel/pan input, minimum-window layout, unchanged original pixels when copying at 800%, fullscreen chrome removal, F11/Escape, restored bounds, repeated transitions, and closing while fullscreen without late-callback failures. In self-test mode fullscreen uses a simulated 1200×800 rectangle at -32000,-32000, with zero opacity, no activation, no taskbar entry, and no Topmost. Live monitor coverage, taskbar occlusion and mixed-DPI monitor transitions are **not** exercised, to avoid interrupting the user. Production fullscreen uses the current monitor's physical bounds via SetWindowPos and does not enable Topmost. Light/dark editing menus, More surfaces, and small/fullscreen image captures were visually inspected.

The previous `dist/Jot-bidi` v1.3.0 build passed **284/284 background checks**. Earlier evidence is in **test-results/bidi-final-check/**; the coverage below is retained as history. See **docs/mixed-language-writing.md** for the bidi behavior and full coverage matrix, and **docs/responsiveness-assessment.md** for the earlier renderer/performance investigation.

New bidi checks cover the eight supplied identifier-led Persian examples and plain Copy equality, independent paragraph/cell/list direction, technical-unit isolation, visual ASCII URL order, selection/bookmark preservation, a caret at the start of a second paragraph, rich inline formatting, repeated-normalization stability, actual typing/backspace, image boundaries, source marks/ZWNJ/emoji, IME deferral, neutral-line keyboard stability, paragraph overrides via UI, Undo/Redo, real reload, export, and read-only index previews. Long unbroken text is tested for bounded token recognition. A save/hide assertion now checks the saved logical text and parsed HTML text, rather than requiring unbroken raw HTML across direction wrappers. No finite suite establishes all possible linguistic intent; ambiguous cases retain Auto/LTR/RTL overrides.

New paste checks verify text/plain taking precedence over colored HTML/code; no imported foreground/background/font styles; preserved tabs, blank lines, CR/LF/CRLF and literal markup characters; Persian/English paragraph direction; Save/Undo/Redo; HTML-only code flattened without losing indentation; HTML tables converted to text/tab separators; discarded scripts/event handlers; an empty clipboard leaving the note untouched; and text/image ordering without source styling. Image-only paste still preserves the original 600×200 image behind its thumbnail. Stored rich HTML and outgoing Copy remain supported, including tables and original-resolution images. Tests use in-memory clipboard events/DataObjects; they do not overwrite the user's clipboard.

New checks cover index-node identity, stale-action prevention, metadata/reordering updates, filter/search round trips, literal-star groups, cache removal after deletion, hidden-index refresh suppression and catch-up, and immediate Undo/Redo and formatting with pending typing. Index update work improved from 119ms median to 2ms in the 250-note fixture. Image-heavy edit-handler work improved from 12.4ms to 0.3ms; serialization remains necessary at idle/save/command boundaries. The direct native host is a measurement-only experiment with no production feature parity. WPF test windows remain transparent; that separate probe is opaque but positioned outside all monitor bounds with native no-activate/tool-window flags, no taskbar entry, no user data, and no clipboard/hotkey interaction. No actual desktop motion or Telegram parity is claimed.

Evidence for this release is under **test-results/controls-final/**: results.json, performance.json, custom tray menus in both themes, line-height controls, circular text-color controls, first-ready light/dark frames, composed active/inactive note windows, smaller corner masks, color palettes, deletion confirmation, pinned-note light/dark states, header More menus at normal/minimum size, monochrome icon size samples, and diagnostic test logs.

The new tray tests render the actual custom WPF template offscreen, check app branding and theme colors, exercise every command through its real button event, verify Settings reuses the index, create a separate note, flush notes through Quit, and check pending/error recovery. Escape dismissal and monitor-position calculations are covered, including negative coordinates and 100/150/200% sizes. Menu availability is also tested with a corrupt notes file, so a preferences read failure cannot remove Quit access. Live shell right-click/foreground activation, actual mixed-DPI monitor movement, and Windows overflow promotion were intentionally not exercised. The menu never becomes topmost and has no taskbar entry.

Startup policy checks verify that normal launch reveals the index, explicit `--tray` stays quiet, and test mode never creates a real tray icon. Existing native window property checks confirm taskbar eligibility without showing production windows. Windows owns tray overflow promotion; the new visibility action is routed to `ms-settings:taskbar` only on user action and is intercepted in tests. No registry preferences or other app icons are changed.

Line-height tests exercise More and Settings controls, synchronize multiple notes, verify computed spacing, persistence after reload, rejected out-of-range values, and unchanged rich HTML. Text color uses a filled circle; Clear formatting is absent. The separate upright T toolbar toggle stays unchanged. Inactive pinned notes hide Pin with all the other header buttons, and restore the whole group on hover.

## Performance comparison (v1.1.1 baseline)

Same-machine synthetic workloads, using the same offscreen host and benchmark routine before/after. Baseline: `test-results/perf-before/performance.json`. Final: `test-results/smooth-verified/performance.json`. These are observations under the machine's concurrent workload, not latency guarantees.

The current v1.2.0 run (`controls-final/performance.json`) measured a 1.8ms median edit handler (2.9ms p95), 136.7ms median 5.12 MB save/load, and a largest native UI timer gap of 22.5ms. The earlier comparison below is retained as historical evidence of the optimization, not presented as measurements from the new build.

| Measurement | Before | After |
| --- | ---: | ---: |
| 1,800-paragraph edit handler, median of 24 inputs | 25.2 ms | 2.2 ms |
| Same edit handler, p95 | 28.3 ms | 4.0 ms |
| 5.12 MB store save + load, median of 5 | 378.7 ms | 186.8 ms |
| Largest native UI timer gap during those saves | 191.8 ms | 23.4 ms |

Typing timings measure synchronous edit work, not full input-to-display latency. Store timings include durable flushes. The timer uses a 10ms interval; its gaps also include scheduler/GC noise. A previous optimized run measured 1.6ms edits and 146.9ms saves; the final numbers above are used rather than selecting the fastest result. No claim of uniform cold-start improvement is made: fresh WebView profiles and system load make that noisy.

New regression checks cover file-cache invalidation after external corruption; failed writes retaining committed cache/index data; summaries excluding rich HTML/original images; one-note-only editor loads; autosaves not rebroadcasting preferences; unchanged saves not rewriting disk; mutation-scoped bilingual paragraph updates; IME composition with undo/redo; native notes staying transparent before readiness; and the initial resolved crimson header in both themes. Newly created warm-session notes were ready in 298ms (light) and 371ms (dark) in the final run. All windows remained offscreen, non-activating, non-topmost, and transparent during tests. Actual foreground first-paint and taskbar pin/cache interaction were not manipulated.

The enlarged colored artwork fills each small ICO slot to within two pixels of its longer dimension, while the tray keeps its prior padding. Packaged color/dark/light ICOs are byte-checked against the export renderer. Runtime reads these assets rather than recomputing the raster exports.

## Regression coverage

New lifetime regression checks cover six image-viewer open/fit/actual-size/close cycles, replayed and queued focus callbacks after disposal, four closes during startup, undecodable images, and normal note save/hide/reopen. The original notes and shared session remain intact. The close-button test harness recognizes destruction of its DevTools target instead of waiting forever for a reply from a closed viewer. Startup awaits are cancelled per window; the shared environment is not cancelled. Closing-window errors are not hidden by a blanket dispatcher exception handler.

Icon checks verify colored native app frames, highest-resolution window selection, eight ICO sizes, identical alpha masks for the two tray inks, transparency, small-size visibility, and monochrome contrast. docs/icon-design/final-icon-board.png shows actual packaged pixels. Live Windows taskbar pin/cache behavior is still not manipulated.

The alignment regression checks measure the actual SVG rectangles—not just the button boxes. Every footer glyph is 16×16, centered in its button, and shares the same vertical center in dark/light themes and at the minimum window width. The original T show/hide icon is restored and remains upright in both expanded/collapsed states. The toolbar captures, including aligned-toolbar-minimum.png, were visually inspected.

The note header is checked for New note on the left and More → Pin → Close on the right, with matching DOM/tab order and no app-logo/Home button. Appearance is explicitly absent from notes, while Settings remains accessible through the existing index window. Menu-label tests cover All notes, compact 12px Light mode / Dark mode text, Font size with px units, and the exact Export / Copy / Delete labels. The separate plain-copy action and Text-style dropdown are absent. All 19 colors remain available.

Native pointer tests hover/click the disabled minus at 13px and plus at 24px: neither shows a wait cursor, has a loading animation, nor sends a preference write. The enabled opposite button moves away from each limit. Captures include both limit states. Explicitly busy actions still retain their pending spinner/label behavior.

Pointer-driven host checks exercise Copy and Export without touching the real clipboard or opening a save dialog. Theme and text-size actions are checked through the renderer.

Menu checks require all 19 distinct colors to be visible and hit-testable at once in one ordered row, with no palette scrolling. They cover left/right-arrow navigation, Home/End, selection retention, focus, and dismissal. Native pointer checks select the last color without scrolling at minimum size, then restore the original color. At the minimum 320×250 window, only the option list scrolls; the palette remains visible, Delete stays reachable, and opening/cancelling its confirmation preserves the note. Light/dark and minimum-size captures were visually inspected.

Bottom-toolbar checks verify bold/italic/underline, strikethrough, bullet/numbered lists, color, image insertion, and the toggle fit on a single footer row at minimum size. There are nine centered SVGs after removing Clear formatting. Strikethrough toggles on/off directly, with no More tools popup. The filled circle takes the selected text color; the compact color picker fits above the row. Previously stored headings/code remain supported; new paste is plain by default.

New-note checks verify crimson header defaults with a neutral app theme; a separate CSS-before-preferences check verifies neutral startup colors in both themes. Existing selected note colors remain intact. Pinned/inactive checks verify Pin hides alongside the other controls, retains its filled glyph and transparent background, and returns on header hover. All hovered header controls remain below the 8px color strip in light/dark mode. These tests never actually enable Topmost on the desktop.

Motion checks inspect real CSS transitions and Web Animations: intermediate header heights, 8px collapsed / 34px expanded states, stationary editor bounds, full-height menu reveal, inert closing controls, rapid reversal from the current frame, and cleanup. Paused start/middle/end frames are captured in more-opening-*.png and visually inspected; the new reveal is not tested merely by checking whether an Animation object exists. Toolbar and color-picker opening/closing plus reduced-motion behavior are also checked. DevTools motion emulation affects only the isolated test WebView and is reset afterward. Ordinary pointer checks wait until the target animation settles; dedicated motion checks inspect intermediate states.

Existing checks cover neutral global chrome with independent blue/rose note headers; all 19 note colors; legacy global-accent migration; color retention across saves, theme changes, and reloads; no header-color leakage into writing text; filled/unfilled Pin states; 6px outer/5px content corners; and an 8px inactive color strip without shifting the editor.

Deletion tests cover Cancel as default focus; cancel preserving the note; recovery-path and main-store-write failures preserving live notes; disabled/pending confirmation; saving the latest draft and original image before deletion; closing only the selected window; updating the index; rejection of stale saves, reopening a deleted ID, and cross-note deletion. Synthetic archived notes remain in the isolated test output, never the user's note store.

Icon checks cover eight ICO sizes from 16 to 256px and visible monochrome detail at 16, 20, 24, 32, 48, and 64px against light/dark backgrounds. Rendered small icons, first-ready header colors, native composition captures, circular color controls, hidden inactive Pin, and tray menus were visually inspected. In the current final run, opening the offscreen test home took 2.21 seconds and the 66,000-character write/save test took 166ms; these are local measurements, not startup or latency guarantees.

Existing checks cover: no shutdown/grip controls in notes; X wired to save/hide; New note aligned to the left; More covering the top of the note and constrained to small windows; transparent active Pin styling; neutral footer icons in both themes even when legacy preferences request colored icons; and taskbar eligibility for production home/note/image windows. Taskbar property checks construct but never display those windows; no live taskbar or foreground interaction is performed.

Existing checks verify: no note placeholder or save badge; keyboard-aware Persian/English caret behavior; an English-only UI even when legacy preferences request Persian; hidden tray startup without initializing a WebView; monochrome glyph contrast in both Windows themes; clipboard lock recovery/bounded retries/cancelled stale requests; clipboard failures not changing save state; safe logs that exclude private-content sentinels; bounded log rotation; and failure-tolerant logging.

New checks cover settings reusing the index's native window handle without increasing the window count; Back restoring the group filter; removal of promotional headings; a visible grouping action; neutral note surfaces with colored headers/top strips; Quit saving both pending note drafts; cancellation/retry after a failed save; and the pending spinner/label.

Existing checks cover editable titles/groups surviving concurrent editor saves; group filtering; English UI without rewriting Persian note content; active-keyboard empty caret direction; compact toolbar menus; tiny inline image insertion without paragraph breaks; original 600×200 clipboard image dimensions despite a 72×48 preview limit; native icon synchronization.

All checks run against the real .NET host and WebView2 renderer, with isolated notes and offscreen windows. The desktop remains untouched and the user's clipboard is never overwritten. Quit-button tests exercise the real save path with shutdown deferred; after a successful report, the shared production Quit method closes the isolated test windows and process.

Coverage:
- Home screen creates independent windows; reopening a note reuses its existing window.
- Concurrent editors preserve each other's data.
- Global theme and icon weight update across windows without changing each note's accent or independent writing preferences.
- The writing toolbar defaults to visible, can be hidden, and persists.
- Automatic Persian/English paragraph direction.
- Bold, italic, underline, colors, headings, lists, undo/redo.
- Plain paste discards source styling/structural markup while keeping text, whitespace, images and order; stored rich notes retain formatting on load/Copy.
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

Run instructions are in README.md. The prior live app was left untouched; the new build is in dist/Jot-context. Real mixed-content paste into Codex and live Windows tray/taskbar visibility/theme changes were not exercised. Clipboard-lock tests inject the actual HRESULT through the production retry routine without locking or changing the user's clipboard. DataObjects, generated native icon pixels, and hidden-window lifecycle were tested offscreen.
