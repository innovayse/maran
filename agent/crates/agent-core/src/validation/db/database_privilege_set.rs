//! A duplicate-free set of database privileges with a fixed rendering order.

use crate::validation::db::database_privilege::DatabasePrivilege;
use crate::validation::db::database_privilege_set_error::DatabasePrivilegeSetError;

/// Separator of a rendered privilege list.
const LIST_SEPARATOR: &str = ", ";

/// Some of the closed set of [`DatabasePrivilege`]s, held as one bit per member.
///
/// A set rather than a list so that "the privileges the login holds" and "the
/// privileges it is to hold" compare and subtract without caring about the order
/// anything arrived in, and so that a statement built from one is the same text
/// every time: a test can pin the statement, and a retry sends the same bytes.
///
/// The empty set is a value, not an error: it is how "revoke everything on this
/// database" is said.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct DatabasePrivilegeSet(u32);

impl DatabasePrivilegeSet {
    /// The set holding no privilege.
    #[must_use]
    pub const fn empty() -> Self {
        Self(0)
    }

    /// Builds the set of exactly the privileges in `privileges`.
    ///
    /// # Errors
    ///
    /// - [`DatabasePrivilegeSetError::TooMany`] when more were given than the
    ///   closed set holds.
    /// - [`DatabasePrivilegeSetError::Duplicate`] when one is named twice.
    pub fn of(privileges: &[DatabasePrivilege]) -> Result<Self, DatabasePrivilegeSetError> {
        if privileges.len() > DatabasePrivilege::ALL.len() {
            return Err(DatabasePrivilegeSetError::TooMany {
                length: privileges.len(),
                maximum: DatabasePrivilege::ALL.len(),
            });
        }

        let mut bits = 0_u32;
        for privilege in privileges {
            let bit = 1_u32 << privilege.position();
            if bits & bit != 0 {
                return Err(DatabasePrivilegeSetError::Duplicate {
                    privilege: *privilege,
                });
            }
            bits |= bit;
        }

        Ok(Self(bits))
    }

    /// The set of every privilege for which `held` answers `true`.
    ///
    /// For reading what a login holds out of the server's grant row, one column
    /// per privilege. It cannot fail, because the closed set is walked once.
    #[must_use]
    pub fn from_all_where(held: impl Fn(DatabasePrivilege) -> bool) -> Self {
        let bits = DatabasePrivilege::ALL
            .into_iter()
            .filter(|privilege| held(*privilege))
            .fold(0_u32, |bits, privilege| {
                bits | (1_u32 << privilege.position())
            });

        Self(bits)
    }

    /// Whether `privilege` is in the set.
    #[must_use]
    pub const fn contains(self, privilege: DatabasePrivilege) -> bool {
        self.0 & (1_u32 << privilege.position()) != 0
    }

    /// Whether the set holds nothing.
    #[must_use]
    pub const fn is_empty(self) -> bool {
        self.0 == 0
    }

    /// The privileges in `self` that are not in `other`.
    #[must_use]
    pub const fn difference(self, other: Self) -> Self {
        Self(self.0 & !other.0)
    }

    /// The members, in the fixed order of [`DatabasePrivilege::ALL`].
    pub fn iter(self) -> impl Iterator<Item = DatabasePrivilege> {
        DatabasePrivilege::ALL
            .into_iter()
            .filter(move |privilege| self.contains(*privilege))
    }

    /// The members as the comma-separated list a `GRANT` or `REVOKE` takes,
    /// in the fixed order, or an empty string for the empty set.
    #[must_use]
    pub fn sql_list(self) -> String {
        self.iter()
            .map(DatabasePrivilege::sql_keyword)
            .collect::<Vec<&str>>()
            .join(LIST_SEPARATOR)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/db/database_privilege_set_tests.rs"]
mod tests;
