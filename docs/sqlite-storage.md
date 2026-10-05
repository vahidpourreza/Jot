# SQLite storage

Jot uses `Microsoft.Data.Sqlite` 10.0.12 and explicitly pins the native bundle `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 (SQLite 3.53.4 in the verified Windows package). Dependency versions are recorded in `packages.lock.json`. No server, EF Core, network database, or new image pipeline is introduced. Version 1.7 uses schema 3 as an editor-compatibility boundary; its table layout is unchanged from v1.6/schema 2. The prior v1.5 installer used schema 1.

## Layout and operations

- Normal data root: `%LOCALAPPDATA%\Jot`; test roots are isolated under the selected test-output directory.
- Main store: `jot.db`, schema version 3, Jot application ID. Separate `notes`, `groups`, `settings`, `app_state`, and `note_preferences` tables. Unrecognized legacy note/settings metadata is retained where supported by the existing model.
- Each `note_preferences` row belongs to one note (foreign key with delete cascade). New notes store local toolbar visibility and always-on-top pin choices, but omit `fontSize` and `lineHeight` until explicitly changed. Missing typography fields inherit current global `settings`; a null font/line-height patch removes only that override. All existing saved typography stays explicit, even when it equals the former default, until Use default is chosen. Schema-1 upgrades snapshot the prior effective values. Content autosaves never overwrite these settings, titles, groups or colors. Theme remains global. Early development builds' per-note theme values are ignored on read/import and removed on the next note-settings save; no further schema change or rich-content rewrite is required.
- Images are still embedded in each note's original HTML. Index queries use stored text, metadata and an image-present flag, never the HTML payload. Opening one note reads only that note. Preference and note-color notifications do not load all notes.
- Each save uses parameterized SQL and a transaction. Content writes cannot overwrite titles, groups or colors from the index. Group changes, deletion and full legacy imports are transactional. Deleted-note recovery JSON is durable before the note is deleted.
- All provider calls, parsing and disk work run on background workers behind a per-root semaphore. SQLite's async ADO.NET methods are not used as a substitute for moving work off the UI thread. Connections are short-lived, pooling is disabled, and lock waits are bounded (3 seconds in production).
- WAL journaling, full synchronous durability and foreign keys are enabled. The bundled engine must be at least 3.51.3, excluding SQLite's older WAL-reset corruption bug. The provider dependency and actual runtime version are tested.

## One-time migration

Before moving a schema-2 database to schema 3, Jot creates and verifies the permanent `jot.db.before-v3.bak`. If an earlier attempt already left a valid backup, it is preserved and a fresh `jot.db.before-v3-current-*.bak` snapshots the current data as well. Only the version marker changes; note HTML, preferences, links and metadata are not rewritten. Bad or obstructed backups prevent the upgrade. Older builds reject schema 3, avoiding lossy saves through their retired editor. See [editorcn migration](editorcn-migration.md) for `.jot` file versions and rollback cautions.

1. If no SQLite store exists, validate the old version-2 `notes.json`, including IDs, duplicate IDs, text fields and timestamps. Do not edit the source or its `.bak` file.
2. Build a uniquely named staging database under the same data directory. Insert all records transactionally, preserving identifiers, order, active note, content, groups, titles, colors and timestamps. Existing English-only interface/neutral-app preference rules still apply.
3. Compare imported content, check SQLite integrity and foreign keys, close and durably flush the staged database, then move it to `jot.db` without overwriting any existing database.
4. Enable WAL and create a verified SQLite backup. The original JSON files remain recovery snapshots, not writable mirrors.

Legacy notes without a `view` object receive explicit font-size and line-height snapshots from their previous global preferences. Supplied view objects, including deliberate sparse overrides, are retained. This compatibility step applies only to legacy JSON migration/browser import; creating a note or opening a new portable `.txt`/`.jot` file does not trigger it.

Invalid JSON, duplicate identifiers, unsupported schemas, bad timestamps and blocked paths fail visibly without replacing valid data. Temporary files from failed initialization attempts are removed only from the attempt's own unique paths. A valid existing database is authoritative, even if the old JSON has subsequently changed. A missing database with a known database backup fails closed rather than importing a stale JSON snapshot. A malformed database is never automatically overwritten or downgraded.

Migration is performed by the new app on first launch. Development verification does not migrate the user's actual note store. Quit the old Jot process before launching the new version: the single-instance mechanism otherwise activates the already-running old app. After migration, do not continue editing through an older JSON-based release, since those changes do not update SQLite.

## Work-window session

The optional `app_state.extra.desktopSession` object remains version 1. Its `windows` array records visible, non-minimized standalone notes and their physical-pixel bounds; an optional `workspace` records note-tab IDs, the active tab, whether Settings is open, and workspace bounds. No schema upgrade or note-content rewrite is needed. Normal Quit and Windows system-close commands flush editors, save the session transactionally and update the backup. Closing the workspace with its window X also saves its snapshot before hiding and marks it for retention by a later Quit. Closing an individual note tab removes it from that workspace membership; it does not delete the note.

A standalone note's custom X still closes that note independently, excluding it from the standalone restoration list. Closing the last app window quits Jot; Home/Settings, notes and image viewers count, including minimized windows, while hidden tray/cache windows do not prevent shutdown. Image viewers are never startup targets. Other app-state metadata is retained.

Startup reads session metadata without loading every note's rich HTML, filters deleted IDs and removes duplicates. Notes reopen according to Open mode. A recorded Settings tab reopens in the workspace; a saved workspace may therefore reveal Home during startup. With no saved notes or Settings tab, startup creates one blank note. Invalid/unsupported session data fails visibly instead of replacing notes. Restored bounds are fitted to an available monitor. These are orderly close/Quit snapshots, not recovery of unsaved UI state after forced termination.

## Backup and recovery

Opening an existing schema-1 database first creates a verified backup and a retained `jot.db.before-v2.bak` recovery snapshot. The settings-table creation, seeding of all existing notes from their prior shared settings, and schema-version change are then committed in one transaction. A backup failure leaves the schema/data unchanged. Normal rolling backups do not replace the retained pre-v2 snapshot. Older v1.5 binaries intentionally reject schema 2; do not alternate between old and new binaries against the same live store.

`jot.db.bak` is a standalone SQLite snapshot produced by `SqliteConnection.BackupDatabase`, verified with `quick_check`, durably flushed, and atomically published. It is created during JSON migration, before the first mutation of an existing database in a new store session, and after all editors flush on normal Quit. A backup failure does not overwrite the previous good snapshot. It is not a new full copy on each keystroke, and it is not an automatic restore mechanism.

When copying the data folder manually, quit Jot first. Do not copy just `jot.db` from a running app: committed transactions may still be in `jot.db-wal`. Do not manually remove WAL/SHM files while the store is open. For recovery, preserve the entire damaged data folder first, then restore a verified, consistent snapshot with all Jot processes stopped. Do not combine an older database with newer WAL sidecars. The retained legacy JSON is only the pre-migration state, not a current backup after further editing.

Errors are logged with operation, exception type, HRESULT and SQLite error codes. Logs omit note content, parameters, raw exception messages and clipboard/image data.

## Library folders and tabbed windows

Home treats rows in the existing `groups` table as durable optional folders. Empty folders are retained; rename keeps group identity, and removing a folder transactionally moves its notes to Unfiled before removing the folder. `LoadIndex` returns folder names, while full-model import/export retains empty folders. The schema remains version 2.

Library pinning is stored as `libraryPinned` in note metadata and is independent of the note window's `view.pinned` always-on-top setting. Summary queries expose the library flag for sorting/filtering without loading rich HTML. Bulk Pin/Unpin, Move and Delete validate the complete selection and commit in one transaction. Delete flushes affected live editors and writes all recovery archives before any selected row is removed; a failed archive or SQL statement leaves the selection intact. Linked external files are not deleted. Accepted library operations are tracked through close/Quit.

Older window-session records with a `Tabs` array alongside `NoteId` remain readable. Current workspace state separately records its note tabs and Settings tab. Only the active note tab owns the shared editor; switching durably flushes pending input before changing identity. Home Copy/Export flushes the live editor and uses original HTML/image data. The tab-label query selects short summaries without loading rich HTML or images.

The editor's saved plain-text summary and clipboard text fallback use logical DOM line boundaries rather than layout-derived paragraph gaps. Intentional empty/trailing lines remain, while repeated Cut/Paste does not accumulate blank paragraphs. Only newly pasted plain-line paragraphs receive the plain-line presentation marker; existing rich HTML is not restyled. A portable `.jot` export snapshots effective font size and line height into the file while leaving sparse library overrides unchanged.

## Verification scope

The isolated suite exercises migration and re-opening, unchanged original JSON/backups, metadata and images, single-note writes, concurrent store instances, rollback after an earlier statement succeeds, lock contention, verified backups, failed backup retention, corrupt/future databases, retryable migration failure and stale-save rejection. Child-process probes deliberately terminate only their own invisible test process with either committed or uncommitted transactions, then reopen and integrity-check the test database. They never use the real data directory or normal app activation/mutex path.

The existing full editor/UI suite also runs offscreen, including writing, mixed Persian/English text, image viewing, Copy, Delete, settings, tray controls and window lifetimes. These checks do not simulate physical disk/controller failure or promise uniform end-to-end UI latency.

## Official references

- [Microsoft.Data.Sqlite async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)
- [Transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)
- [SQLite online backup](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup)
- [SQLite WAL behavior and WAL-reset fix](https://sqlite.org/wal.html)
