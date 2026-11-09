//! A path inside an account's home that a browse may descend, the home itself included.

use std::path::PathBuf;

use super::browse_path_error::BrowsePathError;
use super::path_component::check_path_component;
use super::path_component_problem::PathComponentProblem;

/// Most components a browse will descend.
///
/// A browse only reads, so unlike [`super::relative_path::RelativePath`] (eight,
/// sized for site document roots, and a bound on what a write may BUILD) the
/// ceiling is about how deep a customer may have nested their own tree and still
/// browse it. Sixty-four is deeper than any real tree and still turns a request
/// of a million components into a refusal before anything is allocated.
const MAXIMUM_COMPONENTS: usize = 64;

/// Longest whole path accepted, in bytes (the kernel's `PATH_MAX`).
const MAXIMUM_TOTAL_BYTES: usize = 4096;

/// A path relative to an account's home whose containment is a property of the type.
///
/// Constructed only by [`BrowsePath::parse`]. Unlike `RelativePath` the empty
/// path is valid and names the home itself, which is where every browse starts.
/// Every component is an ordinary entry name (not empty, `.` or `..`, no control
/// character, not over 255 bytes), so the descriptor walk can hand each one to
/// `openat` and provably cannot leave the home.
///
/// This is advice, not the containment: the walk is (rules/security.md item 2).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BrowsePath {
    /// The components in order, each already proved to be an entry name.
    /// Empty for the home.
    components: Vec<String>,
}

impl BrowsePath {
    /// Parses `text` as a path relative to an account's home; `""` is the home.
    ///
    /// # Errors
    ///
    /// Returns [`BrowsePathError::TooLong`] for text over 4096 bytes,
    /// [`BrowsePathError::Absolute`] for one starting with `/`,
    /// [`BrowsePathError::EmptyComponent`] for a doubled or trailing separator,
    /// [`BrowsePathError::Traversal`] for a `.` or `..` component,
    /// [`BrowsePathError::ForbiddenCharacter`] for a control character,
    /// [`BrowsePathError::ComponentTooLong`] for a component over 255 bytes and
    /// [`BrowsePathError::TooDeep`] for more than 64 components.
    pub fn parse(text: &str) -> Result<Self, BrowsePathError> {
        if text.len() > MAXIMUM_TOTAL_BYTES {
            return Err(BrowsePathError::TooLong);
        }
        if text.is_empty() {
            return Ok(Self {
                components: Vec::new(),
            });
        }
        if text.starts_with('/') {
            return Err(BrowsePathError::Absolute);
        }

        let mut components = Vec::new();
        for component in text.split('/') {
            check_path_component(component).map_err(|problem| match problem {
                PathComponentProblem::Empty => BrowsePathError::EmptyComponent,
                PathComponentProblem::Traversal => BrowsePathError::Traversal,
                PathComponentProblem::ForbiddenCharacter => BrowsePathError::ForbiddenCharacter,
                PathComponentProblem::TooLong => BrowsePathError::ComponentTooLong,
            })?;
            // Inside the loop so the ceiling bounds what is allocated.
            if components.len() == MAXIMUM_COMPONENTS {
                return Err(BrowsePathError::TooDeep);
            }
            components.push(component.to_owned());
        }

        Ok(Self { components })
    }

    /// The components, in order; empty for the home.
    #[must_use]
    pub fn components(&self) -> &[String] {
        &self.components
    }

    /// Whether this path names the account's home itself.
    #[must_use]
    pub fn is_home(&self) -> bool {
        self.components.is_empty()
    }

    /// The final component; empty for the home.
    #[must_use]
    pub fn file_name(&self) -> &str {
        self.components.last().map_or("", String::as_str)
    }

    /// The directory containing this path, or `None` for the home, which has none.
    ///
    /// What a read needs: walk to the parent, then open the leaf by name.
    #[must_use]
    pub fn parent(&self) -> Option<Self> {
        self.components.split_last().map(|(_last, parents)| Self {
            components: parents.to_vec(),
        })
    }

    /// The path as a `PathBuf`; empty for the home.
    ///
    /// A rendering of the proved components, not a re-parse of the caller's text.
    #[must_use]
    pub fn as_path(&self) -> PathBuf {
        self.components.iter().collect()
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fs/browse_path_tests.rs"]
mod tests;
