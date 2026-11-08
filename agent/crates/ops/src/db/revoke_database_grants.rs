//! Taking every grant on one database away before the database goes.

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::grant_pattern::grant_pattern_for;
use crate::db::grant_rows::grant_rows;

/// Whether `character` may appear in a user name read back from the server.
///
/// The alphabet of [`DbUserName`](maran_agent_core::validation::db::db_user_name::DbUserName)
/// plus `.` and `-`, which a user made by hand may hold. Nothing here is a quote,
/// a backtick, a backslash, a semicolon or whitespace.
fn is_safe_user_character(character: char) -> bool {
    character.is_ascii_alphanumeric() || matches!(character, '_' | '.' | '-')
}

/// Whether `character` may appear in a host read back from the server.
///
/// A host may be a name, an address or a pattern, so `%`, `:` and `.` are in.
fn is_safe_host_character(character: char) -> bool {
    character.is_ascii_alphanumeric() || matches!(character, '_' | '.' | '-' | ':' | '%')
}

/// Whether `value` is non-empty and made only of characters `allowed` accepts.
fn is_safe(value: &str, allowed: fn(char) -> bool) -> bool {
    !value.is_empty() && value.chars().all(allowed)
}

/// Revokes every privilege any user holds on `name`, whoever the user is.
///
/// # Why this exists
///
/// `DROP DATABASE` does not touch the server's grant table: a database-level
/// grant is stored under the database's name, so a database created later under
/// the same name is reached by every grant still stored for it. `drop_database`
/// removes the dedicated user the caller names, which covers the grant this panel
/// issued — but the user is named independently of the database, a drop that
/// stops between the two statements leaves the user (and `create_database`
/// reuses an existing user), and a grant held by anyone else is never looked at.
/// Reading the table and revoking what is there closes all three, whoever the
/// grantee is.
///
/// # What is matched
///
/// Rows whose `Db` column is, literally, this database's escaped grant pattern or
/// its unescaped spelling (written by agents before the escape existed). A
/// wildcard row such as `alice%` is not this database's grant and is left alone.
///
/// # Why the user and host are checked
///
/// They come from the server's stored data and not from a validated type, and the
/// statement interpolates them. A row whose user or host holds a character
/// outside a small allow-list is answered with [`DbError::Unparsable`] and
/// nothing is revoked or dropped: skipping it would silently keep a grant, which
/// is the defect this function removes.
///
/// # Idempotency
///
/// A second call finds no rows and sends no statement beyond the read.
///
/// # Errors
///
/// - [`DbError::Unparsable`] when the grant table cannot be read, or a matching
///   row holds a user or host outside the allow-list.
/// - Otherwise whatever the client failed with; see [`DbHost::execute`].
pub(crate) fn revoke_database_grants(
    host: &dyn DbHost,
    name: &DatabaseName,
) -> Result<(), DbError> {
    let escaped = grant_pattern_for(name.as_str());
    let mut revocations: Vec<String> = Vec::new();

    for [grant_host, database, user] in grant_rows(host)? {
        if database != escaped && database != name.as_str() {
            continue;
        }

        if !is_safe(&user, is_safe_user_character) || !is_safe(&grant_host, is_safe_host_character)
        {
            return Err(DbError::Unparsable);
        }

        let statement = format!(
            "REVOKE ALL PRIVILEGES, GRANT OPTION ON `{database}`.* FROM '{user}'@'{grant_host}'"
        );
        if !revocations.contains(&statement) {
            revocations.push(statement);
        }
    }

    for statement in revocations {
        host.execute(&statement)?;
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/db/revoke_database_grants_tests.rs"]
mod tests;
