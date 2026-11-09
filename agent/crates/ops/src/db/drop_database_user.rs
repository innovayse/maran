//! DropDatabaseUser: a login and every privilege it holds.

use maran_agent_core::validation::db::db_user_name::DbUserName;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::user_exists::user_exists;
use crate::db::user_host::USER_HOST;

/// Drops `user`.
///
/// `DROP USER` removes the login's rows from every grant table, so no privilege
/// it held on any database survives it. That is why nothing is revoked first.
///
/// The agent does not know which login is a database's dedicated one and does not
/// decide whether a login may be dropped on its own: that rule belongs to the
/// panel, which holds the rows (`db.proto`). `drop_database` drops the dedicated
/// login together with its database; this operation drops any other.
///
/// # Idempotency
///
/// A login that is not on the server is [`DbError::NotFound`] and nothing is sent
/// beyond the existence check. The statement is a plain `DROP USER`, so losing the
/// race to another writer is answered by the server's own refusal rather than by a
/// silent success.
///
/// # Errors
///
/// - [`DbError::NotFound`] when the login is not on this server.
/// - [`DbError::AccessDenied`] when the server refuses the agent's connection.
/// - [`DbError::Unparsable`] when the existence query answers with something that
///   is not a number.
/// - [`DbError::ClientFailed`] when the server refuses a statement for any other
///   reason, carrying its error number and none of its output.
/// - [`DbError::Bound`] when the client ran past its deadline, so no answer was
///   read. A statement already sent may nevertheless take effect on the server.
pub fn drop_database_user(host: &dyn DbHost, user: &DbUserName) -> Result<(), DbError> {
    if !user_exists(host, user)? {
        return Err(DbError::NotFound);
    }

    host.execute(&format!("DROP USER '{}'@'{USER_HOST}'", user.as_str()))?;

    Ok(())
}

#[cfg(test)]
#[path = "../tests/db/drop_database_user_tests.rs"]
mod tests;
