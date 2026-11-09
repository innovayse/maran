//! The text a name search looks for.

use super::search_query_error::SearchQueryError;

/// Longest query, in bytes: no entry name is longer, so none could match.
const MAXIMUM_BYTES: usize = 255;

/// A case-insensitive substring of an entry NAME.
///
/// Not a glob, a regular expression or a program: matching is a plain
/// `contains` on lowercased text, so a query cannot steer the search, only
/// narrow what it reports. Stored lowercased, so the per-name work is one
/// lowercase and one `contains`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SearchQuery {
    /// The query, lowercased.
    needle: String,
}

impl SearchQuery {
    /// Parses `text` as a search query.
    ///
    /// # Errors
    ///
    /// Returns [`SearchQueryError::Empty`] for empty text,
    /// [`SearchQueryError::TooLong`] for more than 255 bytes,
    /// [`SearchQueryError::ContainsSeparator`] for a `/` and
    /// [`SearchQueryError::ForbiddenCharacter`] for a control character.
    pub fn parse(text: &str) -> Result<Self, SearchQueryError> {
        if text.is_empty() {
            return Err(SearchQueryError::Empty);
        }
        if text.len() > MAXIMUM_BYTES {
            return Err(SearchQueryError::TooLong);
        }
        if text.contains('/') {
            return Err(SearchQueryError::ContainsSeparator);
        }
        if text.bytes().any(|byte| byte.is_ascii_control()) {
            return Err(SearchQueryError::ForbiddenCharacter);
        }
        Ok(Self {
            needle: text.to_lowercase(),
        })
    }

    /// Whether `name` contains the query, ignoring case.
    #[must_use]
    pub fn matches(&self, name: &str) -> bool {
        name.to_lowercase().contains(&self.needle)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fs/search_query_tests.rs"]
mod tests;
