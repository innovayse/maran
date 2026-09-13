//! One database's line in a backup's manifest.

use serde::{Deserialize, Serialize};

/// What the manifest records about one dumped database.
///
/// The checksum is the load-bearing field and the reason this is a struct
/// rather than a list of names: a restore extracts the dumps root-side and
/// checks each one against the digest recorded here BEFORE loading it, because
/// the loader connects to the server as `root@localhost` and a dump file that
/// is not the one this backup wrote is arbitrary SQL as the database
/// superuser.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ManifestDatabase {
    /// The database's full name, prefix included, exactly as it was dumped.
    pub name: String,

    /// The size of the dump file, in bytes.
    pub bytes: u64,

    /// SHA-256 of the dump file, hex, lowercase.
    pub sha256: String,
}
