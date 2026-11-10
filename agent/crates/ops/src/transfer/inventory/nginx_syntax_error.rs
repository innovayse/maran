//! Why an nginx configuration could not be read.

/// The reasons a configuration file is skipped whole. None of them carries a
/// byte of the file.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum NginxSyntaxError {
    /// The text is longer than one command output may be.
    #[error("the configuration is longer than the bound")]
    TooLarge,
    /// The text holds a NUL byte.
    #[error("the configuration holds a NUL byte")]
    ControlCharacter,
    /// A quoted word is never closed.
    #[error("a quoted word is not closed")]
    UnterminatedQuote,
    /// A single word is longer than the bound.
    #[error("a word is longer than the bound")]
    WordTooLong,
    /// The configuration, includes expanded, holds more tokens than the bound.
    #[error("the configuration holds more tokens than the bound")]
    TooManyTokens,
    /// A statement holds more words than the bound.
    #[error("a statement holds more words than the bound")]
    StatementTooLong,
    /// A `}` has no `{`, or a `{` is never closed.
    #[error("the braces do not balance")]
    UnbalancedBraces,
    /// Blocks nest deeper than the bound.
    #[error("blocks nest deeper than the bound")]
    TooDeep,
    /// An include chain comes back to a file it already entered.
    #[error("include files include each other")]
    IncludeLoop,
    /// An include chain is deeper than the bound.
    #[error("includes nest deeper than the bound")]
    IncludeTooDeep,
    /// Expanding the includes took more steps than the bound.
    #[error("expanding the includes did more work than the bound")]
    TooMuchWork,
}
