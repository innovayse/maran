//! Why a caller-supplied search query was refused.

/// Rejection reasons for [`super::search_query::SearchQuery::parse`].
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum SearchQueryError {
    /// The query was empty, which would match every entry.
    #[error("search query is empty")]
    Empty,
    /// The query was longer than any entry name can be.
    #[error("search query is too long")]
    TooLong,
    /// The query held `/`, which no entry name contains, so it could never match.
    #[error("search query contains a separator")]
    ContainsSeparator,
    /// The query held a NUL or another control character.
    #[error("search query holds a control character")]
    ForbiddenCharacter,
}
