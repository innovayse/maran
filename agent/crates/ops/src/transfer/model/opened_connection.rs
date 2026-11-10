//! A connection to a source, ready to use, and the key it holds.

use crate::transfer::model::ssh_connection::SshConnection;
use crate::transfer::transfer_scratch::TransferScratch;

/// A connection ready to be used, and the scratch directory that holds its key.
///
/// The directory is removed when this value is dropped, so the key lives exactly
/// as long as whoever holds it.
pub struct OpenedConnection<'a> {
    /// The resolved, laid-out connection every `ssh` run on it uses.
    pub connection: SshConnection,
    /// The root-only directory holding the private key and the known_hosts file.
    pub scratch: TransferScratch<'a>,
}
