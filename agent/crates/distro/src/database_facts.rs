//! The database client and its service unit.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use crate::adapter_binary::AdapterBinary;

/// The database client and its service unit.
pub trait DatabaseFacts: Send + Sync {
    /// Absolute path of the `mysql` client binary, for the process-execution
    /// allow-list.
    ///
    /// Every database and database-user operation is a statement handed to this
    /// client; nothing in the agent speaks the wire protocol itself. Both
    /// families install it at `/usr/bin/mysql` today — stated here so a reader
    /// does not wonder whether one family was copied by mistake — and it is
    /// still asked of the adapter, because a binary is spawnable only because
    /// this trait named it, and `ops` names no absolute path of its own.
    fn mysql_client_binary(&self) -> AdapterBinary;

    /// Name of the database service unit, for `systemctl restart`.
    ///
    /// `mariadb` on both families, because both ship MariaDB rather than MySQL
    /// proper. The agreement is a fact about these two distributions, not a rule
    /// the crate relies on: if a family ever ships MySQL proper, this is the one
    /// method whose answer changes, and it changes for that family alone.
    fn mysql_service(&self) -> &'static str;
}
