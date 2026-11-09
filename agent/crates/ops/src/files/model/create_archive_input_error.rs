//! Why a request to create an archive was refused before any work.

/// Rejection reasons for [`super::create_archive_input::CreateArchiveInput::new`].
#[derive(Debug, Clone, Copy, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum CreateArchiveInputError {
    /// No source was named.
    #[error("no source was named")]
    NoSources,
    /// More sources than one archive takes.
    #[error("too many sources")]
    TooManySources,
    /// A source was the home itself.
    #[error("a source is the home directory")]
    SourceIsHome,
    /// The destination was the home itself.
    #[error("the destination is the home directory")]
    DestinationIsHome,
    /// A source equals, or lies inside, another source, so a member would be
    /// written twice.
    #[error("sources overlap")]
    OverlappingSources,
    /// The destination lies inside a source, so the archive would contain itself.
    #[error("the destination lies inside a source")]
    DestinationInsideSource,
}
