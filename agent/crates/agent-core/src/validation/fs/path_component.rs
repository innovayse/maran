//! The check every path component of a customer-supplied path passes.

use super::path_component_problem::PathComponentProblem;

/// Longest single component the agent will accept.
///
/// 255 bytes is the limit every filesystem in the support matrix enforces on a
/// name, so a longer one could not be created anyway; refusing it here turns an
/// `ENAMETOOLONG` from inside a forked child — which can report nothing but an
/// exit status — into a typed answer the panel can read.
const MAXIMUM_COMPONENT: usize = 255;

/// Judges one component of a path split at `/`.
///
/// The second path type that needed these rules (`BrowsePath` beside
/// `RelativePath`) moved them here instead of keeping two copies that could
/// drift apart: a component is an ordinary entry name when it is not empty, not
/// `.` or `..`, holds no control character and is not longer than a filesystem
/// accepts. What differs between the two types — how many components and
/// whether none at all is a path — stays in each type.
///
/// Control characters are checked on the raw bytes rather than on `char`s: a
/// NUL is what truncates the name at the C boundary, and every other control
/// character is refused with it because none belongs in a file name the panel
/// asked for (rules/security.md item 4).
///
/// # Errors
///
/// Returns the [`PathComponentProblem`] naming the first rule `component` breaks.
pub(crate) fn check_path_component(component: &str) -> Result<(), PathComponentProblem> {
    if component.is_empty() {
        return Err(PathComponentProblem::Empty);
    }
    if component == "." || component == ".." {
        return Err(PathComponentProblem::Traversal);
    }
    if component.bytes().any(|byte| byte.is_ascii_control()) {
        return Err(PathComponentProblem::ForbiddenCharacter);
    }
    if component.len() > MAXIMUM_COMPONENT {
        return Err(PathComponentProblem::TooLong);
    }
    Ok(())
}
