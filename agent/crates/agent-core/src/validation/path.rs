//! Path containment: every customer-supplied path resolves through here.

use std::path::{Path, PathBuf};

use super::name::AccountName;
use super::path_error::PathError;
use crate::agent_paths::AgentPaths;

/// Resolves `relative` inside `account`'s home directory.
///
/// Returns the canonical absolute path, which is what callers must use from then
/// on: resolving and then reopening by the original path would reintroduce the
/// race this function exists to close.
///
/// # Errors
///
/// Returns [`PathError::NotFound`] when the path does not exist, and
/// [`PathError::EscapesHome`] when it resolves outside the account's home.
pub fn resolve_in_home(account: &AccountName, relative: &Path) -> Result<PathBuf, PathError> {
    resolve_under(
        &PathBuf::from(AgentPaths::ACCOUNT_HOME_ROOT).join(account.as_str()),
        relative,
    )
}

/// Core of [`resolve_in_home`] with the home root injected.
///
/// Containment is decided *after* canonicalization, never by inspecting the path
/// text: `..` segments, a symlink pointing outside the home, and a symlink whose
/// own parent is a symlink all produce a path that looks contained and is not.
/// Asking the filesystem what the path really is answers all three at once.
///
/// # Errors
///
/// As documented on [`resolve_in_home`].
fn resolve_under(home: &Path, relative: &Path) -> Result<PathBuf, PathError> {
    let canonical_home = home.canonicalize().map_err(|_| PathError::NotFound)?;
    let canonical = home
        .join(relative)
        .canonicalize()
        .map_err(|_| PathError::NotFound)?;

    if canonical.starts_with(&canonical_home) {
        Ok(canonical)
    } else {
        Err(PathError::EscapesHome)
    }
}

#[cfg(test)]
#[path = "../tests/validation/path_tests.rs"]
mod tests;
