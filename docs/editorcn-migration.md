# Editorcn migration (Jot 1.7)

Jot now uses the real `@editorcn/block-editor` components and Tiptap/ProseMirror for note bodies. React is limited to the editing surface; Home, tabs, Settings, Trash, note headings/emojis, native clipboard, SQLite, file handling and Windows controls remain Jot-owned. No server or runtime CDN is required.

## Building

Use Windows with .NET 10 SDK, Node.js 22 or newer and pnpm 11.10.0:

```powershell
pnpm install --frozen-lockfile
pnpm run check:editor
dotnet build Jot.csproj -c Release
```

The native build runs `pnpm run build:editor`. It bundles the pinned editor sources into ignored `assets/editor/editor.js`, `editor.css` and `editor-licenses.txt`, all included in the published app. `-p:SkipEditorBuild=true` is only for an already-current bundle. Generated assets and `node_modules` do not belong in Git.

The editorcn 0.3.4 npm package contains source but points its exports at missing `dist` files, so the build aliases to the pinned source. Narrow, asserted build-time adaptations route its Copy actions through Jot's native clipboard and place its bubble menu outside the scroll container with conventional focus behavior. The dependency files themselves are not edited. An upstream change to either adapted Copy location fails the build for review. Full licenses for actually bundled packages are generated from package metadata and shipped beside the bundle; editorcn's missing package license is retained from its exact npm source commit.

## Content and state

- `editor-src/entry.tsx` adapts a single editor/view to the native host and caches independent ProseMirror states per open note. It replaces the previous DOM snapshot/`execCommand` editing engine.
- `jot-extensions.ts` preserves note formatting, embedded original images, tables, checklists and paragraph direction. Mixed Persian/English isolation is a view decoration, not hidden text or manual DOM mutation.
- `jot-html-compatibility.ts` validates old HTML and checks text, formatting, images, links and structures after import. Unsupported content is read-only; the stored original remains available. Legacy table captions are retained as a paragraph immediately before the table.
- Opening a note retains its exact original HTML until a real edit. Undo back to its initial document also restores that original representation. Export/file dirty checks therefore do not mistake parser normalization for an edit.
- Clipboard text remains plain by default, with logical line breaks and original mixed images retained. Native Copy succeeds before Cut deletes anything. AltGr and composition remain typing operations.
- The old `writing-tools.js`, `document-blocks.js` and associated table CSS were retired; their previous source remains in Git history.

## Upgrade and recovery

An older library is backed up before its SQLite `user_version` advances to 3. This changes only the compatibility marker, not note bodies. Version-1 preference migration still passes through version 2 first. The earliest `jot.db.before-v3.bak` is immutable; after a failed/retried upgrade a fresh `jot.db.before-v3-current-*.bak` also records the actual database being upgraded. Failed or unverified backups prevent the upgrade.

Older Jot builds reject version 3, preventing their old sanitizer from dropping new rich nodes on autosave. New `.jot` files similarly use envelope version 2; the new reader accepts versions 1 and 2. The content fingerprint remains independent of that envelope version, so unchanged existing files do not become dirty or autosave merely because the app upgraded.

Use Jot 1.7 or newer after opening the library with this build. A pre-upgrade backup does not contain later edits: preserve the current library and files before considering a rollback. This review build does not modify Windows file associations; an association pointing at an older executable may need updating after review, especially when the new app is not already running.

## Verification

`--self-test` now selects the current editor regression suite, including native editor acceptance, upgrade/file compatibility, SQLite, Trash, library and shortcut tests. The earlier custom-editor snapshot harness is retained as historical test source, not used to validate the new engine. Focused probes are also available:

```powershell
Jot.exe --exit-probe --probe-scenario editor-migration --test-output <fresh-test-directory>
Jot.exe --exit-probe --probe-scenario editor-format --test-output <fresh-test-directory>
```

Tests use synthetic notes, offscreen windows and the private test clipboard bridge. Reported check counts and native process exit codes are recorded in `TESTING.md`; isolated timing samples are not a claim of Notepad parity.
