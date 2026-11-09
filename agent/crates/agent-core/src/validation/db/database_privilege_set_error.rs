//! Why a set of database privileges was refused.

use crate::validation::db::database_privilege::DatabasePrivilege;

/// Rejection reasons for [`super::database_privilege_set::DatabasePrivilegeSet::of`].
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DatabasePrivilegeSetError {
    /// More privileges were given than the closed set holds, which means at least
    /// one is repeated. Refused before any entry is read, so the work is bounded
    /// by the set and not by the request.
    #[error("{length} privileges given, more than the {maximum} that exist")]
    TooMany {
        /// The number given.
        length: usize,
        /// The size of the closed set.
        maximum: usize,
    },
    /// A privilege was named twice. A repeat is a caller's mistake, and silently
    /// merging it would hide that.
    #[error("privilege {} named more than once", .privilege.sql_keyword())]
    Duplicate {
        /// The privilege that appeared again.
        privilege: DatabasePrivilege,
    },
}
