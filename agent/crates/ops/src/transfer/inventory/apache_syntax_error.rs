//! Why an Apache configuration could not be read.

/// The reasons a configuration file is skipped whole. None of them carries a
/// byte of the file.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum ApacheSyntaxError {
    /// The text is longer than one command output may be.
    #[error("the configuration is longer than the bound")]
    TooLarge,
    /// A line is longer than the bound, holds a NUL byte, or the line bound cut
    /// the file short. A line that cannot be read whole is never read in part.
    #[error("a line of the configuration was refused")]
    LineRefused,
    /// A line, once its continuations are joined, is longer than the bound.
    #[error("a joined line is longer than the bound")]
    LineTooLong,
    /// A section is closed that was not opened, is closed by another name, is
    /// never closed, or a `VirtualHost` is opened inside another.
    #[error("the sections do not balance")]
    UnbalancedSection,
    /// Sections nest deeper than the bound.
    #[error("sections nest deeper than the bound")]
    TooDeep,
}
