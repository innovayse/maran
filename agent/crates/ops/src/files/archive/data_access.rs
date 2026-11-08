//! Whether a walk over an archive needs the members' bytes.

/// What a walk is for.
///
/// The first pass over an archive only has to judge it, and for a zip that means
/// the central directory alone: no member is decompressed. The second pass
/// writes, and needs every member's bytes.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum DataAccess {
    /// Judge the members; their bytes are not read.
    Skip,
    /// Hand each member's bytes to the visitor.
    Read,
}
