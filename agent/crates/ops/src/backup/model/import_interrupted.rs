//! The marker that an import stream did not arrive whole.

/// An import source ended abnormally: the sender went away, the stream failed,
/// or the sender broke the protocol.
///
/// Carries nothing: why it stopped is the sender's to know, and the importer's
/// only decision is that what it has received must not be published.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ImportInterrupted;
