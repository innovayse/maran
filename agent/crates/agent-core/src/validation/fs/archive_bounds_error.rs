//! Why the caps an archive request stated were refused.

/// Rejection reasons for [`super::extract_bounds::ExtractBounds::parse`] and
/// [`super::create_bounds::CreateBounds::parse`].
///
/// A cap of zero is refused rather than read as "no cap": the contract makes
/// every cap required, and a check that admits everything looks exactly like a
/// check that works. A cap above the agent's own ceiling is refused rather than
/// clamped, so a panel configured past what the agent will do hears about it.
#[derive(Debug, Clone, Copy, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum ArchiveBoundsError {
    /// The entry cap was zero.
    #[error("the entry cap is missing")]
    NoEntryCap,
    /// The byte cap was zero.
    #[error("the byte cap is missing")]
    NoByteCap,
    /// The ratio cap was zero.
    #[error("the ratio cap is missing")]
    NoRatioCap,
    /// The entry cap was above the agent's ceiling.
    #[error("the entry cap is above what the agent allows")]
    EntryCapTooHigh,
    /// The byte cap was above the agent's ceiling.
    #[error("the byte cap is above what the agent allows")]
    ByteCapTooHigh,
    /// The ratio cap was above the agent's ceiling.
    #[error("the ratio cap is above what the agent allows")]
    RatioCapTooHigh,
}
