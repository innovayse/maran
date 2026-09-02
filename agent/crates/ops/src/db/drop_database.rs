//! DropDatabase: the database and its dedicated user, together.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::list_databases::database_exists;

/// The host the dedicated user was allowed to connect from.
const USER_HOST: &str = "localhost";

/// Drops `name` and the user `user` that was created with it.
///
/// # Why the parameters are validated types
///
/// `name` is a [`DatabaseName`], which has no constructor that takes a whole
/// name: the only way to obtain one is `for_account`, which builds it from an
/// account and a `[a-z0-9]` suffix. A service therefore cannot pass along the
/// fully-qualified name that arrived on the wire — it rebuilds the name from the
/// account the panel authorised, and a request naming another tenant's database
/// produces a name for the caller's own account instead of that tenant's. A
/// `&str` parameter here would make that mistake invisible and unreviewable,
/// which is exactly how a drop of somebody else's database gets written.
///
/// The same reasoning applies to `user`. The two are separate parameters rather
/// than one pair because a database and its user are named independently by the
/// customer (`db.proto`), so there is nothing to derive one from the other with.
///
/// The statements interpolate both, for the reason `create_database` sets out at
/// length: the server's DDL takes no placeholders, and the protection is the
/// alphabet of the validated type rather than any escaping.
///
/// # Idempotency
///
/// A second drop reports [`DbError::NotFound`] and changes nothing, which is the
/// contract's own answer (`db.proto`). The user is removed with `IF EXISTS`, so
/// a database whose user was already gone still drops cleanly instead of
/// stranding the database behind a failure.
///
/// The database is dropped before the user, deliberately. The other order leaves
/// a database nothing can reach for the moment between the two statements, and
/// leaves it that way permanently if the process dies in between — a customer's
/// data present, inaccessible, and invisible to a retry that would have removed
/// it.
///
/// # Errors
///
/// - [`DbError::NotFound`] when the database is not on this server.
/// - [`DbError::AccessDenied`] when the server refuses the agent's connection.
/// - [`DbError::ClientFailed`] when the server refuses a statement for any other
///   reason.
pub fn drop_database(
    host: &dyn DbHost,
    name: &DatabaseName,
    user: &DbUserName,
) -> Result<(), DbError> {
    if !database_exists(host, name)? {
        return Err(DbError::NotFound);
    }

    host.execute(&format!("DROP DATABASE `{}`", name.as_str()))?;
    host.execute(&format!(
        "DROP USER IF EXISTS '{}'@'{USER_HOST}'",
        user.as_str()
    ))?;

    Ok(())
}

#[cfg(test)]
#[path = "../tests/db/drop_database_tests.rs"]
mod tests;
