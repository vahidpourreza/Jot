# SQLite storage

Jot uses `Microsoft.Data.Sqlite` 10.0.12 and explicitly pins the native bundle `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 (SQLite 3.53.4 in the verified Windows package). Dependency versions are recorded in `packages.lock.json`. No server, EF Core, network database, or new image pipeline is introduced. The v1.6 development build adds independent note-view settings in schema 2; the prior v1.5 installer used schema 1.

## Layout and operations

- Normal data root: `%LOCALAPPDATA%\Jot`; test roots are isolated under the selected test-output directory.
- Main store: `jot.db`, schema version 2, Jot application ID. Separate `notes`, `groups`, `settings`, `app_state`, and `note_preferences` tables. Unrecognized legacy note/settings metadata is retained where supported by the existing model.
- Each `note_preferences` row belongs to one note (foreign key with delete cascade) and stores its font size, line height, toolbar visibility and pin choice. New notes snapshot writing defaults. Content autosaves never overwrite these settings, titles, groups or colors. Writing defaults do not restyle existing notes. Theme remains in the global `settings` table and updates every window. Early development builds' per-note theme values are ignored on read/import and removed on the next note-settings save; no schema change or note-content rewrite is needed for this correction.
- Images are still embedded in each note's original HTML. Index queries use stored text, metadata and an image-present flag, never the HTML payload. Opening one note reads only that note. Preference and note-color notifications do not load all notes.
- Each save uses parameterized SQL and a transaction. Content writes cannot overwrite titles, groups or colors from the index. Group changes, deletion and full legacy imports are transactional. Deleted-note recovery JSON is durable before the note is deleted.
- All provider calls, parsing and disk work run on background workers behind a per-root semaphore. SQLite's async ADO.NET methods are not used as a substitute for moving work off the UI thread. Connections are short-lived, pooling is disabled, and lock waits are bounded (3 seconds in production).
- WAL journaling, full synchronous durability and foreign keys are enabled. The bundled engine must be at least 3.51.3, excluding SQLite's older WAL-reset corruption bug. The provider dependency and actual runtime version are tested.

## One-time migration

1. If no SQLite store exists, validate the old version-2 `notes.json`, including IDs, duplicate IDs, text fields and timestamps. Do not edit the source or its `.bak` file.
2. Build a uniquely named staging database under the same data directory. Insert all records transactionally, preserving identifiers, order, active note, content, groups, titles, colors and timestamps. Existing English-only interface/neutral-app preference rules still apply.
3. Compare imported content, check SQLite integrity and foreign keys, close and durably flush the staged database, then move it to `jot.db` without overwriting any existing database.
4. Enable WAL and create a verified SQLite backup. The original JSON files remain recovery snapshots, not writable mirrors.

Invalid JSON, duplicate identifiers, unsupported schemas, bad timestamps and blocked paths fail visibly without replacing valid data. Temporary files from failed initialization attempts are removed only from the attempt's own unique paths. A valid existing database is authoritative, even if the old JSON has subsequently changed. A missing database with a known database backup fails closed rather than importing a stale JSON snapshot. A malformed database is never automatically overwritten or downgraded.

Migration is performed by the new app on first launch. Development verification does not migrate the user's actual note store. Quit the old Jot process before launching the new version: the single-instance mechanism otherwise activates the already-running old app. After migration, do not continue editing through an older JSON-based release, since those changes do not update SQLite.

## Work-window session

The optional `app_state.extra.desktopSession` object has version 1 and a `windows` array of note ID plus physical-pixel X/Y/Width/Height records. No schema upgrade, note-content rewrite, or image-storage change is needed. Normal Quit and Windows system-close commands share one operation: flush notes, transactionally record only visible non-minimized note windows, then update the database backup. System close is intercepted before any window is independently hidden, so taskbar Close all windows preserves the same note set as Quit. The note's custom X closes that note independently; when it closes the last app window, the app also quits. That last-X shutdown records an empty session, so next launch creates one blank note while all closed notes remain saved in Home. Home/Settings and image viewers count as open windows, including minimized windows, but hidden tray/cache windows do not prevent shutdown. Other app-state metadata is retained. Hidden notes and image viewers are not startup targets.

Startup loads the session without loading every note's rich HTML. Deleted IDs are filtered and duplicates removed. A missing or empty session creates one blank note; an invalid/unsupported session fails visibly instead of replacing data. Home is revealed only explicitly or as error recovery. Restored bounds are fitted to an available monitor before note content becomes visible. Snapshots require a normal Quit from this build; older builds and forced termination do not capture the current visible-window set.

## Backup and recovery

Opening an existing schema-1 database first creates a verified backup and a retained `jot.db.before-v2.bak` recovery snapshot. The settings-table creation, seeding of all existing notes from their prior shared settings, and schema-version change are then committed in one transaction. A backup failure leaves the schema/data unchanged. Normal rolling backups do not replace the retained pre-v2 snapshot. Older v1.5 binaries intentionally reject schema 2; do not alternate between old and new binaries against the same live store.

`jot.db.bak` is a standalone SQLite snapshot produced by `SqliteConnection.BackupDatabase`, verified with `quick_check`, durably flushed, and atomically published. It is created during JSON migration, before the first mutation of an existing database in a new store session, and after all editors flush on normal Quit. A backup failure does not overwrite the previous good snapshot. It is not a new full copy on each keystroke, and it is not an automatic restore mechanism.

When copying the data folder manually, quit Jot first. Do not copy just `jot.db` from a running app: committed transactions may still be in `jot.db-wal`. Do not manually remove WAL/SHM files while the store is open. For recovery, preserve the entire damaged data folder first, then restore a verified, consistent snapshot with all Jot processes stopped. Do not combine an older database with newer WAL sidecars. The retained legacy JSON is only the pre-migration state, not a current backup after further editing.

Errors are logged with operation, exception type, HRESULT and SQLite error codes. Logs omit note content, parameters, raw exception messages and clipboard/image data.

## Verification scope

The isolated suite exercises migration and re-opening, unchanged original JSON/backups, metadata and images, single-note writes, concurrent store instances, rollback after an earlier statement succeeds, lock contention, verified backups, failed backup retention, corrupt/future databases, retryable migration failure and stale-save rejection. Child-process probes deliberately terminate only their own invisible test process with either committed or uncommitted transactions, then reopen and integrity-check the test database. They never use the real data directory or normal app activation/mutex path.

The existing full editor/UI suite also runs offscreen, including writing, mixed Persian/English text, image viewing, Copy, Delete, settings, tray controls and window lifetimes. These checks do not simulate physical disk/controller failure or promise uniform end-to-end UI latency.

## Official references

- [Microsoft.Data.Sqlite async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)
- [Transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)
- [SQLite online backup](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup)
- [SQLite WAL behavior and WAL-reset fix](https://sqlite.org/wal.html)
