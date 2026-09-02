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
    // other tenant's data on the host.
    host.execute(&format!(
        "GRANT ALL PRIVILEGES ON `{}`.* TO '{}'@'{USER_HOST}'",
        request.database.as_str(),
        request.user.as_str()
    ))?;

    Ok(())
}

#[cfg(test)]
#[path = "../tests/db/create_database_tests.rs"]
mod tests;
