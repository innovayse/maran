//! The seam between a database transfer and the restricted loader.

use std::fs::File;

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::transfer::transfer_op_error::TransferOpError;

/// Loads a dump someone else wrote into one existing database.
///
/// A trait because the loader lives in another area (`ops::backup`'s
/// restricted loader) and areas never import each other: the agent crate, which
/// owns both, composes the real one. The contract is the loader's: the dump is
/// scanned, loaded as a temporary user that holds `database` and nothing else,
/// and that user is dropped on every path. A dump that cannot be loaded that way
/// is REFUSED and never loaded with more privilege.
pub trait DatabaseLoader: Send + Sync {
    /// Loads `dump` into `database`.
    ///
    /// # Errors
    ///
    /// [`TransferOpError::LoadRefused`] when the scan refused the dump, and
    /// [`TransferOpError::LoadFailed`] when the server refused a statement of it
    /// or the load could not run.
    fn load(&self, database: &DatabaseName, dump: File) -> Result<(), TransferOpError>;
}
