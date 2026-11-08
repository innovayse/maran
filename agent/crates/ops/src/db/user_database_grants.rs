//! Reading what one login holds on databases, from `mysql.db`.

use maran_agent_core::validation::db::database_privilege::DatabasePrivilege;
use maran_agent_core::validation::db::database_privilege_set::DatabasePrivilegeSet;
use maran_agent_core::validation::db::db_user_name::DbUserName;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::grant_rows::undo_batch_escaping;
use crate::db::privilege_column::{GRANT_OPTION_COLUMN, privilege_column};
use crate::db::user_host::USER_HOST;

/// What a login holds on one database, as `mysql.db` stores it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct UserDatabaseGrant {
    /// The `Db` column exactly as the server stores it, escapes included.
    pub(crate) database: String,
    /// Whether the row carries the grant option.
    pub(crate) grant_option: bool,
    /// The privileges of the closed set the row holds. A privilege the row holds
    /// that is outside the set is not read and so not reported.
    pub(crate) privileges: DatabasePrivilegeSet,
}

/// One flag column as the client prints it: `Y` or `N`.
fn flag(field: &str) -> Result<bool, DbError> {
    match field {
        "Y" => Ok(true),
        "N" => Ok(false),
        _ => Err(DbError::Unparsable),
    }
}

/// The statement that asks for `user`'s database-level rows.
///
/// The user is a validated type and the host a constant; the column names come from
/// [`privilege_column`], so nothing a caller typed is in it.
fn read_statement(user: &DbUserName) -> String {
    let columns: Vec<&str> = DatabasePrivilege::ALL
        .into_iter()
        .map(privilege_column)
        .collect();

    format!(
        "SELECT Db, {GRANT_OPTION_COLUMN}, {} FROM mysql.db WHERE User = '{}' AND Host = '{USER_HOST}'",
        columns.join(", "),
        user.as_str()
    )
}

/// Every database-level row `user` holds at [`USER_HOST`].
///
/// # Errors
///
/// - [`DbError::Unparsable`] when a row does not have the fields asked for, or a
///   flag is neither `Y` nor `N`.
/// - Otherwise whatever the client failed with; see [`DbHost::execute`].
pub(crate) fn user_database_grants(
    host: &dyn DbHost,
    user: &DbUserName,
) -> Result<Vec<UserDatabaseGrant>, DbError> {
    host.execute(&read_statement(user))?
        .lines()
        .filter(|line| !line.trim().is_empty())
        .map(|line| {
            let fields: Vec<String> = line.split('\t').map(undo_batch_escaping).collect();
            if fields.len() != DatabasePrivilege::ALL.len() + 2 {
                return Err(DbError::Unparsable);
            }

            let mut granted: Vec<DatabasePrivilege> = Vec::new();
            for (privilege, field) in DatabasePrivilege::ALL.into_iter().zip(&fields[2..]) {
                if flag(field)? {
                    granted.push(privilege);
                }
            }

            Ok(UserDatabaseGrant {
                database: fields[0].clone(),
                grant_option: flag(&fields[1])?,
                privileges: DatabasePrivilegeSet::from_all_where(|privilege| {
                    granted.contains(&privilege)
                }),
            })
        })
        .collect()
}
