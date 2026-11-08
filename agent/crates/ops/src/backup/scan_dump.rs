//! The pre-scan of a dump someone else wrote.

use std::io::{ErrorKind, Read};

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::backup_error::{BackupError, PROGRAM_UNAVAILABLE};
use crate::backup::dump_lexer::DumpLexer;
use crate::backup::model::dump_refusal::DumpRefusal;

/// How many bytes are read from the dump at a time.
const CHUNK: usize = 64 * 1024;

/// Reads `source` to its end and refuses it if it holds anything a dump of
/// `database` must not.
///
/// Streamed in constant memory: a dump is a customer's whole database and can
/// be gigabytes. Nothing is executed, nothing is rewritten (the dump is hashed,
/// and a rewrite would destroy the digest check), and nothing the dump says is
/// kept: the answer is a [`DumpRefusal`], a closed kind, never a statement.
///
/// This is one half of the restricted load. The other, which holds even if this
/// one has a gap, is that the dump is loaded by a database user that can touch
/// `database` and nothing else. See [`DumpLexer`] for what each half is for.
///
/// Not bounded in time, because it reads a file already on local disk at the
/// speed of the disk; the load that follows is bounded by its call class.
///
/// # Errors
///
/// [`BackupError::RestrictedLoadRefused`] carrying why, and
/// [`BackupError::LoadFailed`] when the dump cannot be read.
pub(crate) fn scan_dump(
    source: &mut impl Read,
    database: &DatabaseName,
) -> Result<(), BackupError> {
    let mut lexer = DumpLexer::new(database.as_str());
    let mut chunk = vec![0_u8; CHUNK];

    loop {
        let read = match source.read(&mut chunk) {
            Ok(0) => break,
            Ok(read) => read,
            Err(error) if error.kind() == ErrorKind::Interrupted => continue,
            Err(_) => {
                return Err(BackupError::LoadFailed {
                    status: PROGRAM_UNAVAILABLE,
                });
            }
        };
        lexer.feed(&chunk[..read]).map_err(refused)?;
    }

    lexer.finish().map_err(refused)
}

/// Wraps a refusal in this area's error.
fn refused(reason: DumpRefusal) -> BackupError {
    BackupError::RestrictedLoadRefused { reason }
}

#[cfg(test)]
#[path = "../tests/backup/scan_dump_tests.rs"]
mod tests;
