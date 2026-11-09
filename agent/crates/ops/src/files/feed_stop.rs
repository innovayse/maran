//! Why the parent stopped feeding an upload to the child.

use std::io;

/// The reasons the parent's feed ended before the upload was complete.
#[derive(Debug)]
pub(crate) enum FeedStop {
    /// The source held more bytes than the cap the request stated.
    OverLimit,
    /// The source ended before it was complete: the sender went away or failed.
    Aborted,
    /// The channel to the child failed or timed out.
    Channel(io::Error),
}

impl FeedStop {
    /// The error the fork sees, which aborts the child's upload.
    #[must_use]
    pub(crate) fn into_io_error(self) -> io::Error {
        match self {
            Self::OverLimit => io::Error::from(io::ErrorKind::InvalidData),
            Self::Aborted => io::Error::from(io::ErrorKind::ConnectionAborted),
            Self::Channel(error) => error,
        }
    }
}
