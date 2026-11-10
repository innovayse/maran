//! A token of an nginx configuration file.

/// One lexical element of an nginx configuration.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum NginxToken {
    /// A word, quotes removed.
    Word(String),
    /// `;`, which ends a directive.
    Semicolon,
    /// `{`, which opens a block.
    OpenBrace,
    /// `}`, which closes a block.
    CloseBrace,
}
