//! What a bounded read of one directory's entry names found.

use std::ffi::OsString;

/// The names read from a directory, and whether the read stopped at its bound.
///
/// `.` and `..` are never among the names: they are the directory's own links,
/// not entries, and a caller that was handed them would have to remember to
/// skip them. Names are raw bytes ([`OsString`]) because a customer chooses
/// them and nothing makes them UTF-8; deciding whether a name can be shown is
/// the caller's judgement, not this type's.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DirectoryNames {
    /// The names read, in the order the filesystem returned them (unsorted).
    /// At most the bound the caller asked for.
    pub names: Vec<OsString>,
    /// `true` when the read stopped because the bound was reached, so the
    /// directory may hold more names than `names` carries. It is set as soon as
    /// the bound is met, not only when a further name is seen: a directory with
    /// exactly as many names as the bound is reported as exceeded, and a caller
    /// that asks for "limit plus one" therefore learns that the limit was passed.
    pub exceeded: bool,
}
