//! Taking one database's dump, and hashing what came out.

use std::path::{Path, PathBuf};

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::archive::checksum_file::checksum_file;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::manifest_database::ManifestDatabase;

/// The flag that takes the whole dump inside one transaction.
///
/// Without it the dump is a sequence of independent reads and the result is a
/// database at no single point in time — tables that reference each other come
/// from different moments. A restore of that is a restore of a state the
/// customer's application never had.
const SINGLE_TRANSACTION: &str = "--single-transaction";

/// The flag that streams rows instead of buffering the whole table in the
/// client. A large table without it is a root daemon out of memory.
const QUICK: &str = "--quick";

/// The flag that includes stored routines.
const ROUTINES: &str = "--routines";

/// The flag that includes triggers.
const TRIGGERS: &str = "--triggers";

/// The flag that includes scheduled events.
const EVENTS: &str = "--events";

/// The flag that writes binary columns as hex.
///
/// A `BLOB` written as raw bytes goes through the dump file, the archive and a
/// reload as bytes something may re-encode; as hex it survives all three
/// unchanged.
const HEX_BLOB: &str = "--hex-blob";

/// The flag naming the file the client writes, spelled as `--flag=value`.
///
/// **`--result-file` and not a redirect**, because a redirect needs a shell and
/// there is no shell here (rules/security.md item 3) — and because the
/// alternative without one, plumbing the child's stdout into a file descriptor
/// the agent opened, is a descriptor to get wrong in a root daemon for no gain.
const RESULT_FILE: &str = "--result-file";

/// The flag that makes the client dump the named database WITH its
/// `CREATE DATABASE`.
///
/// Rather than the positional form, which dumps the tables alone: a restore
/// that has just dropped the database needs the statement that re-creates it,
/// and the alternative is the agent composing DDL of its own.
const DATABASES: &str = "--databases";

/// The extension every dump file carries inside the archive.
const DUMP_EXTENSION: &str = ".sql";

/// The complete argv the dump client is spawned with, program excluded.
///
/// In one place so that "what does a dump run with" is a question with one
/// answer and one test, rather than a line inside a host implementation nothing
/// asserts on.
///
/// **`--no-tablespaces` is deliberately absent.** It was measured as ACCEPTED
/// by the client on both families (`maran_distro`'s
/// `database_dump_binary` records the transcript), so its absence is a choice
/// rather than a compatibility limit: the flag exists to let a client dump
/// without the `PROCESS` privilege, and this agent connects over the local
/// socket as `root@localhost`, which has it. A flag that suppresses output the
/// connection is entitled to would be a smaller backup for no reason.
///
/// Every element is either one of the constants above or a value with a type:
/// `database` is a [`DatabaseName`], which cannot hold a space, a quote, a
/// semicolon or a newline, and `into` is built by this area from the agent's
/// own scratch path and that same name.
pub(crate) fn dump_arguments(database: &DatabaseName, into: &Path) -> Vec<String> {
    vec![
        SINGLE_TRANSACTION.to_owned(),
        QUICK.to_owned(),
        ROUTINES.to_owned(),
        TRIGGERS.to_owned(),
        EVENTS.to_owned(),
        HEX_BLOB.to_owned(),
        format!("{RESULT_FILE}={}", into.to_string_lossy()),
        DATABASES.to_owned(),
        database.as_str().to_owned(),
    ]
}

/// The file one database's dump is written to, inside the scratch's
/// `databases/` directory.
///
/// The name is the database's own, which is the archive's contract (R2:
/// `databases/<database>.sql`) and is also what lets a restore match a member
/// to a manifest entry without a second index. It is a [`DatabaseName`], so it
/// is a single path component by construction — there is no separator in its
/// alphabet.
pub(crate) fn dump_path(databases_directory: &Path, database: &DatabaseName) -> PathBuf {
    databases_directory.join(format!("{}{DUMP_EXTENSION}", database.as_str()))
}

/// Dumps `database` into the scratch's `databases/` directory and describes
/// what landed there.
///
/// The size comes from hashing the file rather than from the client's own
/// report, and the two are not the same claim: the client says how much it
/// wrote, and the hash says what is on the disk now. The manifest records the
/// second, because that is what a restore will read back.
///
/// A dump larger than `ceiling` is refused HERE, before the next one is taken.
/// A ceiling that exists is a refusal an operator can read; no ceiling is a
/// full disk at 03:00, and a full disk during a backup is a host whose other
/// services start failing.
///
/// # Errors
///
/// - [`BackupError::DumpFailed`] when the client refused or could not be run.
/// - [`BackupError::DumpTooLarge`] when what it wrote exceeds `ceiling`.
/// - [`BackupError::ChecksumUnreadable`] when the file it wrote cannot be read
///   back.
///
/// On every one of those the caller deletes the whole scratch: whatever the
/// client had already written is not a smaller backup, it is a truncated
/// database waiting to be restored over a working one.
pub(crate) fn dump_database(
    host: &dyn BackupHost,
    database: &DatabaseName,
    databases_directory: &Path,
    ceiling: u64,
) -> Result<ManifestDatabase, BackupError> {
    let path = dump_path(databases_directory, database);
    host.dump_database(database, &path)?;

    let (sha256, bytes) = checksum_file(&path)?;
    if bytes > ceiling {
        return Err(BackupError::DumpTooLarge {
            limit: ceiling,
            actual: bytes,
        });
    }

    Ok(ManifestDatabase {
        name: database.as_str().to_owned(),
        bytes,
        sha256,
    })
}

#[cfg(test)]
#[path = "../../tests/backup/archive/dump_database_tests.rs"]
mod tests;
