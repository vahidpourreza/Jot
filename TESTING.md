# Jot workspace verification

## Combined tint and accent strip (2026-10-03)

`dist/Jot-dev-combined-tabs/Jot.exe` always displays the soft tint and colored top strip together. The Tab colors Settings card, handlers and renderer mode attribute are removed. Defaults and preference reads no longer expose `tabColorStyle`; old stored values are ignored without rewriting note colors, content or the legacy settings row.

Focused `tab-colors` acceptance passed **30/30**, process exit 0 (`test-results/combined-tab-colors-20261003/tab-colors-results.json`). It checks all 19 colors in light/dark, selected/inactive and pinned tabs, readable text, visible 2px strips plus tinted surfaces, absent Settings controls, identical results with legacy full/accent settings, unchanged note colors and existing shortcut migration coverage. Header previews in both themes were visually inspected. Release publish, focused JavaScript syntax and whitespace checks passed. Broader editor/file/stress suites were not rerun for this scoped presentation change; their existing limitations below remain unchanged. The running reviewed build and its Windows file association were left untouched.

## Document editor, emoji and readable tabs (2026-10-03)

Current development build: `dist/Jot-dev-documents-reviewed/Jot.exe`. The user's running `dist/Jot-dev-documents/Jot.exe`, note store, clipboard and foreground were untouched. No dependencies, installer, commit or push were added. Tests use synthetic stores and offscreen, transparent, non-activating windows.

The document heading saves a title independently of its filename, with a complete local Unicode 18 emoji chooser. Formatting lives in the top toolbar, `/` command menu and selection toolbar. The browser-tab pins retain their labels; dragging uses pointer-following transforms, cached geometry and a single queued animation frame. A successful drop settles visually, while cancellation clears its transform/capture state immediately. User-facing title/filename behavior supersedes the older unified identity design; `.txt` keeps its title/emoji in Jot's library because the plain file has no metadata fields.

Final focused results (some coverage overlaps):

| Probe / evidence under `test-results/` | Assertions | Process result |
| --- | ---: | --- |
| `documents-acceptance-document-ui-20261003` | 69/69 | exit 0 |
| `documents-final-emoji-picker-20261003` | 43/43 | exit 0 |
| `documents-regression-document-files-20261003` | 24/24 | exit 0 |
| `documents-final-browser-tabs-20261003` | 97/97 | native exit `0xC0000602` after report |
| `documents-regression-workspace-files-20260929-20261003` | 158/158 | exit 0 |
| `documents-regression-editing-files-20261003` | 144/144 | exit 0 |
| `documents-regression-toast-design-20261003` | 38/38 | exit 0 |
| `documents-final-standard-editing-20261003` | 56/56 | exit 0 |
| `documents-acceptance-responsiveness-20261003` | 15/15 | exit 0 |

The tab probe reproduces the previously observed native WebView/CoreMessaging shutdown failure signature, including when run alone. Its passing feature assertions do **not** establish clean native shutdown. This limitation remains unresolved and is not masked as a successful process exit. A full long-running stress run and live desktop/global-hotkey tests were not performed.

Checks cover title IME updates, late Enter/focus races, failed metadata save/Retry, flush-before-switch/close, independent `.jot` title roundtrip, `.txt` metadata limitations, old v1/v2 dirty-state baselines, and explicit default-emoji hash compatibility. Slash tests preserve literal paths and unmatched Enter, reject stale choices and restore the query/format in one Undo. Scanning reads only a bounded suffix near the caret, tested on a 200k-character paragraph. Top menus expose keyboard focus/Escape behavior. Bilingual selection, rich Copy, paste spacing, file autosave/conflicts, shortcuts and saved tab order remain covered by regressions.

The emoji tests validate all 3,963 fully-qualified entries and 5,235 accepted forms in JS/native code, with categories, tone variants, search, keyboard paging, busy-cancel protection and exact saved-content preservation. Only 96 choices are rendered per page. Windows' emoji font can lack newer glyphs; searchable names and saved values are retained, and the picker explains that limitation. Narrow layouts confine unsupported multi-glyph sequences within their cells. Dark/light document, command-menu, selection-toolbar, tab and emoji captures were inspected.

The responsiveness fixture retains zero full-library reloads for eight ordinary saves and zero plain-text extractions for four clean flushes. Sixty synthetic edits produced one tab-render call; synchronous edit work measured 0.1ms median / 0.3ms p95. These are local fixture measurements, not input-to-display latency or Notepad parity claims. Release publish, source JavaScript syntax, emoji asset reproducibility and whitespace checks passed.

### Explicitly approved Windows association repair

On 2026-10-03, read-only checks found that only `.jot` referenced `jot_auto_file`, whose open command targeted an older build and whose DefaultIcon was absent. After user approval, the existing `HKCU\Software\Classes\jot_auto_file` open command and icon were updated to the reviewed executable and its ICO. Windows `AssocQueryString` resolved both new paths, and the protected UserChoice remained unchanged. No other extension, system-wide registry setting or running process was modified. The old subtree was exported first; `artifacts/file-association-20261003/` contains the backup and a guarded restore script. Registration uses the documented [DefaultIcon mechanism](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-assign-a-custom-icon-to-a-file-type) and [association-change notification](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify), without deleting caches or restarting Explorer. Actual Explorer-window display was not inspected or manipulated.

## Soft tab colors and selection appearance (2026-10-01)

The follow-up development build is `dist/Jot-dev-soft-tabs/Jot.exe`. The tab presentation now uses small note-color tints, neutral readable text and colored icons. Selected tabs use a stronger tint/border and medium-weight title without a bright bottom underline. The existing `full` preference value remains compatible and is labelled **Soft tint**; explicit Accent strip choices remain unchanged. Note colors, layout, tab commands and saved content are untouched.

The selection diagnostic reproduced the reported overlap: the original IRANSansX range rectangle is 20px high for 13px text, while 1.2× line spacing advances by only 15.59375px. At 16px and 24px, the original selection rectangles are 24px and 36px respectively. Evidence is in `test-results/soft-tabs-selection-probe-20261001/selection-geometry.json`, with one-line and all-lines captures. The probe uses only synthetic notes and does not alter the user's data or spacing preferences.

The editor now uses the same regular/bold font files through an editor-only alias with 80% ascent, 38% descent and zero line gap. Selection remains native. The alias keeps range rectangles within the chosen line pitch without changing wrapping, saved spacing, font size, content or UI fonts. Tall bold Arabic/Persian marks can extend beyond their selection background at minimum spacing; preventing all glyph-ink overlap would require changing the user's spacing or font. Those glyphs are deliberately not clipped. Existing custom font overrides and monospace code retain their own font metrics.

