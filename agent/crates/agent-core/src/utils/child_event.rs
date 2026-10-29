//! What a helper thread of the core tells the thread that waits.

use crate::utils::output_stream::OutputStream;

/// One message from a reader or writer thread to the waiting thread.
#[derive(Debug)]
pub(crate) enum ChildEvent {
    /// A stream reached end-of-file under its cap; these are all its bytes.
    Output {
        /// Which stream ended.
        stream: OutputStream,
        /// Everything it carried.
        bytes: Vec<u8>,
    },
    /// A stream carried more than its cap. Nothing it carried is kept.
    Overflow(OutputStream),
    /// A stream could not be read.
    ReadFailed(OutputStream),
    /// The input could not be written to the child.
    InputFailed,
    /// Every input byte reached the child's pipe. Sent so the waiting thread
    /// counts the writer as finished and never reads the clock past a verdict
    /// the writer has not yet given.
    InputDelivered,
}
