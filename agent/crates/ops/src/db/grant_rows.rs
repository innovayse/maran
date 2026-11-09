//! Reading the server's database-level grant table.

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;

/// The statement that asks the server for every database-level grant it holds.
///
/// Every row, not only the local ones: a row at another host carrying the same
/// unescaped wildcard is the same class of exposure reachable from further away,
/// and an operation that filtered it out in SQL could not report it.
const GRANT_ROWS: &str = "SELECT Host, Db, User FROM mysql.db";

/// The character the client's batch output escapes with.
pub(crate) const BATCH_ESCAPE: char = '\\';

/// Every row of the server's database-level grant table.
///
/// # Errors
///
/// - [`DbError::Unparsable`] when a row is not the three fields asked for.
/// - Otherwise whatever the client failed with; see [`DbHost::execute`].
pub(crate) fn grant_rows(host: &dyn DbHost) -> Result<Vec<[String; 3]>, DbError> {
    host.execute(GRANT_ROWS)?
        .lines()
        .filter(|line| !line.trim().is_empty())
        .map(|line| {
            let mut fields = line.split('\t').map(undo_batch_escaping);
            match (fields.next(), fields.next(), fields.next()) {
                (Some(host), Some(database), Some(user)) => Ok([host, database, user]),
                _ => Err(DbError::Unparsable),
            }
        })
        .collect()
}

/// Undoes the escaping the client applies to a field in batch output.
///
/// It matters for exactly one character and would be invisible without it: a
/// `Db` stored as `alice\_shop` is PRINTED as `alice\\_shop`, so a reader that
/// took the output literally would see a name escaped twice, refuse it as
/// unfamiliar, and report every already-correct row as one it cannot classify.
pub(crate) fn undo_batch_escaping(field: &str) -> String {
    let mut undone = String::with_capacity(field.len());
    let mut characters = field.chars();
    while let Some(character) = characters.next() {
        if character != BATCH_ESCAPE {
            undone.push(character);
            continue;
        }

        match characters.next() {
            Some('n') => undone.push('\n'),
            Some('t') => undone.push('\t'),
            Some('0') => undone.push('\0'),
            Some(escaped) => undone.push(escaped),
            None => undone.push(BATCH_ESCAPE),
        }
    }

    undone
}