Soft-tint color/shortcut-migration checks passed **49/49**, exit 0 (`soft-tabs-colors-final-20261001`), with all 19 colors checked selected and inactive in both themes. Writing-default/paste checks passed **84/84**, exit 0 (`soft-tabs-writing-final-20261001`); standard native editing passed **56/56**, exit 0 (`soft-tabs-editing-final-20261001`). Header previews are `tab-colors/tab-colors-soft-header-dark.png` and `tab-colors-soft-header-light.png` in the color run. Initial color-probe assertions passed but its screenshot wait incorrectly excluded WPF's frame inset; that test wait was corrected. The full stress suite was not rerun for these scoped appearance changes.

Final selection acceptance passed **583/583**, exit 0 (`soft-tabs-selection-acceptance-20261001`): 13–24px, all six supported line heights, regular/bold bilingual text, bounded selection rectangles, unchanged line pitch/wrapping/selection/content, identical painted glyph pixels at the same baseline, no line clipping, preserved rich-copy/custom fonts, and unchanged saved writing preferences. An earlier assertion incorrectly required every tall glyph to fit inside the highlight background and failed for tight bold text; the corrected acceptance explicitly tests unchanged painted ink and no clipping instead. Both reports remain available. Release publish and whitespace checks passed; runtime CSS/markup match the reviewed source. The running older Jot was not closed or modified, and no installer was created.

## Browser workspace and responsiveness (2026-10-01)

Review build: `dist/Jot-dev-browser-workspace/Jot.exe` (development-only, no installer). The user's running older build and its notes/clipboard were not changed. All new test windows remain isolated, offscreen, transparent, non-activating and non-topmost.

Final focused acceptance and regression reports total **796/796 passing checks**, all with process exit 0; coverage overlaps between suites. Release publish, all source JavaScript syntax checks and `git diff --check` passed. Light/dark full-color tabs, compact/wide pins, overflow and bottom-right toasts were visually inspected from offscreen captures.

Browser-tab/color/shortcut-migration acceptance passed **74/74** (`browser-workspace-acceptance-browser-tabs-20261001/browser-tabs-results.json`): real composition-input reordering in regular and pinned sections, Escape/capture cleanup, Ctrl+Shift+T, closed-draft preservation, deleted/already-open history filtering, bounded history, transfer behavior, saved pin/order restoration, narrow overflow access, all 19 colors in both full/accent styles and themes, text contrast, unchanged stored note colors, reversible Settings and legacy shortcut collisions. The complete library/writing/session regression passed **222/222**, exit 0 (`browser-workspace-acceptance-library-refresh-20261001/library-refresh-results.json`).

The desktop-input investigation found a visible alpha-zero `Chrome_WidgetWin_1` owned by the live app's WebView browser process covering the desktop while its WPF host was minimized; no thread had stuck mouse capture. WPF still reports a minimized window as visible. The fix explicitly hides the WebView controller on minimize/hide, retains its layout, and restores it without navigating on show/restore. An event-driven process-scoped guard makes only the exact invisible composition surface click-through; it does not change other applications or opaque browser popups. This signature also matches an [upstream WebView2 report](https://github.com/MicrosoftEdge/WebView2Feedback/issues/5668); that report is supporting context, not proof of a vendor fix. `WebViewInputGuardTests` plus `BrowserHostVisibilityTests` passed **50/50** with exit 0 (`browser-workspace-desktop-input-20261001`). Actual desktop clicking while running the new build remains a user acceptance check; the running old app was not used as a mutation target.

`ResponsivenessTests` and `ToastIdleTests` passed **15/15** with exit 0 (`browser-workspace-perf-20261001`). The same synthetic fixture before/after uses 240 notes of 2,000 plain characters, eight ordinary saves, 28 open tabs, 60 edits and four clean flushes. The baseline is `browser-perf-baseline-20261001`:

| Work measured | Before | After |
| --- | ---: | ---: |
| Full-library loads for eight saves | 8 | 0 |
| Store operations for those saves | 32 | 16 |
| Tab redraws during 60 edits | 61 | 0 |
| Synchronous edit handler, median / p95 | 1.6 / 2.4 ms | 0.1 / 0.7 ms |
| Plain-text extractions for four clean flushes | 4 | 0 |
| Empty-toast hidden mutations in 400 ms | 24 | 0 |

Save completion timings have a 100ms polling floor in this harness and are not evidence of faster end-to-end saves. Typing measurements exclude input-to-display latency, startup, native movement and disk scheduling; Windows 10 Notepad parity is not claimed. Correctness checks cover live/hidden Home previews, search membership, stable unrelated card nodes, metadata reconciliation, stale file-state replies, `.txt` formatting-only edits and dirty/Undo state.

Bottom-right/no-X notification checks passed **38/38**, exit 0 (`browser-workspace-toast-design-20261001`): Web-style neutral cards, placement at 360/520px in both themes, keyboard focus/Escape, modal accessibility, queue/action handling, paused expiry, five-second ordinary/eight-second error durations, reduced motion and escaped text. Light/dark captures are retained in that run.

Regressions passed with exit 0: **156/156** file-status/workspace-tool checks (`browser-regression-workspace-files-20260929-20261001`), **139/139** editing/files/overflow checks (`browser-workspace-final-editing-files-20261001`), **46/46** custom-shortcut checks (`browser-workspace-final-custom-shortcuts-20261001`), and **56/56** standard-editing checks (`browser-regression-standard-editing-serial-20261001`). The first parallel standard-editing run exceeded the browser's multi-click interval for one Persian fixture because diagnostic roundtrips occurred between presses; it sent exactly one press per click, and the serial rerun passed. Initial tab acceptance caught and corrected SVG class assignment in All tabs and capture ownership during tab movement. Tab-drag verification uses real composition-controller input because this WebView runtime loses drag capture during CDP mouse moves, even before any DOM movement.

The historical long-running WebView/CoreMessaging shutdown failure is not claimed fixed by these focused suites. Full stress, live Windows hotkeys, real desktop mouse interaction and mixed-DPI monitors were not exercised.

## Evidence retention

Taskbar-menu verification, both new-build broad runs and the old-build comparison are archived at their original relative paths inside `test-results/verification-evidence-20260928-102938.zip` (519 files; each SHA-256 checked against its source). The corresponding browser profiles, temporary baseline copy, build intermediates and inactive superseded app were removed afterward. `docs/taskbar-icons.png` retains the reviewed glyph sheet. The app process and user note store were not interrupted or changed by this cleanup.

The 2026-09-28 workspace cleanup archived **2,007 reports/screenshots** in `test-results/verification-evidence-20260928-092558.zip`, verifying every archived file against its original SHA-256 before removal. Historical `test-results/...` and `artifacts/verification/...` evidence paths below now refer to entries inside that archive. Synthetic note stores and browser profiles were discarded, not archived. Older executable directories mentioned below were removed; the current `dist/Jot-dev-faceted-icon` build remains. All **661 protected source/app/installer/recovery files** matched their pre-cleanup hashes. No app tests or builds were rerun for this generated-file cleanup.

