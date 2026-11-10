//! A Node.js major version from the app catalogue, safe to write into a unit.

use crate::validation::web::node_major_error::NodeMajorError;

/// The majors `docs/apps/README.md` offers: the closed catalogue of `nodejs-<major>` apps.
pub const SUPPORTED_MAJORS: [&str; 4] = ["18", "20", "22", "24"];

/// A Node.js major version — `20` — checked once at the boundary against the closed
/// catalogue and then carried as a type.
///
/// The value is written into the description of a site's systemd unit, a file the service
/// manager runs as root's child and which is line-oriented, through a template that escapes
/// nothing by design: values are validated, not escaped (rules/rust.md "Validation first").
/// Membership in a four-element list is the strongest validation there is, which is why this
/// is a lookup and not a digit check. Construction is the only way to obtain one.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NodeMajor(&'static str);

impl NodeMajor {
    /// Parses `candidate` as one of the supported majors.
    ///
    /// # Errors
    ///
    /// - [`NodeMajorError::Empty`] when `candidate` is empty.
    /// - [`NodeMajorError::Unsupported`] for anything that is not exactly one of the
    ///   catalogue's majors, a control character or a longer spelling (`20.1`, `v20`) included.
    pub fn parse(candidate: &str) -> Result<Self, NodeMajorError> {
        if candidate.is_empty() {
            return Err(NodeMajorError::Empty);
        }

        SUPPORTED_MAJORS
            .iter()
            .find(|major| **major == candidate)
            .map(|major| Self(major))
            .ok_or_else(|| NodeMajorError::Unsupported {
                candidate: candidate.to_owned(),
            })
    }

    /// The major as it is written — `20`.
    #[must_use]
    pub fn as_str(&self) -> &'static str {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/node_major_tests.rs"]
mod tests;
