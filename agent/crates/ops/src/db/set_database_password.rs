//! SetDatabasePassword: a new credential for a user that already exists.

use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;

/// The host the dedicated user is allowed to connect from.
///
/// The same `localhost` `create_database` grants, restated here rather than
/// shared, because the two statements must name the same account or this one
/// would silently mint a SECOND user with the same name and a different host —
/// leaving the customer's application authenticating against the old password
/// while the panel reported the change as done.
const USER_HOST: &str = "localhost";

/// Sets `user`'s password to `password`.
///
/// # Why this operation exists at all
///
/// Nobody in this system keeps a copy of a database password. The panel mints
/// one, shows it once and forgets it; the agent passes it on and forgets it; the
/// server's own hash is the only copy (`db.proto`). `create_database` is
/// deliberately idempotent in the way that makes a retried creation safe — a
/// pair that already exists is reported as
/// [`DbError::AlreadyExists`](crate::db::DbError::AlreadyExists) and its
/// password is left alone — which leaves this operation as the whole of the
/// recovery path for a customer who lost theirs.
///
/// # Why the parameters are validated types
///
/// `user` is a [`DbUserName`], which has no constructor taking a whole name: the
/// only way to obtain one is `for_account`. A service therefore cannot forward
/// the name that arrived on the wire and must rebuild it from the account the
/// panel authorised, so a request naming another tenant's user produces a name
/// under the CALLER's own account instead. That is this operation's tenant
/// boundary, and it is a type rather than a check — which matters more here than
/// almost anywhere else in the crate, because the prize for getting past it is a
/// working credential on somebody else's database rather than a read.
///
/// `password` is a [`Password`], whose alphabet excludes the quote, the
/// backslash and the backtick. `ALTER USER … IDENTIFIED BY '<value>'` is DDL and
/// takes no placeholders, so the value is interpolated; what makes that safe is
/// that there is nothing in a `Password` for an interpolation to break out with.
/// The values are validated, not escaped — see `create_database` for the full
/// argument, and read it before widening either alphabet.
///
/// # Idempotency
///
/// Setting a password twice leaves the user with the second one, which is the
/// only sense in which a password change can converge. A user that is not on
/// this server is [`DbError::NotFound`] and is deliberately NOT created: minting
/// a credential here would produce a live login for a database the panel has no
/// row for, which is the orphan the create path takes trouble to avoid.
///
/// The existence check is separate from the `ALTER` rather than folded into it,
/// because MySQL reports a failed `ALTER USER` with a generic operation-failed
/// number that carries the same meaning as several unrelated refusals, and a
/// caller told "not found" for a server that is merely refusing the agent would
/// go and recreate a database that is still there.
///
/// # Errors
///
/// - [`DbError::NotFound`] when no such user is on this server.
/// - [`DbError::AccessDenied`] when the server refuses the agent's connection.
/// - [`DbError::Unparsable`] when the existence query answers with something
///   that is not a number.
/// - [`DbError::ClientFailed`] when the server refuses a statement for any other
///   reason, carrying its error number and none of its output.
pub fn set_database_password(
    host: &dyn DbHost,
    user: &DbUserName,
    password: &Password,
) -> Result<(), DbError> {
    if !user_exists(host, user)? {
        return Err(DbError::NotFound);
    }

    host.execute(&format!(
        "ALTER USER '{}'@'{USER_HOST}' IDENTIFIED BY '{}'",
        user.as_str(),
        password.as_str()
    ))?;

    Ok(())
}

/// Whether the server holds `user` at [`USER_HOST`].
///
/// Asked of `mysql.user` rather than of a grant listing, because a grant tells
/// which databases a user may reach and this question is only whether the login
/// is there at all — a user whose grant was revoked is still a user whose
/// password can be set.
///
/// The host is part of the question. `'shop'@'localhost'` and `'shop'@'%'` are
/// two different logins to MySQL, and answering "yes" off the name alone would
/// let this operation report success after altering nothing.
///
/// # Errors
///
/// - [`DbError::Unparsable`] when the count is not a number.
/// - Otherwise whatever the client failed with; see [`DbHost::execute`].
fn user_exists(host: &dyn DbHost, user: &DbUserName) -> Result<bool, DbError> {
    let printed = host.execute(&format!(
        "SELECT COUNT(*) FROM mysql.user WHERE user = '{}' AND host = '{USER_HOST}'",
        user.as_str()
    ))?;

    let count: u64 = printed.trim().parse().map_err(|_| DbError::Unparsable)?;

    Ok(count > 0)
}

#[cfg(test)]
#[path = "../tests/db/set_database_password_tests.rs"]
mod tests;
