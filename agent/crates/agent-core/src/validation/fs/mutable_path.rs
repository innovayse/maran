//! A path inside an account's home that a mutation may address.

use super::browse_path::BrowsePath;
use super::mutable_path_error::MutablePathError;
use crate::agent_paths::AgentPaths;

/// A path relative to an account's home naming an entry a mutation may change.
///
/// Stricter than [`BrowsePath`] in exactly two ways: it is never empty (the home
/// itself cannot be moved, copied, trashed or deleted) and its first component is
/// never [`AgentPaths::TRASH_DIRECTORY_NAME`] (the trash cannot be moved into
/// itself, copied or deleted through the ordinary rpcs). Everything else, the
/// component rules and the depth ceiling, is `BrowsePath`'s, so the two cannot
/// disagree about what a name means.
///
/// This is advice, not the containment: the descriptor walk is
/// (rules/security.md item 2).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MutablePath {
    /// The components in order, each proved to be an entry name. Never empty.
    components: Vec<String>,
}

impl MutablePath {
    /// Parses `text` as a path of an entry inside an account's home.
    ///
    /// # Errors
    ///
    /// Returns [`MutablePathError::Malformed`] for anything [`BrowsePath::parse`]
    /// refuses, [`MutablePathError::Home`] for the empty path and
    /// [`MutablePathError::Reserved`] when the first component is the trash
    /// directory's name.
    pub fn parse(text: &str) -> Result<Self, MutablePathError> {
        let browse = BrowsePath::parse(text)?;
        if browse.is_home() {
            return Err(MutablePathError::Home);
        }
        if browse.components().first().map(String::as_str) == Some(AgentPaths::TRASH_DIRECTORY_NAME)
        {
            return Err(MutablePathError::Reserved);
        }
        Ok(Self {
            components: browse.components().to_vec(),
        })
    }

    /// The components above the final name, in order; empty when the entry sits
    /// directly in the home.
    #[must_use]
    pub fn parent_components(&self) -> &[String] {
        match self.components.split_last() {
            Some((_last, parents)) => parents,
            None => &[],
        }
    }

    /// The final component: the entry's own name.
    #[must_use]
    pub fn file_name(&self) -> &str {
        self.components.last().map_or("", String::as_str)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fs/mutable_path_tests.rs"]
mod tests;
