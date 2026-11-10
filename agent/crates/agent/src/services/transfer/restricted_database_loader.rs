//! The [`DatabaseLoader`] that composes the backup area's restricted loader.

use std::fs::File;

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_ops::backup::{BackupError, ProcessRestrictedDumpHost, load_dump_restricted};
use maran_ops::transfer::{DatabaseLoader, TransferOpError};

/// Loads a dump a source produced through `ops::backup`'s restricted loader.
///
/// This is the one place the transfer area meets the backup area, and it is in
/// the service crate because areas never import each other. The loader is the
/// same one an imported backup artifact takes: the dump is scanned, a temporary
/// user that holds the one database is created, the dump is loaded as it, and the
/// user is dropped on every path. There is no fallback to the database superuser;
/// a dump the scan refuses is refused.
pub struct RestrictedDatabaseLoader {
    /// The host the loader drives.
    host: ProcessRestrictedDumpHost,
}

impl RestrictedDatabaseLoader {
    /// Creates the loader around the database host.
    #[must_use]
    pub fn new(host: ProcessRestrictedDumpHost) -> Self {
        Self { host }
    }
}

impl DatabaseLoader for RestrictedDatabaseLoader {
    /// Loads `dump` into `database` through the restricted loader.
    ///
    /// # Errors
    ///
    /// [`TransferOpError::LoadRefused`] when the scan refused the dump, and
    /// [`TransferOpError::LoadFailed`] for every other way the load can end.
    fn load(&self, database: &DatabaseName, dump: File) -> Result<(), TransferOpError> {
        load_dump_restricted(&self.host, database, dump).map_err(|error| {
            if matches!(error, BackupError::RestrictedLoadRefused { .. }) {
                TransferOpError::LoadRefused
            } else {
                TransferOpError::LoadFailed
            }
        })
    }
}
