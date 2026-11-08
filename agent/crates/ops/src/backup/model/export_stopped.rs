//! The marker that the consumer of an export stopped taking bytes.

/// An export sink refused a message: the client went away or stopped reading.
///
/// Carries nothing: why the consumer stopped is the transport's to know, and the
/// export's only decision is to stop reading and release the account's lock.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ExportStopped;
