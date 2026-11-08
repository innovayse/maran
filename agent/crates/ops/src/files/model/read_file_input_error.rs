//! Why a read request's byte limit was refused.

/// Failures of [`ReadFileInput::new`](super::read_file_input::ReadFileInput::new).
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum ReadFileInputError {
    /// The limit was zero: a read that may return nothing is a mistake.
    #[error("the read limit must be at least one byte")]
    ZeroLimit,
    /// The limit was above what the agent ever streams.
    #[error("the read limit is above the agent's ceiling")]
    AboveCeiling,
}
