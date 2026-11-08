//! The name of one member of an archive, proved to be a place inside the destination.

use super::archive_member_name_error::ArchiveMemberNameError;
use super::path_component::check_path_component;
use super::path_component_problem::PathComponentProblem;

/// Most components a member's name may have.
///
/// The destination is a browse path (up to 64 components) and the member is
/// written below it, so the whole tree stays well inside what a filesystem
/// accepts, and a name of a million components is refused before the
/// extraction has allocated for it.
pub const MAXIMUM_COMPONENTS: usize = 64;

/// Longest whole member name accepted, in bytes (the kernel's `PATH_MAX`).
const MAXIMUM_TOTAL_BYTES: usize = 4096;

/// A member name that cannot leave the directory the archive is extracted into.
///
/// Constructed only by [`ArchiveMemberName::parse`], from the RAW bytes the
/// archive carries. That is the point of the type: the extraction writes what a
/// scan accepted, so the scan and the writer cannot disagree about what a name
/// means, and nothing the archive crate offers (`enclosed_name`, `path`,
/// `unpack_in`) is ever consulted. Every component is an ordinary entry name
/// (not empty, `.` or `..`, no control character, not over 255 bytes), the name
/// is not absolute, holds no backslash, and is valid UTF-8, so a descriptor walk
/// that hands each component to `openat` provably cannot leave the destination.
///
/// One trailing `/` is accepted and remembered: it is how zip and tar mark a
/// directory, and whether a member IS a directory is decided by its entry type,
/// never by this marker alone.
///
/// This is advice, not the containment: the descriptor walk is
/// (rules/security.md item 2).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ArchiveMemberName {
    /// The components joined with `/`, without a leading or trailing separator.
    path: String,
    /// Whether the archive wrote a separator after the last component.
    trailing_separator: bool,
}

impl ArchiveMemberName {
    /// Parses the raw name bytes of an archive member.
    ///
    /// # Errors
    ///
    /// Returns [`ArchiveMemberNameError::Empty`] for no bytes,
    /// [`ArchiveMemberNameError::TooLong`] for more than 4096,
    /// [`ArchiveMemberNameError::NotUtf8`] for bytes that are not UTF-8,
    /// [`ArchiveMemberNameError::Backslash`] for a name holding `\`,
    /// [`ArchiveMemberNameError::Absolute`] for one starting with `/`,
    /// [`ArchiveMemberNameError::EmptyComponent`] for a doubled separator,
    /// [`ArchiveMemberNameError::Traversal`] for a `.` or `..` component,
    /// [`ArchiveMemberNameError::ForbiddenCharacter`] for a control character
    /// (a NUL included), [`ArchiveMemberNameError::ComponentTooLong`] for a
    /// component over 255 bytes and [`ArchiveMemberNameError::TooDeep`] for more
    /// than 64 components.
    pub fn parse(raw: &[u8]) -> Result<Self, ArchiveMemberNameError> {
        if raw.is_empty() {
            return Err(ArchiveMemberNameError::Empty);
        }
        if raw.len() > MAXIMUM_TOTAL_BYTES {
            return Err(ArchiveMemberNameError::TooLong);
        }
        let text = std::str::from_utf8(raw).map_err(|_| ArchiveMemberNameError::NotUtf8)?;
        if text.contains('\\') {
            return Err(ArchiveMemberNameError::Backslash);
        }
        if text.starts_with('/') {
            return Err(ArchiveMemberNameError::Absolute);
        }

        let (body, trailing_separator) = match text.strip_suffix('/') {
            Some(body) => (body, true),
            None => (text, false),
        };

        let mut components = 0_usize;
        for component in body.split('/') {
            check_path_component(component).map_err(|problem| match problem {
                PathComponentProblem::Empty => ArchiveMemberNameError::EmptyComponent,
                PathComponentProblem::Traversal => ArchiveMemberNameError::Traversal,
                PathComponentProblem::ForbiddenCharacter => {
                    ArchiveMemberNameError::ForbiddenCharacter
                }
                PathComponentProblem::TooLong => ArchiveMemberNameError::ComponentTooLong,
            })?;
            // Inside the loop so the ceiling bounds what is walked.
            components += 1;
            if components > MAXIMUM_COMPONENTS {
                return Err(ArchiveMemberNameError::TooDeep);
            }
        }

        Ok(Self {
            path: body.to_owned(),
            trailing_separator,
        })
    }

    /// The name as `/`-joined components, without a trailing separator.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.path
    }

    /// The components, in order; always at least one.
    pub fn components(&self) -> impl Iterator<Item = &str> {
        self.path.split('/')
    }

    /// The last component, the name the member itself gets.
    #[must_use]
    pub fn file_name(&self) -> &str {
        self.path.rsplit('/').next().unwrap_or(&self.path)
    }

    /// Whether the archive wrote a separator after the name.
    #[must_use]
    pub fn had_trailing_separator(&self) -> bool {
        self.trailing_separator
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fs/archive_member_name_tests.rs"]
mod tests;
