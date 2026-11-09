//! The kind of a listed entry.

/// The same four-valued vocabulary `fstatat` reports (file, directory, symlink,
/// other), re-exported here so callers of the file operations import what an
/// entry IS from the area that lists it. One definition, one meaning: a second
/// enum with the same variants would need a conversion that could only be wrong.
pub use maran_agent_core::privs::entry_kind::EntryKind;
