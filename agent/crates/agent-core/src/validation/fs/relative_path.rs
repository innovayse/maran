//! A path inside an account's home that cannot name anything outside it.

use std::path::PathBuf;

use super::relative_path_error::RelativePathError;

/// Longest single component the agent will accept.
///
/// 255 bytes is the limit every filesystem in the support matrix enforces on a
/// name, so a longer one could not be created anyway; refusing it here turns an
/// `ENAMETOOLONG` from inside a forked child — which can report nothing but an
/// exit status — into a typed answer the panel can read.
const MAXIMUM_COMPONENT: usize = 255;

/// Most components the agent will descend into or create.
///
/// The write creates every missing level, so this is a ceiling on how much of a
/// customer's home one request may build. Eight is comfortably more than the
/// deepest path the contract has a use for — `sites/<domain>/.well-known/
/// acme-challenge/<token>` is five — and small enough that a request cannot turn
/// into a thousand `mkdirat` calls.
const MAXIMUM_COMPONENTS: usize = 8;

/// A relative path whose containment is a property of the type.
///
/// Constructed only by [`RelativePath::parse`], so holding one is proof that
/// every component is an ordinary entry name: not empty, not `.`, not `..`, no
/// separator, no control character, and not more of them than the agent will
/// walk. Downstream code does not re-check it, because it cannot be built from
/// anything else (rules/rust.md "Validation first").
///
/// That property is what lets the file operations descend a directory at a time
/// with `openat`: each component is a name a `*at` syscall may be handed, so the
/// walk starts at the account's home descriptor and provably cannot leave it —
/// no canonicalization, and therefore no window between deciding a path is safe
/// and using it.
///
/// It is deliberately NOT a `PathBuf` wrapper with a check on the outside. A
/// `PathBuf` re-parses on every use and would let `..` back in through a join;
/// the components are stored split so that there is nothing left to re-parse.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RelativePath {
    /// The components, in order, each already proved to be an entry name.
    /// Never empty: a path with no components names nothing.
    components: Vec<String>,
}

impl RelativePath {
    /// Parses `text` as a path relative to an account's home.
    ///
    /// # Errors
    ///
    /// Returns [`RelativePathError::Empty`] for an empty string,
    /// [`RelativePathError::Absolute`] for one starting with `/`,
    /// [`RelativePathError::EmptyComponent`] for a doubled or trailing
    /// separator, [`RelativePathError::Traversal`] for a `.` or `..` component,
    /// [`RelativePathError::ForbiddenCharacter`] for a control character
    /// anywhere, [`RelativePathError::ComponentTooLong`] for a component over
    /// 255 bytes, and [`RelativePathError::TooDeep`] for more than eight
    /// components.
    pub fn parse(text: &str) -> Result<Self, RelativePathError> {
        if text.is_empty() {
            return Err(RelativePathError::Empty);
        }
        if text.starts_with('/') {
            return Err(RelativePathError::Absolute);
        }

        let mut components = Vec::new();
        for component in text.split('/') {
            if component.is_empty() {
                return Err(RelativePathError::EmptyComponent);
            }
            if component == "." || component == ".." {
                return Err(RelativePathError::Traversal);
            }
            // Checked on the raw bytes rather than on `char`s: a NUL is what
            // truncates the name at the C boundary, and every other control
            // character is refused with it because none belongs in a file name
            // the panel asked for (rules/security.md item 4).
            if component.bytes().any(|byte| byte.is_ascii_control()) {
                return Err(RelativePathError::ForbiddenCharacter);
            }
            if component.len() > MAXIMUM_COMPONENT {
                return Err(RelativePathError::ComponentTooLong);
            }
            // Checked INSIDE the loop, so the ceiling is a bound on what this
            // function allocates rather than a report on what it already has.
            // A path of a million components would otherwise be materialised in
            // full — one `String` each — before being refused. That is the same
            // argument the write stream's byte cap makes for checking a chunk
            // before appending it.
            if components.len() == MAXIMUM_COMPONENTS {
                return Err(RelativePathError::TooDeep);
            }
            components.push(component.to_owned());
        }

        Ok(Self { components })
    }

    /// The components leading to the file, without the file's own name.
    ///
    /// Empty for a path of one component, which is the file sitting directly in
    /// the account's home.
    ///
    /// `split_last` and not `len() - 1`, which would underflow and panic on an
    /// empty component list. `parse` cannot produce one — it refuses empty text,
    /// and `split('/')` on non-empty text always yields at least one item — but
    /// [`Self::file_name`] three lines below already declines to trust that same
    /// invariant, citing the rule that a root process must not panic on input
    /// (rules/rust.md). Two neighbours disagreeing about how far to trust one
    /// invariant is how the weaker one survives a later edit to the constructor.
    #[must_use]
    pub fn parent_components(&self) -> &[String] {
        self.components
            .split_last()
            .map_or(&[][..], |(_last, parents)| parents)
    }

    /// The final component: the name of the file itself.
    #[must_use]
    pub fn file_name(&self) -> &str {
        // The constructor rejects an empty path, so there is always a last
        // component. `unwrap_or_default` rather than an index or an `unwrap`
        // because a root process must not panic on input (rules/rust.md), and
        // an empty name is refused by every `*at` wrapper it could reach.
        self.components.last().map_or("", String::as_str)
    }

    /// The path as a `PathBuf`, for the containment check that answers the same
    /// question a second time.
    ///
    /// Every component has already been proved to be an entry name, so this
    /// join cannot introduce a `..` — it is a rendering of the components, not
    /// a re-parse of the caller's text.
    #[must_use]
    pub fn as_path(&self) -> PathBuf {
        self.components.iter().collect()
    }

    /// The parent directory as a `PathBuf`, for the same second check.
    ///
    /// An empty path buffer for a one-component path, which
    /// [`crate::validation::fs::path::resolve_in_home`] reads as the home directory
    /// itself.
    #[must_use]
    pub fn parent_as_path(&self) -> PathBuf {
        self.parent_components().iter().collect()
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fs/relative_path_tests.rs"]
mod tests;
