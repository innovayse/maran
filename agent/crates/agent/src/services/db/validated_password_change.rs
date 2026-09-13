//! Turning a `SetDatabasePassword` request into the values the operation takes.

use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Builds the user and the password `SetDatabasePassword` re-credentials with.
///
/// One bundle per request shape rather than two checks chained in the handler,
/// so the handler stays the three steps and nothing else (rules/rust.md
/// "Service anatomy"). The account is dropped here on purpose: setting a
/// password touches no database and nothing else the account owns, so there is
/// nothing left for the operation to want it for, and passing it on would
/// suggest otherwise.
///
/// The database name is deliberately NOT part of this request. A password
/// belongs to a MySQL user, and a user may be granted on a database whose name
/// the customer chose independently (`db.proto`), so asking for a database here
/// would either be an unused field or a second thing to get wrong.
///
/// The two checks are what make this rpc safe to expose at all. The user name is
/// REBUILT from the account rather than taken off the wire — `DbUserName` has no
/// constructor that accepts a whole name — so a request naming another tenant's
/// user produces a name under the caller's own account instead. And the password
/// cannot hold a quote, a backslash or a backtick, so it cannot break out of the
/// `IDENTIFIED BY '<value>'` literal the operation interpolates it into. The
/// prize for getting past either of these is a working credential on somebody
/// else's data, which is why both are types rather than checks.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, for a
/// user suffix that is empty or outside `[a-z0-9]`, for a prefixed user past the
/// server's thirty-two byte user-name limit, or for a password outside the
/// allowed alphabet. An empty password is refused by that last check rather than
/// treated as "leave it unchanged" (`db.proto`): a silent no-op would report
/// success for a credential that was never rotated, and the customer would be
/// shown a password that does not work.
pub fn validated_password_change(
    account_username: &str,
    db_username: &str,
    password: &str,
) -> Result<(DbUserName, Password), AgentError> {
    let account = validated_account(account_username)?;

    let user = DbUserName::for_account(&account, db_username)
        .map_err(|error| invalid_input(error.to_string()))?;
    let password = Password::parse(password).map_err(|error| invalid_input(error.to_string()))?;

    Ok((user, password))
}

#[cfg(test)]
#[path = "../../tests/services/db/validated_password_change_tests.rs"]
mod tests;
