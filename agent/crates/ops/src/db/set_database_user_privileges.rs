//! SetDatabaseUserPrivileges: what one login may do on one database.

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::grant_pattern::grant_pattern_for;
use crate::db::list_databases::database_exists;
use crate::db::model::database_privilege_change::DatabasePrivilegeChange;
use crate::db::user_database_grants::user_database_grants;
use crate::db::user_exists::user_exists;
use crate::db::user_host::USER_HOST;

/// Makes `change.user`'s privileges on `change.database` exactly
/// `change.privileges`.
///
/// # What is sent, and in which order
///
/// The login's current row for the database is read (`user_database_grants`), and
/// only the difference is sent:
///
/// 1. `GRANT <missing> ON <pattern>.*` when the wanted set holds something the row
///    lacks;
/// 2. `REVOKE <surplus> ON <pattern>.*` when the row holds something not wanted;
/// 3. `REVOKE GRANT OPTION ON <pattern>.*` when the row carries the grant option,
///    whatever was asked for;
/// 4. `REVOKE GRANT OPTION` and then `REVOKE ALL PRIVILEGES` on the UNESCAPED
///    spelling of the database, when an older agent left a wildcard row for the
///    same login. Two statements, because MariaDB refuses the combined
///    `REVOKE ALL PRIVILEGES, GRANT OPTION ON db.*` (error 1064).
///
/// Grants come before revokes, so an interruption leaves the login with too much
/// access and never with none, and a retry converges. This is the order
/// `repair_grants` uses, for the same reason. A login already holding exactly the
/// wanted set sends nothing beyond the read.
///
/// # Why the grant option is always revoked
///
/// No [`DatabasePrivilege`](maran_agent_core::validation::db::database_privilege::DatabasePrivilege)
/// stands for it, so this operation can never grant it; but a row may hold it
/// because somebody granted it by hand or an older agent did. A login holding it
/// can hand the database to a login the panel has no row for, so the panel's record
/// of what the login may do would be false. The revoke makes it true again.
///
/// # Why the pattern is escaped
///
/// A database-level `GRANT`'s name is a LIKE-style pattern, and every name here
/// holds `_` (`grant_pattern.rs` explains the reach). Every statement names the
/// database through `grant_pattern_for`, and the row compared against in the
/// read is the escaped spelling; the unescaped spelling is a row a pre-escape
/// agent wrote, which matches neighbouring databases, and step 4 removes it AFTER
/// the escaped grant is in place. A row under any other `Db` value is not this
/// database's and is neither read nor touched.
///
/// # What is NOT covered
///
/// A privilege outside the closed set that a row happens to hold (for example
/// `DELETE HISTORY` on MariaDB, which `ALL PRIVILEGES` includes) is not read and
/// not revoked. The threat note records it. Two overlapping calls for the same login
/// and database are not serialised.
///
/// # Idempotency
///
/// An empty set for a login that holds nothing is a no-op, not an error. A login or
/// a database that is not on the server is [`DbError::NotFound`] and nothing is
/// created.
///
/// # Errors
///
/// - [`DbError::NotFound`] when the login or the database is not on this server.
/// - [`DbError::AccessDenied`] when the server refuses the agent's connection.
/// - [`DbError::Unparsable`] when an answer is not in the shape this reads.
/// - [`DbError::ClientFailed`] when the server refuses a statement for any other
///   reason, carrying its error number and none of its output.
/// - [`DbError::Bound`] when the client ran past its deadline, so no answer was
///   read. A statement already sent may nevertheless take effect on the server.
pub fn set_database_user_privileges(
    host: &dyn DbHost,
    change: &DatabasePrivilegeChange,
) -> Result<(), DbError> {
    if !user_exists(host, &change.user)? {
        return Err(DbError::NotFound);
    }
    if !database_exists(host, &change.database)? {
        return Err(DbError::NotFound);
    }

    let escaped = grant_pattern_for(change.database.as_str());
    let grants = user_database_grants(host, &change.user)?;
    let current = grants.iter().find(|grant| grant.database == escaped);
    let held = current.map(|grant| grant.privileges).unwrap_or_default();
    let holds_grant_option = current.is_some_and(|grant| grant.grant_option);
    let has_wildcard_row = grants
        .iter()
        .any(|grant| grant.database == change.database.as_str() && grant.database != escaped);

    let user = change.user.as_str();
    let missing = change.privileges.difference(held);
    let surplus = held.difference(change.privileges);

    if !missing.is_empty() {
        host.execute(&format!(
            "GRANT {} ON `{escaped}`.* TO '{user}'@'{USER_HOST}'",
            missing.sql_list()
        ))?;
    }
    if !surplus.is_empty() {
        host.execute(&format!(
            "REVOKE {} ON `{escaped}`.* FROM '{user}'@'{USER_HOST}'",
            surplus.sql_list()
        ))?;
    }
    if holds_grant_option {
        host.execute(&format!(
            "REVOKE GRANT OPTION ON `{escaped}`.* FROM '{user}'@'{USER_HOST}'"
        ))?;
    }
    if has_wildcard_row {
        for privileges in ["GRANT OPTION", "ALL PRIVILEGES"] {
            host.execute(&format!(
                "REVOKE {privileges} ON `{}`.* FROM '{user}'@'{USER_HOST}'",
                change.database.as_str()
            ))?;
        }
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/db/set_database_user_privileges_tests.rs"]
mod tests;
