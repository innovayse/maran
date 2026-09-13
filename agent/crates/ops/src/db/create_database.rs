//! CreateDatabase: the database, its dedicated user, and that user's grant.

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::list_databases::database_exists;
use crate::db::model::create_database_request::CreateDatabaseRequest;

/// The character set every database this agent creates is given.
///
/// Chosen once, here, rather than taken from a caller: it is the only encoding
/// that stores the whole of Unicode in this server family, and the older
/// three-byte `utf8` it would otherwise inherit from the server's configuration
/// silently truncates rows at the first four-byte character.
const CHARACTER_SET: &str = "utf8mb4";

/// The collation that goes with [`CHARACTER_SET`].
const COLLATION: &str = "utf8mb4_unicode_ci";

/// The host the dedicated user may connect from.
///
/// Local only. A database user that may connect from anywhere is a database
/// user that can be brute-forced from anywhere, and nothing this panel hosts
/// reaches the database over the network.
const USER_HOST: &str = "localhost";

/// The one character a database-level `GRANT` reads as a wildcard that a
/// [`DatabaseName`](maran_agent_core::validation::db::database_name::DatabaseName)
/// can actually hold.
///
/// `_` — matching any single character — and it is in every name this agent
/// grants on, because it is the separator between an account and the suffix its
/// customer asked for, and because an account name may contain it too.
const GRANT_SINGLE_CHARACTER_WILDCARD: char = '_';

/// What that character has to be written as to mean itself.
const ESCAPED_SINGLE_CHARACTER_WILDCARD: &str = "\\_";

/// Creates `request`'s database and the user that owns it.
///
/// # How these statements are built, and why interpolation is correct here
///
/// The server's DDL cannot parameterise an identifier, and it cannot
/// parameterise the literal in `IDENTIFIED BY` either, so there is no
/// placeholder to bind and the values are interpolated. **The protection is the
/// validated type, not escaping.** A [`DatabaseName`](maran_agent_core::validation::db::database_name::DatabaseName)
/// and a [`DbUserName`](maran_agent_core::validation::db::db_user_name::DbUserName)
/// hold only `[a-z0-9_]`, and a
/// [`Password`](maran_agent_core::validation::secrets::password::Password) holds
/// only letters, digits and `-_.=+`. None of the three can hold a backtick, a
/// quote, a backslash, a semicolon, a space or a newline, so interpolating them
/// into ``CREATE DATABASE `name` `` and
/// `CREATE USER 'user'@'localhost' IDENTIFIED BY '<password>'` **cannot**
/// inject: there is nothing in any of them for an interpolation to break out
/// with.
///
/// **Injection is not the only axis, and the alphabet answers only that one.**
/// The `GRANT` below takes its database name as a LIKE-style PATTERN rather than
/// as an identifier, where the `_` that every one of these names carries matches
/// any single character — so a name that cannot break out of its quotes can still
/// name databases it does not own. That is `grant_pattern_for`'s job below, and it is
/// separate from this paragraph's on purpose: one is about what a value can do to
/// the statement, the other about what the statement does with a well-formed
/// value.
///
/// That sentence is here because the next reader will see interpolation next to
/// SQL and reach for a fix. The fix that suggests itself — accept a wider
/// alphabet and escape it on the way in — removes the control and replaces it
/// with an escaping routine nobody has reviewed. If a value needs a character
/// these types refuse, the question to answer is whether the value may hold it
/// at all, in the validated type, where the answer is written once.
///
/// # Idempotency
///
/// A repeat is reported as [`DbError::AlreadyExists`] and changes nothing —
/// notably not the existing user's password (`db.proto`). Retrying a create that
/// timed out is therefore safe, which is the whole point: the caller cannot tell
/// a lost response from a lost request, and the second attempt must not reset
/// the credential the customer was already shown.
///
/// The existence check is a listing rather than a `CREATE DATABASE IF NOT
/// EXISTS`, because the conditional form reports success for a database that
/// was already there and the caller must be able to tell the two apart. Losing
/// the race against another writer between the check and the create is still
/// answered correctly: the server refuses with its own "database exists" number,
/// which `DbError::from_client` maps to the same
/// [`DbError::AlreadyExists`].
///
/// The user is created with `IF NOT EXISTS`, which is the opposite choice for
/// the opposite reason: a user left behind by an interrupted drop must not stop
/// a create, and the conditional form leaves that user's password alone.
///
/// # Errors
///
/// - [`DbError::AlreadyExists`] when the database is already on this server.
/// - [`DbError::AccessDenied`] when the server refuses the agent's connection.
/// - [`DbError::ClientFailed`] when the server refuses a statement for any other
///   reason, carrying its error number and none of its output.
pub fn create_database(host: &dyn DbHost, request: &CreateDatabaseRequest) -> Result<(), DbError> {
    if database_exists(host, &request.database)? {
        return Err(DbError::AlreadyExists);
    }

    host.execute(&format!(
        "CREATE DATABASE `{}` CHARACTER SET {CHARACTER_SET} COLLATE {COLLATION}",
        request.database.as_str()
    ))?;

    host.execute(&format!(
        "CREATE USER IF NOT EXISTS '{}'@'{USER_HOST}' IDENTIFIED BY '{}'",
        request.user.as_str(),
        request.password.as_str()
    ))?;

    // Scoped to this one database, never `ON *.*`: the user exists to serve one
    // customer's application, and a server-wide grant would let it read every
    // other tenant's data on the host. The name is escaped, not merely quoted —
    // see `grant_pattern_for`, which is where the scoping actually happens.
    host.execute(&format!(
        "GRANT ALL PRIVILEGES ON `{}`.* TO '{}'@'{USER_HOST}'",
        grant_pattern_for(request.database.as_str()),
        request.user.as_str()
    ))?;

    Ok(())
}