## Current development build (v1.6.0)

### Native editing, shared notifications and configurable shortcuts (current source, 2026-09-29)

Review build: `dist/Jot-dev-editing-controls/Jot.exe`. Focused validation passed with clean process exits: **56/56** standard-editing checks (`test-results/editing-controls-final-standard-editing/editing-results.json`), **36/36** notification checks (`editing-controls-final-toast-design/editing-controls-results.json`), and **46/46** configurable-shortcut checks (`editing-controls-final-custom-shortcuts/editing-controls-results.json`). Existing regressions also passed cleanly: **222/222** library/writing/session checks (`editing-controls-regression-library-refresh`) and **156/156** workspace/file-tool checks (`editing-controls-regression-workspace-files-20260929`). The editing/file regression wrote **139/139 passing assertions** (`editing-controls-regression-editing-files`) but then exited with native **0xC0000602**; this is not a clean process pass. The full long-running stress suite was not rerun. Release publish, JavaScript syntax, whitespace checks and reviewed light/dark screenshots passed. The previously recorded native `CoreMessaging.dll`/WebView teardown fault remains unresolved; these changes do not claim to fix it.

`JotWebView2CompositionControl.cs` corrects the WPF composition input boundary: on the second left/right mouse press, WPF raises both MouseDoubleClick and MouseDown, and the SDK forwarded both as browser presses. That turned a real word double-click into paragraph selection. The adapter suppresses only the duplicate MouseDown forwarding; the native double-click route remains intact, and middle/X buttons and stylus input retain their original handling. The editor continues using Chromium's native word boundaries rather than a replacement selection parser.

`StandardEditingTests.cs` checks single/double/triple clicks through real WebView pointer dispatch and the WPF routed input boundary. Fixtures include English, Persian, Persian ZWNJ, a mixed Persian/Latin run, rich formatting and legacy loose text, with unchanged content assertions. The broader typing matrix covers Shift-click, cross-paragraph dragging, Ctrl+arrow word movement, Ctrl+Shift+arrow selection, Ctrl+Backspace/Delete, Home/End, document-boundary selection, replacing selected text, Cut/Undo selection restoration, and a composed Persian insertion with ZWNJ and Undo. Tests stay within isolated offscreen windows and do not inject desktop-wide mouse/keyboard input.

Drag selection uses native composition input with an explicit held-left-button flag. A diagnostic matrix showed this WebView's CDP drag path failed even in a plain editable iframe with no app handlers, while native input selected correctly. That test limitation was not treated as an editor defect or worked around with custom JavaScript selection rules. Native routed-click fixtures restore the SDK test-only mouse/focus fields in `finally`.

`notifications.js` and `notifications.css` adapt the existing Web Sonner design without adding React or a dependency. Reference sources are `D:\Platform\Web\components\ui\sonner.tsx`, `components\feedback\responsive-toaster.tsx`, `app\globals.css`, and `node_modules\sonner\dist\styles.css`; the Web repository is unchanged. `ToastDesignTests.cs` covers neutral theme tokens, small title/description typography, error-icon-only color, dismiss controls, text-only rendering, live regions, no focus stealing, explicit-ID updates, a three-visible queue, priority for new failures, preservation of focused/running-action notices, timed/paused expiry, sticky errors, stale-action timer isolation, and reduced motion. Modal tests retain clickable top-layer notifications after the dialog closes. Light/dark captures are generated at 360px and 520px widths, with bottom-center bounds above the footer. Contextual form-validation errors intentionally remain inline.

`ShortcutBindings.cs` is the native action/default/validation catalog consumed by `shortcut-bindings.js`; app and file dispatch, displayed labels and optional global registration share that catalog. `CustomShortcutsTests.cs` covers recording, Save/Clear/individual Reset/Reset all, persistence and reload, duplicate/conflicting assignments, reserved editing/OS keys, bare-key and AltGr protection, physical-key layout independence, malformed-settings fallback, and 320/360/900px light/dark shortcut layouts. An injected registrar exercises global rebind conflicts and rollback, clears, disabling and release without reserving Windows keys. Standard editing and Windows reference rows are not remappable. Actual desktop-wide registration, OS shortcut conflicts and live key invocation remain manual acceptance items.

This task leaves the user's running `dist/Jot-dev-workspace-tools/Jot.exe` process and note store untouched. It does not build/install an installer, alter file associations, use the live clipboard, or exercise live Windows hotkeys. Only isolated development-build fixtures are in scope.

### File status, workspace tools and shortcuts (2026-09-29)

Review build: `dist/Jot-dev-workspace-tools/Jot.exe`. The integrated acceptance run passed **156/156 checks with process exit 0** in `test-results/workspace-tools-acceptance-20260929/feature-results.json`. Existing focused regression suites also passed cleanly: **139/139** editing/file checks (`workspace-tools-final-editing-files`), **221/221** library/writing/session checks (`workspace-tools-final-library-refresh`), and **107/107** note-window checks (`workspace-tools-regression-note-fullscreen`). The final shortcut-label refinement passed **26/26** checks with exit 0 in `workspace-tools-shortcuts-final/feature-results.json`. These totals include overlapping coverage, not distinct test cases. Release publish, all JavaScript syntax checks, and `git diff --check` passed. Icon picker, alignment, shortcut page, file-name/status and tab/library screenshots were visually reviewed. The full long-running stress suite was not rerun; its previously recorded native shutdown fault remains unresolved.

The current source distinguishes library autosave from external-file state. File-backed notes display their filename and Unsaved/Saving/Saved/Auto-save paused status, with dirty markers in the library and tabs. Open/Save as synchronize the note title with the filename stem; linked-note Rename routes to Save as, and old files are not renamed or removed. Optional Auto-save files defaults off and writes linked files after one second idle. Close/tab-close/workspace-close flush the latest draft and required file save first; an error retains the editable note. `.txt` dirty state compares plain text, while `.jot` also tracks its portable appearance/icon. Existing library drafts remain protected when the external file changes.

`FileStateAutosaveTests.cs` covers native state/filename transitions, dirty-state reopen, manual versus idle save, debounce, external-change pause and Save-as recovery, disabling automatic saving, inherited typography, text-only comparison, workspace-X flush, actual tab-X preflight, file-open transfer under separate-window mode, concurrent Save/ownership transfer and failed-close retention. `FileStateLegacyTests.cs` covers old bindings without rewriting saved content or assuming old notes inherited new defaults. `FileWriteRaceTests.cs` covers existing-writer conflicts, precommit failure, denied in-place writes and a replacement race. The write path preserves a displaced external version as `.jot-recovery-*`, reports its exact recovery location, and does not report the race as a successful save; Jot's version may already occupy the requested path.

