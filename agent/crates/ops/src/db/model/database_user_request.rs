//! Everything `CreateDatabaseUser` needs, already validated.

use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;

/// The login to create and the password it is created with.
///
/// Both fields are validated types for the reason `CreateDatabaseRequest` gives:
/// the statement interpolates them, and what makes that safe is that neither can
/// hold a quote, a backtick, a backslash, a semicolon, a space or a newline.
/// There is no database here: a new login holds nothing until
/// `set_database_user_privileges` grants it something.
#[derive(Debug, Clone)]
pub struct DatabaseUserRequest {
    /// The login to create, prefixed with its owning account.
    pub user: DbUserName,
    /// The password it is created with. Supplied by the caller and never
    /// generated here (`db.proto`); prints itself as `<password>`.
    pub password: Password,
}
