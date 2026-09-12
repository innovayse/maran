//! Resolving a customer-supplied path to the entry it names, inside a home.
//!
//! **Not "every customer-supplied path resolves through here", which is what
//! this line used to say.** Measured against the workspace, the containment of a
//! customer file operation is the descriptor walk in
//! `ops::files::open_parent_directory` — `openat` with `O_NOFOLLOW`, one
//! component at a time, ownership checked at each — and not this function, which
//! answers where a path led at one instant. See [`resolve_in_home`] for which
//! mechanism holds up which operation, and why the difference is a design and
//! not an omission.

use std::path::{Path, PathBuf};

use super::path_error::PathError;
use crate::agent_paths::AgentPaths;
use crate::validation::system::name::AccountName;

/// Resolves `relative` inside `account`'s home directory.
///
/// Returns the canonical absolute path, which is what callers must use from then
/// on: resolving and then reopening by the original path would reintroduce the
/// race this function exists to close.
///
/// # What this actually contains, and what it does not
///
/// The name reads like *the* path control, and a reviewer looking for one will
/// look for a call to this function. It is not that control, and what this
/// function is instead is written out here rather than left to be re-derived
/// — or, worse, quoted from whatever the security checklist happens to say
/// today: a comment that cites another document's sentence breaks when that
/// sentence is edited, which is how an earlier version of this paragraph came
/// to assert a claim the rule no longer made. Its production callers are exactly
/// two, both reached through a host seam's `resolve_in_account_home`:
///
/// - `ops::files::delete_entry` — to LOCATE an entry that already exists, so a
///   removal can tell "there was nothing there" from "the forked child refused
///   it". The child's outcome is an exit status and cannot make that
///   distinction. Containment there is incidental; the unlink itself is done by
///   `ops::files::remove_in_home`'s descriptor walk.
/// - `ops::sites::resolved_site_paths` — to resolve a document root that must be
///   written into an nginx config as a canonical absolute path.
///
/// **Nothing that is WRITTEN is contained by this function.** Every write to a
/// customer path descends from the home one component at a time through
/// `ops::files::open_parent_directory`, with `O_NOFOLLOW` and `O_DIRECTORY` at
/// every level and an ownership check at every level, inside
/// [`crate::privs::fork_as_account`]. That is strictly stronger than what this
/// function can offer, because a descriptor names an inode: there is no window
/// between the check and the use for the account — who owns every directory
/// being walked — to rename a level. This function answers "where did that path
/// lead?" at one instant and leaves exactly that window open.
///
/// So the honest statement of the control is two-part: **the descriptor walk
/// contains writes, and this resolves reads that must name an existing entry.**
/// A new operation that adopts this function and then reopens by path has
/// reintroduced the race while passing the checklist, which is the failure mode
/// the paragraph above exists to stop.
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
#[path = "../../tests/validation/fs/path_tests.rs"]
mod tests;