Tabs is now the default only where no explicit opening choice exists; saved Separate windows remains respected. Workspace Keep Jot on top is independent of standalone-note Pin and library Pin to top, persists through Home/Settings navigation, and remains reachable through context menus in compact widths. Native maximize from Windows keyboard/snap entrypoints synchronizes work-area dimensions, frame removal and restore state, retaining taskbar space. `NativeWindowPresentationTests.cs` uses offscreen native fixtures and monitor-origin calculations; it does not claim a live desktop drag or mixed-DPI monitor acceptance test.

`NotePersonalizationTests.cs` covers the default notepad symbol, 12 icon choices, preset/custom single emoji, JS/native validation agreement, persistence and `.jot` roundtrip. Paragraph Auto/Left/Center/Right/Justify controls preserve direction and selection, support undo/redo, sanitize to allowed alignment values, retain alignment across load and apply the destination style to plain paste. Minimum-width menu/picker captures remain part of the offscreen fixture.

`ShortcutsSettingsTests.cs` covers the dedicated searchable Settings subpage, Back navigation, compact Auto-save setting, tab creation/switching/close, Home/Settings shortcuts, separate-note routing and modal/AltGr/composition guards. WebView-only key dispatch exercises Ctrl+N/T/W, Ctrl+Tab/Shift+Tab, Ctrl+1–9, Ctrl+comma, Ctrl+Shift+H/P without desktop keyboard injection. Optional Ctrl+Alt+N/J registration is tested through an injected registrar for conflicts, rollback, idempotence and release; testing sessions never reserve or invoke actual Windows-global keys.

No installer or machine file association was rebuilt or installed for this source iteration. No live-running user app, user note store, clipboard, foreground window, Explorer setting or OS hotkey registration was used as a test fixture. Real global-key conflict behavior and live Windows keyboard/snap operation remain manual acceptance items.

### Writing defaults, library organization and workspace restoration (2026-09-28)

This source revision adds app-wide Writing defaults with independent per-note font-size and line-height overrides. New notes inherit missing fields; Use default removes one override. Existing saved values remain explicit, including values equal to the previous defaults. The compact setting is now labelled Open mode. New coverage in `InheritedWritingTests.cs` checks inheritance, explicit equal-value choices, independent reset, persistence, rejected settings and narrow/wide Settings layouts.

`PasteSpacingTests.cs` exercises repeated Cut → Paste through the isolated native clipboard with ordinary paragraphs, intentional blank/trailing lines and inline images. It checks stable logical line breaks, rich Copy/Undo retention, and the effective writing style on newly pasted plain lines while existing rich paragraphs keep their margins. Earlier Cut/Undo selection regressions remain in `UndoSelectionTests.cs`.

The current library adds Pin to top/Pinned filtering, checkbox selection, bulk Pin/Unpin/Move/Delete and folder navigation in a wide sidebar or compact folder control. Library pinning is separate from always-on-top Pin. Bulk deletion flushes live drafts and archives every selected note before transactional deletion; associated external files are kept. Settings is a dedicated closable tab. The workspace window's X saves its open note/Settings tabs and active tab for relaunch, while an individual tab close deliberately removes that tab from the session.

File commands are now reached from the app icon's right-click menu or the editor context menu, with Ctrl+O/Ctrl+S/Ctrl+Shift+S retained. More settings and note-tab menus no longer duplicate those commands. Portable `.jot` export captures effective typography without turning inherited library settings into overrides.

Review build: `dist/Jot-dev-library-v2/Jot.exe`. The final focused run passed **221/221 checks with process exit 0** in `test-results/library-refresh-release-check-20260928/library-refresh-results.json`. The final production regression run passed **1,345/1,345 checks** in `test-results/library-refresh-full-final-20260928/results.json`, including those **221/221 new-feature checks**. The broad process nevertheless exited with native **0xC0000602** after writing the report, so this is **not a clean full-process pass**. Release publish, JavaScript syntax and whitespace checks passed. Wide/compact Home, folders, Settings, file menus, tab overflow and palette captures were visually inspected in light/dark themes. Tests used isolated data and offscreen windows; no installer, machine file association, live note store or running user app was modified.

The experimental WinForms direct-rendering benchmark is now opt-in with `--self-test --direct-rendering-probe`; Jot itself uses the WPF composition host, and every production regression remains in the regular suite. The first broad run wrote all 1,346 results (including 221/221 new-feature checks and seven outdated UI/timing expectations later corrected) before exiting with native `0xC0000602` in `CoreMessaging.dll`. Windows recorded the same module/version/offset with the earlier `Jot-dev-files` test build. The dump implicates native text-input/WebView teardown, but does not establish the exact caller. **The fault also recurred with the experimental benchmark excluded**, so that benchmark is not established as the cause. Production disposal and shutdown behavior remain unchanged; the long-run native shutdown fault is unresolved.

### Tab overflow, opening mode, undo selection and separate files (2026-09-28)

Current review build: `dist/Jot-dev-files/Jot.exe`. Earlier running instances were not interrupted. Overflow has previous/next controls and All tabs with keyboard/wheel navigation; 32-tab coverage checks first/middle/last access at 360/520/900px in both themes. Settings now controls normal library opens and migrates visible/minimized editors to the selected layout only after saving. Staged layouts roll back on failure, and obsolete queued tab selections cannot duplicate migrated editors. Cut/Undo/Redo retain rich formatting and directional selection; failed/stale clipboard operations leave content and history intact.

Open file, Save file and Save as support portable `.jot` and plain `.txt` files. File operations are explicit, tracked through Quit, bounded to 32 MB, written through a sibling temporary file and protected against outside modifications. UTF-8/UTF-16 input, exact multiline/indentation/trailing-newline text roundtrips, image/style/view preservation, repeated opens, read-only write failure, Ctrl+S and same-user file IPC are checked. Optional installer association definitions were prepared but no installer or real file association was installed.

The final full run passed **1,190/1,190 checks with process exit 0**, in `test-results/files-full-final-20260928-140132/results.json`. The separate file run passed **29/29 with exit 0**, in `test-results/files-isolated-20260928-135207/editing-files-results.json`. Earlier combined verification recorded 136/136 assertions but reproduced the already documented native teardown code 0xC0000602; the final full run exited normally. The final source-only CSS adjustment restricts file-save spinners to the three file controls, leaving color-picker progress styling alone.

The initial checks caught narrow tab viewport sizing, an incomplete synthetic Enter event, a fresh-row failure trigger that did not fire, extra text-file blank lines and a completed-file-task wait loop. These were corrected before the final run. No real clipboard, foreground window or user note store was manipulated. Publish, JavaScript syntax and diff checks passed; no commit or installer rebuild was made.

