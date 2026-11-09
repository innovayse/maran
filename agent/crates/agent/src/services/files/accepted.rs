//! What one message of a client-streamed write contributes.

use maran_ops::files::WriteFileInput;

/// What one message of the stream contributes.
pub struct Accepted {
    /// The validated operation input, on the first message only.
    pub input: Option<WriteFileInput>,
    /// The body bytes of this message, possibly empty.
    pub chunk: Vec<u8>,
}
