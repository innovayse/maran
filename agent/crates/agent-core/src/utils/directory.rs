//! Directory helpers: what a tree occupies, and whatever else about directories
//! earns a second caller.

use std::path::Path;

/// Sums the sizes of every regular file under `path`.
///
/// Unreadable entries are skipped rather than failing the whole measurement: the
/// number is reported to a person as "space used", and refusing to produce it
/// because one file could not be read is worse than being slightly low.
///
/// Symlinks are counted as zero and never followed. A link into `/` would otherwise
/// make an account look enormous, and a link into its own parent would make the walk
/// endless — the second of which is a denial of service a customer can create with
/// one command in their own home directory.
///
/// A missing path is zero, not an error: every caller measures something that may
/// legitimately not exist yet, or that is about to be deleted.
#[must_use]
pub fn directory_size(path: &Path) -> u64 {
    let Ok(metadata) = path.symlink_metadata() else {
        return 0;
    };

    if metadata.is_symlink() {
        return 0;
    }

    if metadata.is_file() {
        return metadata.len();
    }

    let Ok(entries) = path.read_dir() else {
        return 0;
    };

    entries
        .flatten()
        .map(|entry| directory_size(&entry.path()))
        .sum()
}

#[cfg(test)]
#[path = "../tests/utils/directory_tests.rs"]
mod tests;