### New-note destination preference and shorter menu labels (2026-09-28)

Current review build: `dist/Jot-dev-preferences/Jot.exe`, published separately from the running workspace build. The tab menu now uses **More settings** with the same ellipsis as standalone notes, and **Open in window** with an outward-link arrow. Settings adds **New notes open in → Tabs / Separate windows**. The saved default is Separate windows; Home, floating-note, tray, taskbar and blank-startup New note actions honor the preference. Explicit New tab and Open in window retain their chosen destination, and changing the default does not move existing notes.

**40/40 focused checks passed with exit 0** in `test-results/new-note-settings-final-20260928-124719/new-note-settings-results.json`. Coverage includes persistence across store and workspace reload, legacy defaults, atomic rejection of invalid values, all creation entrypoints, folder retention, explicit overrides, unchanged existing editors, Settings failure recovery, note-create/save failures, reopening hidden Home, cold startup and creation during Quit. Settings and tab-menu screenshots were inspected in light/dark themes at 360×280 and 520×540. The first run exposed insufficient scroll spacing at the compact height; scroll padding now keeps the whole choice group reachable above the footer.

Development publish, JavaScript syntax and diff checks passed. The broader stress suite was not rerun for this focused preference change. No live window was closed, no installer was rebuilt, and no commit was made.

### Persistent workspace, tab actions and native maximize (2026-09-28)

Current review build: `dist/Jot-dev-workspace/Jot.exe`. It was built separately from the running previous `Jot-dev-tabs` instance. Home, Settings and the note editor now share `workspace.html`; changing views keeps the same document, tab strip, native controls, editor and library-card nodes. The screenshot transition helper was removed. All tabs have visible surfaces, and the permanent Home tab shows only the app icon with its accessible Home name. Workspace note headers are hidden; right-clicking a tab exposes Rename, Note options, Open in separate window, Copy, Export, Close tab and Delete. Double-click title/F2 also renames without changing note contents. Floating-note controls remain available.

Home and ordinary-note maximize now use the monitor work area, keeping the taskbar visible; image-viewer fullscreen retains its explicit full-monitor behavior. Blank-header dragging restores under the pointer and hands movement back to Windows. Blank-header double-click toggles maximize/restore. The focused window run passed **107/107 checks with exit 0** in `test-results/persistent-window-20260928-120743/note-fullscreen-results.json`, including actual WebView pointer capture and double-click events with isolated native pointer fixtures.

The initial workspace run passed **136/136 focused checks with exit 0** in `test-results/persistent-tabs-20260928-115747/library-results.json`. Normal view switching produced **zero navigation events**; nine isolated samples, including saving and readiness polling, measured 8–28ms (local observations, not guarantees). Light/dark captures at 360/520/900px were inspected. Further full-suite checks cover cold Settings startup and native readiness replacement on an actual renderer reload, including concurrent save/Home switching and rejecting old-document readiness messages. The prior screenshot-based implementation below is historical.

The final full regression run passed **1,012/1,012 checks** in `test-results/persistent-full-final-20260928-120847/results.json` and completed with **process exit 0**. JavaScript syntax, development publish and diff checks passed. Test data/windows/clipboard stayed isolated; no live user window was closed or replaced, no installer was rebuilt and no commit was made.

### Tab visual and switching refinement (2026-09-28)

Review build: `dist/Jot-dev-tabs/Jot.exe`. It was published separately so the already-running `dist/Jot-dev` instance was not interrupted. Tabs retain their approved placement, with keyed DOM updates, neutral rounded surfaces, small note-color dots and no whole-strip dimming. Home editors use a neutral utility row. Page navigation holds the last complete WebView frame in memory until the next page is painted; it then swaps without blending text. Note-to-note switching briefly emphasizes only the new writing area. Normal floating-note styling is unchanged.

**113/113 focused checks passed with process exit 0** in `test-results/tab-polish-verified-20260928-112659/library-results.json`. The suite includes all prior 85 library/tab checks plus stable node/focus/scroll identity, fixed control geometry across Home/editor/Settings at 360/520/900px, real midpoint animation inspection (one editor, no duplicated text), complete-frame capture/release, stale readiness guards, failed-save recovery, rapid switching and reduced motion. Thirteen light/dark/transition screenshots were inspected. The initial run caught a real narrow-screen mismatch from Home-only responsive control widths; those rules are now shared. Resize also reveals the active tab while ordinary rerenders retain its scroll position.

Navigation timing samples are included in the report as local offscreen observations, including capture and readiness polling, not performance guarantees. Source syntax, publish and diff checks passed. The broad stress suite was not rerun for this refinement; no installer, live-desktop manipulation or commit was made.

### Home-only tabs and familiar window controls (2026-09-28)

The current development executable is `dist/Jot-dev/Jot.exe`. Home owns the browser-style workspace; Home remains first and nonclosable on the library, Settings and editor pages. Standalone notes have no tab UI/actions and their + still creates a separate note. Home and notes use familiar minimize, square maximize, overlapping-squares restore and X icons. No installer or commit was made.

The final build passed **85/85 focused checks**, exiting cleanly with code 0: `test-results/home-tabs-final-20260928-110156/library-results.json`. They cover same-window ownership, permanent Home, save before switching/closing, closing the final tab, independent note preferences and undo, pending images, failure/retry, standalone transfer in both directions, closing Home during a delayed transfer, three rapid Home/editor navigation cycles with verified saves, normal-note controls, and legacy floating-tab session migration. Light/dark library captures at 360/520/900px and Home/editor captures were inspected.

The broad run recorded **931/931 passing assertions** in `test-results/home-tabs-full-20260928-105747/results.json`, including normal note Minimize and maximize/restore, editor behavior, taskbar actions, persistence and shutdown. After producing that complete report it exited with the previously documented native stress-suite teardown code `0xC0000602` (`-1073740286`), so this is not a clean full-process pass. The final transfer-close/navigation guards were subsequently verified by the clean focused run. All runs used isolated data, offscreen non-activating windows and synthetic clipboard data. No foreground app interaction was performed.

### Minimal taskbar Jump List (2026-09-28)

The current build is `dist/Jot-dev/Jot.exe`. It was built separately while the older faceted-icon build was running; the obsolete directory was cleaned only after that process was no longer running. No installer or commit was made. The only app-owned taskbar entries are Home, New note, Settings, in that order; the tray menu is unchanged.

