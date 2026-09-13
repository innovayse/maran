//! The database catalog a backup asks, answered by the database area.

use std::sync::Arc;

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::backup::{BackupError, DatabaseCatalog};
use maran_ops::db::{self, DbHost};

/// Answers [`DatabaseCatalog`] by asking `ops::db` which databases an account
/// owns.
///
/// **This composition is the whole reason the seam exists.** `ops::backup` does
/// not import another `ops` area — a cross-area import inside `ops` is the
/// defect the firewall plan caught, where one area reached into another's
/// helper and the two grew a dependency neither owned — so the backup area
/// declares what it needs as a trait and the service layer wires the answer in,
/// exactly as the accounts service composes four hosts for the deletion
/// cascade.
///
/// What `ops::db::list_databases` returns is already a
/// [`DatabaseName`]
/// per row, so nothing that reaches the dump client's argv came out of a
/// server's listing untyped. Its own doc is emphatic that the listing is a
/// diagnostic view and not a tenant boundary, and that holds here: the panel
/// decides what a customer may see, and this call decides what a backup of an
/// account's databases contains, which is a question about the server.
pub struct DbHostCatalog<H> {
    /// The database server the listing is asked of.
    host: Arc<H>,
}

impl<H> DbHostCatalog<H> {
    /// Creates the catalog around the database host.
    #[must_use]
    pub fn new(host: Arc<H>) -> Self {
        Self { host }
    }
}

impl<H: DbHost + Send + Sync> DatabaseCatalog for DbHostCatalog<H> {
    /// The databases `account` owns, in the order the listing sorted them.
    ///
    /// Every failure of the listing becomes
    /// [`BackupError::DatabasesUnknown`], and the database area's own error is
    /// deliberately not carried across: `ops::backup` has one error enum for
    /// its whole area (rules/rust.md "Errors"), and what a creation needs to
    /// know is that the question was not answered — it refuses rather than
    /// archiving the files alone, because an archive whose manifest lists no
    /// databases is indistinguishable from one taken of an account that has
    /// none.
    fn databases_of(&self, account: &AccountName) -> Result<Vec<DatabaseName>, BackupError> {
        let listed = db::list_databases(self.host.as_ref(), account)
            .map_err(|_error| BackupError::DatabasesUnknown)?;

        Ok(listed.into_iter().map(|summary| summary.name).collect())
    }
}
