//! GetDatabaseSize: what one database occupies, as the server accounts for it.

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::list_databases::database_exists;
use crate::db::model::database_size_report::DatabaseSizeReport;

/// Measures `name`.
///
/// `name` is a [`DatabaseName`] for the same reason `drop_database`'s is: the
/// type has no constructor that takes a whole name, so a service cannot forward
/// one off the wire and must rebuild it from the account the panel authorised. A
/// size is a smaller prize than a drop, but it is still an answer about another
/// tenant's data, and the boundary is one rule rather than one rule per
/// operation.
///
/// The name is interpolated into the statement's `WHERE` clause. The protection
/// is the validated type and not escaping: a `DatabaseName` holds only
/// `[a-z0-9_]`, so there is no quote in it to close the literal with.
///
/// The figure comes from the server's own table metadata — data plus indexes,
/// summed over the database's tables — rather than from the size of its
/// directory on disk. The directory is what the storage engine has claimed,
/// which stays claimed after rows are deleted; the metadata figure is what every
/// other tool shows the customer, and a panel that disagreed with all of them
/// would be reporting a bug rather than a size.
///
/// A database with no tables sums to nothing at all, which is why the sum is
/// wrapped: without that, an empty database reads back as "not a number" and is
/// refused rather than reported as empty.
///
/// # Errors
///
/// - [`DbError::NotFound`] when the database is not on this server. Checked
///   first and separately, because the sum answers `0` for a database that does
///   not exist exactly as it does for one that is empty, and a caller must not
///   be told a missing database is an empty one.
/// - [`DbError::Unparsable`] when the server's answer is not a number.
/// - [`DbError::AccessDenied`] when the server refuses the agent's connection,
///   and [`DbError::ClientFailed`] for any other refusal.
pub fn database_size(
    host: &dyn DbHost,
    name: &DatabaseName,
) -> Result<DatabaseSizeReport, DbError> {
    if !database_exists(host, name)? {
        return Err(DbError::NotFound);
    }

    let printed = host.execute(&format!(
        "SELECT COALESCE(SUM(data_length + index_length), 0) \
         FROM information_schema.tables WHERE table_schema = '{}'",
        name.as_str()
    ))?;

    let bytes = printed.trim().parse().map_err(|_| DbError::Unparsable)?;

    Ok(DatabaseSizeReport { bytes })
}

#[cfg(test)]
#[path = "../tests/db/database_size_tests.rs"]
mod tests;