**65/65 focused checks** passed in `test-results/taskbar-menu-01/taskbar-results.json`, with process exit 0. They verify ordered JumpTask metadata, no Recent/Frequent categories, real native ICO extraction in both themes, strict argument parsing, cold-action startup without unwanted blank/restored notes, existing Home/Settings reuse, preserved tabs/drafts, real secondary-process IPC for each action, repeated New note requests, rejected invalid commands, failure acknowledgement/recovery, clean listener shutdown, and the existing session-restoration matrix. All windows/data/IPC endpoints were isolated. The glyph sheet is `test-results/taskbar-menu-01/taskbar-icons.png`, not an Explorer screenshot.

Both broad runs recorded **908/908 passing assertions**, including 87 close/relaunch checks, then exited abnormally with `0xC0000602` (`-1073740286`) during final teardown. Windows Application Error event 1000 identified `CoreMessaging.dll` in the first isolated test process (PID 728), not the live user app. These are not considered clean process passes; reports remain in `test-results/taskbar-full-01/results.json` and `test-results/taskbar-full-02/results.json`. No unproven production shutdown workaround was added.

The previous pre-taskbar binary was copied into an isolated temporary directory and its tray assets were repackaged using its own exporter to avoid comparing mismatched assets. It recorded **862/862 passing assertions**, then reproduced the same `0xC0000602` exit (`test-results/taskbar-baseline-matched`). This demonstrates that the stress-suite teardown failure predates the taskbar work; its underlying native cause remains unresolved. An initial old-build run without matching the updated tray packages had one expected icon-package mismatch and exited 2, so it was not used as the successful-shutdown baseline. The isolated temporary copy was removed after comparison.

The running user's Explorer Jump List, pinning, foreground handoff and live theme switch were not exercised, to avoid changing/focusing their desktop. The actual shell menu is registered only by normal launches. The installer shortcut AppUserModelID metadata was updated but no installer was built or installed.

### Monochrome fold correction (2026-09-28)

The updated packer and tray mask passed **61/61 focused offscreen icon checks**, retained in `docs/icon-design/tray-fold-checks.json`. Regression checks cover solid fold interiors, transparent separation, uniform ink and identical light/dark alpha at all eight 16–256px resolutions. The colored master and colored ICO hashes are unchanged. The small-size review sheet was visually inspected and refreshed at `docs/icon-design/final-icon-board.png`. Builds passed with zero warnings/errors; the broad app suite and live Explorer tray interaction were not rerun. Only the mask and two tray ICO asset files were copied to the existing development app, so the running process/executable was left untouched; restart Jot to load them. Temporary build/test profiles were cleaned after verification.

The initial new regression probe exposed a test-helper limitation: native `Icon` could select a smaller frame than requested. It now reads each exact PNG frame from the ICO directory. The next run caught real interpolation-driven ink variations; applying tint after scaling fixed them without changing the alpha or runtime loading path.

`dist/Jot-dev-faceted-icon/Jot.exe` passed **34/34 icon checks** in `test-results/faceted-icon-final/icon-results.json` and **59/59 Home/tab checks** in `test-results/faceted-library-final/library-results.json` (**93/93 focused checks**). Release publish, `scripts/make-icon.ps1 -NoRestore`, and `git diff --check` passed. No installer or commit was made. The broad suite was not rerun for this asset-only integration; the previous full result is retained below.

The approved color master is byte-identical to the selected generated image, verified by SHA-256. Tests check native 16–256px resolutions, transparency, icon-slot fill, the high-resolution native-window icon, shared light/dark tray alpha, package/master equality, and the exact PNG payload for each icon size inside the executable. Home decodes the actual 1254×1254 master correctly in both themes. The actual-size color/monochrome sheet and Home captures were visually inspected; `docs/icon-design/final-icon-board.png` is the current sheet. Tray positioning was rechecked on invisible, non-activating, non-topmost test windows, including scaled layouts. Live Explorer icon-cache refresh and tray interaction were not exercised.

The first focused icon run had one test failure because it assumed a 1280px PNG. The test now reads the source image dimensions and waits for image loading; the approved artwork was not changed.

## Previous Home/library build

`dist/Jot-dev-library/Jot.exe` passed **862/862 full offscreen checks** in `test-results/library-full-02/results.json`, including **59/59 focused Home and tab checks** also recorded in `test-results/library-final-01/library-results.json`. Release publish, JavaScript syntax checks and `git diff --check` passed. No installer or commit was made, and no live user window, clipboard, tray or data profile was touched.

The Home checks cover the logo/window controls/app bar, fullscreen and exact restoration through Settings, search in English/Persian and keyboard variants, grid/list persistence, folder creation/rename/removal and moving notes, untitled cards, note colors, right-click Rename/Copy/Export/Delete, original-resolution image clipboard data, deletion cancellation/recovery copies, and responsive bounds at 360/520/900px in both themes. Grid and list captures were inspected in `test-results/library-final-01/library/`. Persian-writing guidance informed line-by-line preview isolation; stored note text is never normalized by search or previews.

Tab checks cover same-window creation, independent note preferences/colors, saved drafts, per-tab undo, rapid switches, failed-save recovery, pending image insertion, Home Copy flushing a live draft, moving an existing editor without duplication, active/inactive tab deletion, tab close versus window close, grouped-session restoration and Quit during an accepted tab transfer. Only the selected note's rich HTML is loaded; tab headers use a short metadata query. These are isolated native-window/WebView tests, not live desktop drag tests.

The first broad run stopped at a Delete click below the newly scrollable More menu's viewport. The click helper now scrolls the target into view before using actual pointer input. The standalone native delete/exit probe subsequently exited successfully with no windows (`test-results/library-delete-01/probe-exit.json`).

The full run also passes the prior text-color indicator check. Its fixed 160ms sleep was replaced with a bounded wait for the asynchronous color transition; text-formatting production behavior is unchanged.

## Previous last-window quit build

`dist/Jot-dev-last-close-final/Jot.exe` passed **87/87 focused close/exit checks** in `test-results/last-close-final-01/close-last-results.json`. Release publish, `node --check image.js` and `git diff --check` passed. No commit was made.

The broader run in `test-results/last-close-full-02/results.json` passed **797/798** checks. The remaining failure was `format-bold-italic-underline-color` (the text-color indicator assertion); formatting production code was not changed in this task. That full run preceded only the final image-viewer warning handler and its focused recovery checks. An earlier run also had 797/798, failing the inactive light-theme Pin check before its transition settled. Its captured image had the correct hidden icons; the test now waits for header transitions rather than a fixed delay and passed in the second full run. The broad suite is therefore not reported as fully green.

Closing the last app window now saves and quits rather than leaving a tray-only process. Invisible tray hosts and cached closed notes do not count; minimized notes/Home keep the app running. The checks include new-note/reopen races, parallel closes, pending image completion, failed session-save recovery and retry, image-viewer shutdown failure/retry with a visible error, and an empty restored-window session after closing everything individually.