/// Writes `database` as a `GRANT` pattern that matches that database and nothing
/// else.
///
/// **The database name of a database-level `GRANT` is a pattern, not an
/// identifier**, and this is the only place in this agent where that is true. In
/// MySQL and MariaDB `_` matches any single character there and `%` any
/// sequence; backtick-quoting does not turn it off, which is why the server's own
/// manual tells you to escape the character instead — the escaped form is what
/// stops the grantee "being able to access additional databases matching the
/// wildcard pattern".
///
/// It is not a theoretical difference here, because every name this agent grants
/// on holds the character. A name is `<account>_<suffix>`, so it carries the
/// separator at least; and an account name may itself contain underscores, which
/// is what turns one grant into a claim on other accounts' databases. Account
/// `h_stco` asking for `main` produces `h_stco_main`, and that name unescaped is
/// the pattern `h?stco?main`, which matches account `hostco`'s `hostco_main`
/// exactly. Patterns are stored in `mysql.db` and matched when a connection asks
/// for a database, so the reach extends to databases created after the grant.
///
/// `%` is deliberately NOT escaped, and that is a statement about the alphabet
/// rather than an oversight: a `DatabaseName` is built only by
/// `DatabaseName::for_account`, whose account half is `[a-z0-9_]` and whose
/// requested half is `[a-z0-9]`, so `%` cannot be in one. An escape for a
/// character that cannot arrive is a defensive call that cannot fail, which
/// rules/testing.md says to delete rather than label — so this comment carries
/// the reason instead, and the day that alphabet widens, this function is where
/// the second character goes.
///
/// The escape belongs here and NOT in `DatabaseName`: `CREATE DATABASE` and
/// `DROP DATABASE` take the name as an identifier, where a backslash is part of
/// the name, so escaping upstream would create a database that is actually called
/// `h\_stco\_main`.
fn grant_pattern_for(database: &str) -> String {
    database.replace(
        GRANT_SINGLE_CHARACTER_WILDCARD,
        ESCAPED_SINGLE_CHARACTER_WILDCARD,
    )
}

#[cfg(test)]
#[path = "../tests/db/create_database_tests.rs"]
mod tests;
