//! CreateDatabaseUser: a further login for an account, holding nothing yet.

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::model::database_user_request::DatabaseUserRequest;
use crate::db::user_exists::user_exists;
use crate::db::user_host::USER_HOST;

/// Creates `request`'s login with its password and no privilege at all.
///
/// A new login can connect from `localhost` only and reaches no database until
/// `set_database_user_privileges` grants it one. Creating it with a grant would
/// hide, inside one statement pair, the decision which databases it may touch.
///
/// # How the statement is built
///
/// `CREATE USER … IDENTIFIED BY '<password>'` is DDL and takes no placeholders, so
/// both values are interpolated. The protection is the validated types, not
/// escaping: a [`DbUserName`](maran_agent_core::validation::db::db_user_name::DbUserName)
/// holds only `[a-z0-9_]` and a
/// [`Password`](maran_agent_core::validation::secrets::password::Password) only
/// letters, digits and `-_.=+`, so neither can hold anything an interpolation could
/// break out with. `create_database` sets the argument out in full; read it before
/// widening either alphabet.
///
/// # Idempotency
///
/// A login that is already on the server is [`DbError::AlreadyExists`] and its
/// password is left alone, so a retried creation cannot reset a credential the
/// customer was already shown. The statement is a plain `CREATE USER`, not the
/// conditional form: the conditional form reports success for a login that was
/// there, and the caller must be able to tell the two apart. Losing the race to
/// another writer between the check and the create is answered the same way,
/// because the server's own "user exists" number maps to the same variant.
///
/// # Errors
///
/// - [`DbError::AlreadyExists`] when the login is already on this server.
/// - [`DbError::AccessDenied`] when the server refuses the agent's connection.
/// - [`DbError::Unparsable`] when the existence query answers with something that
///   is not a number.
/// - [`DbError::ClientFailed`] when the server refuses a statement for any other
///   reason, carrying its error number and none of its output.
/// - [`DbError::Bound`] when the client ran past its deadline, so no answer was
///   read. A statement already sent may nevertheless take effect on the server.
pub fn create_database_user(
    host: &dyn DbHost,
    request: &DatabaseUserRequest,
) -> Result<(), DbError> {
    if user_exists(host, &request.user)? {
        return Err(DbError::AlreadyExists);
    }

    host.execute(&format!(
        "CREATE USER '{}'@'{USER_HOST}' IDENTIFIED BY '{}'",
        request.user.as_str(),
        request.password.as_str()
    ))?;

    Ok(())
}

#[cfg(test)]
#[path = "../tests/db/create_database_user_tests.rs"]
mod tests;