Eleven isolated two-process scenarios cover explicit Quit, taskbar/system close, plus closing the last note, Home, Settings, image viewer and deleting the last note. Each verifies actual process exit with zero windows, final saved content, and a fresh-process relaunch. The five last-window scenarios reopen one blank note without Home; deleted notes are not resurrected. Evidence is under `test-results/last-close-final-01/system-close/`. No user window, clipboard, tray or profile was touched; no installer was built.

## Previous context-menu Home build

`dist/Jot-dev-context-home/Jot.exe` passed **89/89 focused offscreen checks** in `test-results/context-home-01/note-fullscreen-results.json`. New Home-menu checks cover its standard icon/no shortcut, keyboard access and small-menu scrolling, actual button activation, saving the latest draft, reusing Settings as Home without extra windows, keeping the note open, and empty-note/image context availability. Existing direction and fullscreen/drag checks also passed. Release publish, JavaScript syntax checks and `git diff --check` passed. The broad self-test suite was not rerun. No live window was touched, and no installer or commit was made.

## Previous instant-fullscreen build

`dist/Jot-dev-instant/Jot.exe` passed **81/81 focused offscreen checks** in `test-results/note-instant-05/note-fullscreen-results.json`. Release publish, JavaScript syntax, and `git diff --check` passed. The broad self-test suite was not rerun for this iteration; the previous full run is recorded below. No installer or commit was made, and the user's running build was untouched.

Fullscreen switching is immediate and retains the exact native compact rectangle. Dragging the blank fullscreen header downward restores the compact note beneath the pointer, then hands movement back to Windows. Tests cover exact 452×338 restoration, endpoint-only native frames (no tween), repeated/rapid toggles, old animation payloads, click/jitter/right-click/button/cancellation guards, under-pointer placement at 100/125/150/200% math scales, renderer state synchronization, and session persistence after dragging. Direction alignment and selection styling remain covered in both themes.

The test dispatches trusted mouse events inside the offscreen WebView, checks pointer capture, and deliberately moves from header to editor in one event. That uncovered a header-only move listener gap; active gestures now follow document-level moves too. Evidence: `fullscreen-pointer-trace.json` and `fullscreen-frames.json` in the same report directory. The actual native OS drag loop and mixed-DPI monitor movement were not exercised on the live desktop; tests substitute a private offscreen pointer and verify the handoff branch without touching the real cursor.

The native handoff uses [WM_NCLBUTTONDOWN / HTCAPTION with screen coordinates](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nclbuttondown). A current-button check uses the high bit of [GetAsyncKeyState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate), including swapped physical mouse buttons. Native movement is scheduled after the bridge reply so holding a drag does not create a request timeout. Tests never move the real cursor or enter the live desktop's native move loop.

## Previous animated-fullscreen build (superseded)

`dist/Jot-dev-window-motion/Jot.exe` passed **701/701 offscreen checks** in `test-results/window-motion-optimized/results.json`. Release build/publish, JavaScript syntax checks and `git diff --check` passed. No installer or commit was made, and no live user window/monitor was resized.

Direction checks measure the actual text ranges, not just button boxes: option text is horizontally centered, and its vertical center matches the Direction label in both themes. Selected options use a soft border-free fill; the active fullscreen button has a transparent background. Small-window dark/light captures were inspected in `test-results/note-motion-01/note-tools/`.

This earlier build animated native HWND position and dimensions with a 220ms ease-out and restored the exact saved native rectangle, including an explicitly resized/moved 452×338 fixture. The animation was subsequently removed at the user's request. Its historical frame trace is `test-results/window-motion-optimized/fullscreen-motion-frames.json`; this is native offscreen motion evidence, not a claim about visible desktop FPS or mixed-DPI monitor behavior.

The earlier focused motion/UI probe passed **45/45** checks in `test-results/note-motion-01/note-motion-results.json` before the per-frame layout optimization. The replacement probe runs with `Jot.exe --exit-probe --probe-scenario note-fullscreen --test-output <fresh-isolated-directory>`.

## Previous note-tools checks

`dist/Jot-dev-note-tools/Jot.exe` passed **682/682 offscreen checks** in `test-results/note-tools-01/results.json`. Release build/publish, JavaScript syntax checks and `git diff --check` passed. No installer was built and no commit was made. No live user window, monitor fullscreen state, clipboard or notes were modified.

New checks cover a note's header fullscreen/exit button, borderless simulated-monitor dimensions, unchanged peer note/content/preferences, reload synchronization, hide/reopen, repeated transitions, original bounds/chrome restoration, and retaining the compact bounds in the saved work session. The shared native fullscreen path retains image-viewer coverage and does not enable Topmost. True monitor/taskbar fullscreen coverage and mixed-DPI desktop transitions remain untested on the live desktop.

Right-click direction checks cover Auto/LTR/RTL state, current paragraph and multi-paragraph selections, mixed-selection state, automatic Persian/English flow, text preservation, code-block protection, keyboard navigation within the direction choices, undo/redo and save/reload. Actual button clicks and bounds checks run in both themes at minimum note size. The Persian-writing guidance informed preserving isolated Latin fragments and paragraph-local direction without rewriting user text or changing the English app interface.

Visually inspected captures: `test-results/note-tools-01/note-tools/note-fullscreen.png`, `note-direction-menu-dark.png`, and `note-direction-menu-light.png`. The minimum-size context menu scrolls while keeping its direction choices usable. Existing menu actions and More → Direction are retained.

## Previous tray-anchor checks

`dist/Jot-dev-tray-anchor/Jot.exe` passed **75/75 focused tray-anchor checks** in `test-results/tray-anchor-01/tray-anchor-results.json`. Release publish and `git diff --check` passed. This iteration changes tray geometry only; the broader lifecycle suite was not rerun. The previous full run is recorded below. No installer or commit was made.

Unlike previous placement-only tests, the new probe moves the actual WPF popup HWND using the production positioning path, then measures the visible border with `PointToScreen`. All eight light/dark measurements at 100/125/150/200% rendering scales report **dx=0 and dy=0** between the menu's bottom-right and the specified icon top-left. Expanding an error message retains that same corner. Measurements: `test-results/tray-anchor-01/tray-anchor-measurements.json`. The HWND remains invisible, non-activating, non-topmost, offscreen, and absent from the taskbar throughout.

Focused checks also cover monitor-boundary flips, negative screen origins, icon hit-cell padding, transparent bitmap padding, and the safe click fallback. The production resolver derives visible icon bounds from the shell rectangle, current-DPI small-icon size and ICO alpha bounds; the native positioning code uses measured border offsets instead of an assumed six-pixel shadow. No live Explorer tray registration or right-click was exercised.

Repeat just this focused check with `Jot.exe --exit-probe --probe-scenario tray-anchor --test-output <fresh-isolated-directory>`.

