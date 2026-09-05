//! ListDatabases: which of this server's databases decode to one account.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::system::name::AccountName;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::model::database_summary::DatabaseSummary;

/// The statement that asks the server for every database it holds.
const SHOW_DATABASES: &str = "SHOW DATABASES";

/// Lists the databases whose names decode to `account`.
///
/// **This is a diagnostic view, and it is not the tenant boundary.** What a
/// customer may see, drop or measure is decided by the panel's own rows, which
/// record which account each database was created for; this call exists so an
/// operator can reconcile those rows against what the server actually holds.
/// Nothing here should ever become the thing an authorisation decision is made
/// from — the server has no notion of a tenant, so any answer derived from its
/// names is derived from a naming convention rather than from a record of who
/// asked for what.
///
/// It still refuses to alias one account onto another, because a diagnostic
/// listing that shows a neighbour's names has already leaked them, whatever a
/// UI then does with the answer. The refusal is the decode, not a filter:
///
/// - A prefix scan is the wrong predicate. `starts_with("alice_")` matches
///   `alice_bob_shop`, which belongs to account `alice_bob` — account names may
///   contain the separator, so `alice_` is a prefix of infinitely many other
///   accounts' names.
/// - Splitting at the LAST separator is the right one, and it is exact rather
///   than a better heuristic. `for_account` forbids the separator in the
///   requested half, so a name has exactly one separator after the account's,
///   and the split at the last one recovers the two halves the name was built
///   from. `alice_bob_shop` decodes to account `alice_bob`, which is not
///   `alice`, and is dropped.
///
/// Neither predicate is written here. [`DatabaseName::decode`] is the inverse of
/// the constructor that built the name and lives beside it, so the split can
/// never use a character the join did not; this call only keeps what it
/// returns. A row reported here is therefore a row whose name this agent could
/// itself have created. A database an administrator made by hand under a name
/// outside the convention decodes to nothing and is not listed — as are the
/// server's own `mysql` and `information_schema`, which have no separator at
/// all.
///
/// The result is sorted by name so that two calls against an unchanged server
/// give the same answer in the same order, whatever order the server printed.
///
/// # Errors
///
/// - [`DbError::AccessDenied`] when the server refuses the agent's connection.
/// - [`DbError::Unparsable`] when the listing is longer than the agent will
///   read.
/// - [`DbError::ClientFailed`] for any other refusal by the client.
pub fn list_databases(
    host: &dyn DbHost,
    account: &AccountName,
) -> Result<Vec<DatabaseSummary>, DbError> {
    let mut owned: Vec<DatabaseSummary> = server_databases(host)?
        .iter()
        .filter_map(|name| decode_for_account(account, name))
        .collect();
    owned.sort_by(|left, right| left.name.as_str().cmp(right.name.as_str()));

    Ok(owned)
}

/// Every database name the server printed, one per line, blanks dropped.
///
/// # Errors
///
/// Returns whatever the client failed with; see [`DbHost::execute`].
fn server_databases(host: &dyn DbHost) -> Result<Vec<String>, DbError> {
    Ok(host
        .execute(SHOW_DATABASES)?
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
        .map(str::to_owned)
        .collect())
}

/// Wraps [`DatabaseName::decode`]'s answer as the summary the listing returns.
///
/// The decode itself belongs to the type — see the note on [`list_databases`]
/// for why the whole account is compared and not a prefix of it.
fn decode_for_account(account: &AccountName, name: &str) -> Option<DatabaseSummary> {
    DatabaseName::decode(account, name).map(|database| DatabaseSummary { name: database })
}

/// Whether the server holds `name`.
///
/// The same listing [`list_databases`] decides from, asked about one name — so a
/// database can never be reported as present by one path and absent by another.
/// The comparison is against the full name, which is the only form a
/// [`DatabaseName`] has.
///
/// # Errors
///
/// Returns whatever the client failed with; see [`DbHost::execute`].
pub(crate) fn database_exists(host: &dyn DbHost, name: &DatabaseName) -> Result<bool, DbError> {
    Ok(server_databases(host)?
        .iter()
        .any(|existing| existing == name.as_str()))
}

#[cfg(test)]
#[path = "../tests/db/list_databases_tests.rs"]
mod tests;
