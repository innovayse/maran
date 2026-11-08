//! The name search itself, as the account, inside its home.

use std::ffi::OsStr;
use std::fs::File;
use std::os::unix::fs::MetadataExt;
use std::path::Path;

use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::list_directory_names::list_directory_names;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;

use crate::files::FilesOpError;
use crate::files::model::search_budget::SearchBudget;
use crate::files::model::search_input::SearchInput;
use crate::files::model::search_match::SearchMatch;
use crate::files::model::search_outcome::SearchOutcome;
use crate::files::model::search_scope::SearchScope;
use crate::files::open_directory_in_home::open_directory_in_home;
use crate::files::representable_name::representable_name;

/// Flags a directory is opened with while descending.
///
/// `O_DIRECTORY` and `O_NOFOLLOW` refuse anything that is not a real directory
/// at that name, a symlink included. `O_NONBLOCK` is the denial-of-service
/// control: if the name was swapped for a FIFO between the `lstat` and the open,
/// `O_DIRECTORY` makes the kernel refuse at once, and the flag keeps that true
/// without relying on the order the kernel checks in. `O_CLOEXEC` keeps the
/// descriptor out of anything spawned later.
const DIRECTORY_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_DIRECTORY | libc::O_NOFOLLOW | libc::O_NONBLOCK | libc::O_CLOEXEC;

/// One directory being searched, with the subdirectories still to enter.
struct Frame {
    /// The open directory; subdirectories are opened relative to it.
    directory: File,
    /// Its path from the home, as components.
    prefix: Vec<String>,
    /// Entries in this directory are at this depth; the start directory is 1.
    depth: u32,
    /// Names of subdirectories to enter, in the order they are visited.
    pending: std::vec::IntoIter<String>,
}

/// Searches `home` from `input.start()` for names containing the query.
///
/// Runs inside `fork_as_account_for_bytes`, as the account. Every directory is
/// reached from the descriptor above it, and every entry is `lstat`ed and never
/// opened unless it is a directory the search enters — so a FIFO, a device or a
/// link in the tree is reported and left alone. The search:
///
/// - never follows a symlink: one is reported with its own size and kind and is
///   not entered;
/// - never crosses onto another filesystem (`find -xdev`): a directory whose
///   device differs from the start's is reported but not entered;
/// - enters only directories the account owns, and re-checks that on the
///   descriptor it opened;
/// - is iterative, with at most `max_depth` descriptors open, so a deep tree
///   cannot exhaust the stack or the descriptor table;
/// - stops at the result limit, at `max_depth`, and at the entry-read budget,
///   marking the outcome `truncated` instead of failing.
///
/// Within a directory names are visited in byte order, so the same tree gives
/// the same answer and a bound cuts at the same place every time.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as the walk to the starting directory does, and
/// [`FilesOpError::DirectoryUnusable`] when the start cannot be listed.
pub(crate) fn search_in_home(
    home: &Path,
    input: &SearchInput,
    uid: u32,
) -> Result<SearchOutcome, FilesOpError> {
    search_within(home, input, uid, SearchBudget::standard())
}

/// [`search_in_home`] with the entry-read budget as a parameter, so a test can
/// reach the budget without building 200 000 names.
fn search_within(
    home: &Path,
    input: &SearchInput,
    uid: u32,
    mut budget: SearchBudget,
) -> Result<SearchOutcome, FilesOpError> {
    let start = open_directory_in_home(home, input.start(), uid)?;
    let device = start
        .metadata()
        .map_err(|_| FilesOpError::DirectoryUnusable)?
        .dev();

    let scope = SearchScope { input, uid, device };
    let mut outcome = SearchOutcome::default();
    let first = scan(
        start,
        input.start().components().to_vec(),
        1,
        &scope,
        &mut budget,
        &mut outcome,
    )?;
    let mut stack = vec![first];

    while !outcome.truncated {
        let Some(frame) = stack.last_mut() else { break };
        let Some(name) = frame.pending.next() else {
            stack.pop();
            continue;
        };

        let Ok(directory) = open_in_directory(&frame.directory, OsStr::new(&name), DIRECTORY_FLAGS)
        else {
            continue;
        };
        // Judged on the descriptor, not on the earlier `lstat` of the name,
        // which the customer could have swapped since.
        let Ok(metadata) = directory.metadata() else {
            continue;
        };
        if !metadata.is_dir() || metadata.uid() != uid || metadata.dev() != device {
            continue;
        }

        let mut prefix = frame.prefix.clone();
        prefix.push(name);
        let depth = frame.depth + 1;
        let child = scan(directory, prefix, depth, &scope, &mut budget, &mut outcome)?;
        stack.push(child);
    }

    Ok(outcome)
}

/// Reads one directory: records its matches and collects its subdirectories.
///
/// # Errors
///
/// Returns [`FilesOpError::DirectoryUnusable`] when the directory cannot be
/// listed.
fn scan(
    directory: File,
    prefix: Vec<String>,
    depth: u32,
    scope: &SearchScope<'_>,
    budget: &mut SearchBudget,
    outcome: &mut SearchOutcome,
) -> Result<Frame, FilesOpError> {
    let SearchScope { input, uid, device } = *scope;
    let listed = list_directory_names(&directory, budget.remaining())
        .map_err(|_| FilesOpError::DirectoryUnusable)?;
    budget.spend(listed.names.len());
    // More names than the budget allowed: what was read is processed, and the
    // search ends after it. Anything unread is exactly why it is truncated.
    if listed.exceeded {
        outcome.truncated = true;
    }

    let mut names = listed.names;
    names.sort();
    let mut subdirectories = Vec::new();

    for name in &names {
        if !input.include_hidden() && name.as_encoded_bytes().first() == Some(&b'.') {
            continue;
        }
        let Some(text) = representable_name(name) else {
            outcome.skipped_entries = outcome.skipped_entries.saturating_add(1);
            continue;
        };
        // An entry that vanished, or `.`/`..`, which the stat refuses, is not
        // an entry to report.
        let Ok(stat) = stat_in_directory(&directory, name) else {
            continue;
        };

        if input.query().matches(text) {
            if outcome.matches.len() >= input.max_results() as usize {
                outcome.truncated = true;
                break;
            }
            outcome.matches.push(SearchMatch {
                path: joined(&prefix, text),
                kind: stat.kind,
                size: stat.size,
                mode: stat.mode,
                modified_unix: stat.modified_unix,
                uid: stat.uid,
                gid: stat.gid,
                links: stat.links,
            });
        }

        if stat.kind == EntryKind::Directory
            && stat.uid == uid
            && stat.device == device
            && depth < input.max_depth()
        {
            subdirectories.push(text.to_owned());
        }
    }

    Ok(Frame {
        directory,
        prefix,
        depth,
        pending: subdirectories.into_iter(),
    })
}

/// `prefix` and `name` as one `/`-separated path.
fn joined(prefix: &[String], name: &str) -> String {
    let mut path = prefix.join("/");
    if !path.is_empty() {
        path.push('/');
    }
    path.push_str(name);

    path
}

#[cfg(test)]
#[path = "../tests/files/search_in_home_tests.rs"]
mod tests;
