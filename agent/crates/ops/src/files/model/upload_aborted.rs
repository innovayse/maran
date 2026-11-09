//! The marker that an upload did not arrive whole.

/// An upload source ended without finishing: the sender went away, failed, or sent too much.
///
/// Carries nothing: why it stopped is the sender's to know, and the writer's only decision is
/// that what it has received must not be published.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct UploadAborted;
