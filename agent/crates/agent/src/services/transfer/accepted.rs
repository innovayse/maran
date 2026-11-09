//! What one accepted message of a client-streamed import contributes.

use maran_ops::backup::ImportRequest;

/// What one accepted message contributes.
pub struct Accepted {
    /// The import header, on the first message and never again.
    pub header: Option<ImportRequest>,
    /// The chunk the message carried; empty for the header.
    pub chunk: Vec<u8>,
}
