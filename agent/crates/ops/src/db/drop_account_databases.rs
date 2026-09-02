//! Every database and database user one account owns, taken away together.

use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::system::name::AccountName;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::list_databases::list_databases;

/// The host the dedicated users were allowed to connect from.
///
/// The same `localhost` `create_database` grants and `drop_database` revokes: a
/// database user this panel made may connect from nowhere else, so the account
/// half of every name it holds is answered by asking about that host alone.
const USER_HOST: &str = "localhost";

/// The statement that asks the server for every local user it holds.
///
/// `Host` is compared against a literal in the statement rather than being
/// interpolated, because it is this crate's own constant and not a value any
/// caller can influence.
const SHOW_LOCAL_USERS: &str = "SELECT User FROM mysql.user WHERE Host = 'localhost'";

/// The character `for_account` puts between the account and the requested name.
///
/// Held here as well as in the validated types because this module performs the
/// inverse operation, and a decoder that guessed at the separator would decode
/// nothing on the day the two disagreed — silently, as an account whose
/// databases were all left behind.
const SEPARATOR: char = '_';

/// Drops every database and every database user that belongs to `account`.
///
/// # Why this is not a loop over [`drop_database`](crate::db::drop_database)
///
/// That operation takes a database AND the dedicated user it was created with,
/// because the customer names the two halves independently and there is nothing
/// to derive one from the other with. **That pairing exists only in the panel's
/// rows.** A cascade built out of it would therefore remove exactly the
/// databases the panel still remembers — and leave behind every database and
/// every live credential the panel has forgotten, which is precisely the set an
/// account deletion exists to clean up.
///
/// So the cascade asks the SERVER what is there. Databases come from
/// [`list_databases`], which decodes each name at its LAST separator and
/// matches the whole account, so `alice_bob_shop` is `alice_bob`'s and is not
/// touched when `alice` is deleted. Users are decoded the same way out of the
/// server's own list of local users. Nothing is derived from the other, and
/// nothing depends on the panel remembering.
///
/// # What that means for a re-created account of the same name
///
/// Nothing survives for it to inherit. This is the whole point of the
/// operation: system user names are recycled, so an account created again as
/// `alice` would otherwise find `alice_shop` still on the server with the
/// previous tenant's rows in it, and `alice_shop`'s credential still able to
/// reach them.
///
/// # Idempotency
///
/// An account with nothing on the server is success and sends no DDL at all,
/// which is what makes a retry after a lost response safe. `DROP USER` is
/// conditional for the same reason a drop is retried at all: a user removed by
/// a previous, interrupted attempt must not stop this one.
///
/// The databases go before the users, matching `drop_database`. The other order
/// leaves a database nothing can reach for the moment between the two, and
/// leaves it that way permanently if the process dies in between.
///
/// # Errors
///
/// - [`DbError::AccessDenied`] when the server refuses the agent's connection.
/// - [`DbError::Unparsable`] when a listing is longer than the agent will read.
/// - [`DbError::ClientFailed`] for any other refusal, on the first statement
///   that is refused. Everything before it is dropped and everything after it
///   is untouched, which is safe to retry.
pub fn drop_account_databases(host: &dyn DbHost, account: &AccountName) -> Result<(), DbError> {
    let databases = list_databases(host, account)?;
    let users = account_users(host, account)?;
    if databases.is_empty() && users.is_empty() {
        return Ok(());
    }

    for database in &databases {
        host.execute(&format!("DROP DATABASE `{}`", database.name.as_str()))?;
    }

    for user in &users {
        host.execute(&format!(
            "DROP USER IF EXISTS '{}'@'{USER_HOST}'",
            user.as_str()
        ))?;
    }

    Ok(())
}

/// Every local database user whose name decodes to `account`.
///
/// The decode is the one [`list_databases`] documents at length, applied to the
/// user namespace: split at the LAST separator, compare the WHOLE account, and
/// rebuild through `DbUserName::for_account` so that a name this function
/// reports is a name this agent could itself have created. A user an
/// administrator made by hand outside the convention decodes to nothing and is
/// left alone — as are `root`, `mysql.sys` and the server's own accounts, which
/// have no separator at all.
///
/// A prefix scan would be the wrong predicate here for exactly the reason it is
/// wrong for databases: `alice_` is a prefix of `alice_bob_deploy`, which is
/// account `alice_bob`'s credential, and dropping it would take another
/// tenant's application offline.
///
/// # Errors
///
/// Returns whatever the client failed with; see [`DbHost::execute`].
fn account_users(host: &dyn DbHost, account: &AccountName) -> Result<Vec<DbUserName>, DbError> {
    let mut owned: Vec<DbUserName> = host
        .execute(SHOW_LOCAL_USERS)?
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
        .filter_map(|name| decode_for_account(account, name))
        .collect();
    owned.sort_by(|left, right| left.as_str().cmp(right.as_str()));

    Ok(owned)
}

/// Decodes `name` and reports it only when it decodes to `account` in full.
fn decode_for_account(account: &AccountName, name: &str) -> Option<DbUserName> {
    let (owner, requested) = name.rsplit_once(SEPARATOR)?;
    if owner != account.as_str() {
        return None;
    }

    DbUserName::for_account(account, requested).ok()
}

#[cfg(test)]
#[path = "../tests/db/drop_account_databases_tests.rs"]
mod tests;