## Previous system-close checks

`dist/Jot-dev-system-close/Jot.exe` passed **649/649 checks** in `test-results/system-close-01/results.json`. Release build/publish and `git diff --check` passed. No installer or commit was made. No user app, taskbar, tray or note content was manipulated by testing.

Six new two-process scenarios cover normal Settings Quit, taskbar/system `SC_CLOSE`, direct `WM_CLOSE`, Home only, one individually hidden note, and notes without Home. Each first process delivers Windows close messages only to its own verified invisible/non-activating/non-taskbar windows, actually exits through `Application.Shutdown`, and records a zero-window exit. A separately launched process then restores and verifies the exact note IDs, or one blank for an empty session, with Home still dormant. All original last drafts are checked from the isolated database after the first process has exited. Evidence is in `test-results/system-close-01/system-close/<scenario>/probe-expected.json`, `probe-exit.json`, and `probe-restored.json`.

The production native-window hook now routes system close to the single shared Quit operation before WPF can turn those requests into independent note hides. Custom note X continues to hide just its own note. Low system-command flag bits are masked as specified by [WM_SYSCOMMAND](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-syscommand); direct [WM_CLOSE](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-close) is covered too. These are real native messages and process terminations, not merely saved-session helper tests. Explorer's menu itself is not clicked on the live desktop.

Tray placement checks now follow the Docker screenshot reference: upward, right-aligned to the icon; the visible card can reach the icon at the taskbar without an extra work-area gap. Monitor edges still constrain the menu, with direction flips for other taskbar edges. Jot's compact light/dark styling and no-default-selection behavior are retained. Live Shell popup placement remains unverified.

## Previous lifecycle checks

`dist/Jot-dev-lifecycle/Jot.exe` passed **618/618 offscreen checks** in `test-results/lifecycle-matrix-final/results.json`. Release publish, JavaScript syntax checks and `git diff --check` passed. No installer was built, no commit was made, and the running `dist/Jot-dev-resume` app was left untouched.

The added lifecycle matrix executes actual note-X, native-close, Settings-Quit and tray-dispatch paths against isolated windows/stores, rather than only calling snapshot helpers. Cases cover Home+notes, Settings+notes, notes only, Home only, tray only, one closed note, a reopened note, native-close, minimized notes, close/Quit races, reopen during a slow close, late text without an input notification, a pending image, failed-save/retry, new-note/Quit races, stale save acknowledgements and repeated Quit. Restarts verify exact note IDs (or one fresh blank), complete final bilingual text/content, and dormant Home. Failure preserves the old session and editable drafts. Pending image content is checked in stored HTML; original-resolution behavior remains covered by the existing image/clipboard checks.

The first matrix run caught an inert-editor/pending-insert ordering problem and a test-mode native-close regression. Both were corrected before the final passing run. Notes now wait for pending inserts before becoming inert, force a final DOM snapshot, and wait out an earlier autosave. Native close intent and uniquely correlated flush acknowledgements prevent stale operations from reopening/closing the wrong state. Accepted note creations finish before Quit snapshots the session.

Tray geometry tests now use the same icon-edge placement function as production, including the user's 1440×900 screenshot case, overflow near either horizontal edge, four taskbar edges, negative monitor origins and 100/125/150/200% scale. The .NET 10 NotifyIcon identity adapter is tested using an invisible, unregistered icon. [Shell_NotifyIconGetRect](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyicongetrect) supplies the actual icon bounds in production; this supersedes whole-overflow-panel exclusion. No live tray icon/foreground is manipulated by tests, so live Shell interaction remains unverified.

## Previous note-first startup checks

`dist/Jot-dev-resume/Jot.exe` passed **496/496 offscreen checks** in `test-results/resume-work-final/results.json`. Release publish, JavaScript syntax checks and `git diff --check` passed. No installer was rebuilt, and the user's running `dist/Jot-dev-simple` process was left unchanged.

New restart checks use independent session/store instances and fresh synthetic data: first launch creates one blank note with Home's WebView dormant; Quit captures visible notes, excludes hidden notes/viewers, and backs up the session; reopening restores the same notes once, with rich bilingual content, per-note settings and saved dimensions. Repeated activation reuses visible notes, empty sessions create a new note without replacing old notes, missing/deleted IDs are ignored, malformed layouts cannot replace a good snapshot, unrelated app metadata is retained, and failed session writes leave windows and the previous snapshot intact. Geometry is clamped against positive/negative work areas. No production data is read or changed.

Tray checks reproduce overflow-panel exclusion rectangles across scales and monitor origins. Placement avoids them, rather than only clamping against the taskbar. A remembered Settings focus is cleared to a non-action root on every open. Light/dark tray previews show Home and the standard gear without preselected rows. Actual Explorer foreground/overflow interaction and production note placement are not driven on the live desktop; test windows stay invisible, non-activating and non-topmost.

Implementation references: [Microsoft's notification-icon menu foreground guidance](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackpopupmenu#remarks), [WindowFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-windowfrompoint), and the [Lucide settings glyph](https://github.com/lucide-icons/lucide/blob/main/icons/settings.svg). Jot retains its custom WPF popup, not a native menu. Only a user-opened production popup temporarily becomes topmost; note Pin behavior is unaffected.

## Previous simplified-shell checks

`dist/Jot-dev-simple/Jot.exe` passed **460/460 offscreen checks** in `test-results/simple-shell-final/results.json`. The simplified index and Settings were visually checked in both themes, including the 360×280 minimum window size. The tray previews show four compact rows without shortcut labels or a forced first-row outline. JavaScript syntax checks, Release build/publish and `git diff --check` passed. No installer was built; the user's running `dist/Jot-dev-theme` app was not touched.

The index now exposes New note, Settings, Minimize and Close to tray, with title-only Rename. Tests verify that group data survives rename/content saves but no group/search UI remains, every note stays accessible, Settings reuses the window, and both close/minimize buttons route to distinct native actions. Global Settings has only Theme and Quit; note writing settings remain independent.

Jot-specific hotkeys/shortcuts are removed: the native registration entry point is absent, and custom note/index/settings/image key combinations do not invoke app actions or change toolbar/fullscreen/zoom state. Standard editing keys and accessible menu/dialog navigation remain supported. Image fullscreen/zoom controls still work through their buttons and custom menu.

Tray checks cover 212-DIP width, 32-DIP rows, four actions, light/dark rendering, pending/error recovery, and pixel-placement bounds for top/bottom/left/right taskbars, overflow anchors, growing error content, negative monitor coordinates and 100/125/150/200% sizes. Production placement occurs before revealing/activating the menu and is recalculated after sizing. Actual live-shell anchoring and cross-monitor DPI changes were not exercised, to avoid interrupting the desktop.

## Previous global-theme development checks

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
