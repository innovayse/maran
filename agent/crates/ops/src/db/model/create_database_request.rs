//! Everything `CreateDatabase` needs, already validated.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;

/// The database, the user that will own it, and the user's password.
///
/// Every field is a validated type and none of them is a `String`. That is the
/// area's entire injection defence, so it is worth saying plainly what each one
/// buys:
///
/// - [`DatabaseName`] and [`DbUserName`] can only be built by `for_account`,
///   which applies the `<account>_<name>` prefix and restricts the requested
///   half to `[a-z0-9]`. There is no constructor that produces an unprefixed
///   name, so a fully-qualified name off the wire cannot arrive here at all —
///   the service rebuilds the name from the account the panel authorised.
/// - [`Password`] can only hold letters, digits and `-_.=+`, and prints itself
///   as `<password>`, so the `#[derive(Debug)]` on this struct is safe to reach
///   a tracing field.
///
/// The three of them are what makes the statements in
/// `create_database` safe to interpolate. Read
/// that file's note before changing a field's type back to a string.
#[derive(Debug, Clone)]
pub struct CreateDatabaseRequest {
    /// The database to create, prefixed with its owning account.
    pub database: DatabaseName,
    /// The user that will own it, prefixed with the same account.
    pub user: DbUserName,
    /// The password the user is created with.
    ///
    /// Supplied by the caller and never generated here (`db.proto`): the panel
    /// is the single place a password is minted and stored, so the agent has
    /// nothing to keep and nothing to leak.
    pub password: Password,
}
