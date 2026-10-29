//! Sweeping one shared directory of the files a given uid owns.
//!
//! The walk is the part of account deletion that runs as root over directories
//! any customer can write to, so every step is anchored on a descriptor already
//! held (rules/security.md §2): entries are named only relative to an open
//! directory, decided on by `fstatat(AT_SYMLINK_NOFOLLOW)`, and removed with
//! `unlinkat`. No path string reaches a syscall after the root is opened.

use std::ffi::OsString;
use std::fs::{self, File};
use std::io;
use std::os::unix::fs::MetadataExt;
use std::os::unix::io::AsRawFd;

use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::privs::remove_directory_in_directory::remove_directory_in_directory;
use maran_agent_core::privs::remove_file_in_directory::remove_file_in_directory;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;

/// The deepest directory nesting the walk descends into; deeper is a refusal.
const MAX_DEPTH: usize = 64;

/// The most directory entries one sweep examines before it gives up; exceeding
/// it is a refusal, never a partial success. A shared temp directory holding
/// more than this is not something a deletion should grind through as root.
const MAX_ENTRIES: usize = 1_000_000;

/// Flags for descending: a directory only, never through a symlink.
const DESCEND_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_DIRECTORY | libc::O_NOFOLLOW | libc::O_CLOEXEC;

/// Removes everything `uid` owns under `root`, and reports whether the tree is
/// now free of the uid.
///
/// Returns `Ok(true)` when nothing owned by `uid` remains reachable. Returns
/// `Ok(false)` — a refusal the caller must act on — when a uid-owned
/// directory still holds another owner's entry (it is never emptied of them:
/// no `chown`, no deletion of someone else's data), when a directory was
/// swapped under the walk, or when the depth or entry bound was exceeded.
///
/// # Rules
///
/// - Decisions use the entry's own `st_uid`, never a path or a link target.
/// - A child on a different device than `root_device` is skipped: no mount is
///   crossed.
/// - Symlinks, files, sockets and the like owned by `uid` are unlinked as
///   names; a hard link loses only its name here.
/// - Every directory is descended into (a uid-owned file can sit in another
///   owner's directory), but only a uid-owned one is ever removed, and only
///   once empty.
/// - A directory is opened `O_NOFOLLOW | O_DIRECTORY` and its owner and device
///   are re-checked on the opened descriptor against the earlier `fstatat`.
/// - Depth is bounded by `MAX_DEPTH` (64) and the entries examined by
///   `MAX_ENTRIES` (1,000,000).
///
/// # Errors
///
/// Returns the I/O error of listing a directory, of opening one, or of an
/// unlink other than the entry having already vanished.
pub(crate) fn sweep_directory_by_owner(
    root: &File,
    uid: u32,
    root_device: u64,
) -> io::Result<bool> {
    let mut budget = MAX_ENTRIES;
    sweep(root, uid, root_device, 0, MAX_DEPTH, &mut budget)
}

/// The walk with its bounds as parameters, so a test can lower them.
///
/// `budget` is the number of entries still allowed to be examined across the
/// whole walk; `depth` counts directories entered below the root.
///
/// # Errors
///
/// As [`sweep_directory_by_owner`].
fn sweep(
    directory: &File,
    uid: u32,
    device: u64,
    depth: usize,
    max_depth: usize,
    budget: &mut usize,
) -> io::Result<bool> {
    if depth > max_depth {
        return Ok(false);
    }

    let mut clean = true;
    for name in list_names(directory)? {
        if *budget == 0 {
            return Ok(false);
        }
        *budget -= 1;

        let stat = match stat_in_directory(directory, &name) {
            Ok(stat) => stat,
            Err(error) if error.kind() == io::ErrorKind::NotFound => continue,
            Err(error) => return Err(error),
        };
        if stat.device != device {
            continue;
        }

        if stat.kind != EntryKind::Directory {
            if stat.uid == uid {
                match remove_file_in_directory(directory, &name) {
                    Ok(()) => {}
                    Err(error) if error.kind() == io::ErrorKind::NotFound => {}
                    Err(error) => return Err(error),
                }
            }
            continue;
        }

        let child = match open_in_directory(directory, &name, DESCEND_FLAGS) {
            Ok(child) => child,
            Err(error) if error.kind() == io::ErrorKind::NotFound => continue,
            Err(error) => return Err(error),
        };
        let opened = child.metadata()?;
        if opened.uid() != stat.uid || opened.dev() != stat.device {
            // Swapped between the stat and the open: not the directory decided on.
            clean = false;
            continue;
        }

        let child_clean = sweep(&child, uid, device, depth + 1, max_depth, budget)?;
        if stat.uid != uid {
            clean &= child_clean;
            continue;
        }
        if !child_clean {
            clean = false;
            continue;
        }
        match remove_directory_in_directory(directory, &name) {
            Ok(()) => {}
            Err(error) if error.kind() == io::ErrorKind::NotFound => {}
            Err(error) if error.raw_os_error() == Some(libc::ENOTEMPTY) => clean = false,
            Err(error) => return Err(error),
        }
    }
    Ok(clean)
}

/// Lists the entry names of a directory held by descriptor.
///
/// Reads `/proc/self/fd/<fd>`, which names the held inode itself and not
/// whatever the directory's old path now points at. Names are collected before
/// anything is removed so the listing is not disturbed by the unlinks.
///
/// # Errors
///
/// Returns the I/O error of reading the directory.
fn list_names(directory: &File) -> io::Result<Vec<OsString>> {
    let mut names = Vec::new();
    for entry in fs::read_dir(format!("/proc/self/fd/{}", directory.as_raw_fd()))? {
        names.push(entry?.file_name());
    }
    Ok(names)
}

#[cfg(test)]
#[path = "../tests/accounts/sweep_directory_by_owner_tests.rs"]
mod tests;
