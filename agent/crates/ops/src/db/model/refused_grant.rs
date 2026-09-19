//! One grant-table row the repair deliberately did not touch.

use crate::db::model::grant_repair_refusal::GrantRepairRefusal;

/// A row of the server's grant table that the repair left exactly as it found
/// it, and why.
///
/// The three name fields are plain `String`s rather than validated types, and
/// that is the point: a row lands here precisely because it is NOT a value this
/// agent could have produced, so there is no type for it. They are carried as
/// data for an operator to read — never back into a statement.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RefusedGrant {
    /// The `Host` column, exactly as the server holds it.
    pub host: String,

    /// The `Db` column, exactly as the server holds it — escapes included, so an
    /// operator sees the stored pattern and not a tidied rendering of it.
    pub database: String,

    /// The `User` column, exactly as the server holds it.
    pub user: String,

    /// Why the repair refused this row.
    pub reason: GrantRepairRefusal,
}
