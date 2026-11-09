//! One lexical unit of a dump, as the scanner's checker sees it.

/// A token of a dump's text.
///
/// Deliberately lossy. A string literal's content is kept only when the
/// statement it sits in is a `SET` (the checker asks), because that is the one
/// place a literal's content decides anything; everywhere else it is an empty
/// string, so scanning an `INSERT` of gigabytes allocates nothing per value.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum DumpToken {
    /// A bare word: a keyword, an unquoted name, a number, a `@variable`. Kept
    /// in the case it was written in and cut at 64 bytes.
    Word(String),
    /// A backtick-quoted identifier, without the backticks, cut at 128 bytes.
    Identifier(String),
    /// A quoted string. Empty unless the checker asked for literal content.
    Literal(String),
    /// A single punctuation byte.
    Symbol(u8),
    /// The end of a statement: a `;`, or the end of the input.
    Terminator,
}
