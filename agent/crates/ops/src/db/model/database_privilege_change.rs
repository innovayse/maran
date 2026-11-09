//! Everything `SetDatabaseUserPrivileges` needs, already validated.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::database_privilege_set::DatabasePrivilegeSet;
use maran_agent_core::validation::db::db_user_name::DbUserName;

/// One login, one database, and the complete set the login is to hold on it.
///
/// The user and the database are both built by `for_account`, so a change cannot
/// name another account's login or database. The set is the closed
/// [`DatabasePrivilegeSet`]: nothing in it can be `GRANT OPTION` or a global
/// privilege. An empty set is meaningful and means "hold nothing here".
#[derive(Debug, Clone)]
pub struct DatabasePrivilegeChange {
    /// The login whose privileges change.
    pub user: DbUserName,
    /// The database they change on.
    pub database: DatabaseName,
    /// What the login holds on that database afterwards.
    pub privileges: DatabasePrivilegeSet,
}
