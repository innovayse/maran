//! The question a binary preflight actually needs answered about a path.

/// Answers whether a path names a program this agent could actually spawn.
///
/// Injectable so the check that uses it can be exercised as a fast,
/// deterministic unit test instead of only against a real or polygon
/// filesystem: [`crate::backup::verify_backup_binaries`] takes one of these
/// rather than calling `std::fs::metadata` itself, so a test can hand it a
/// lookup that reports a path as absent without needing a host that is
/// actually missing the package.
pub trait ExecutableLookup {
    /// `true` when `path` names a regular file with at least one execute bit
    /// set for somebody. `false` for anything else: absent, a directory, a
    /// symlink to nothing, or a file nobody may execute.
    fn is_executable(&self, path: &str) -> bool;
}
