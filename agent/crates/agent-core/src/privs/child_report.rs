//! The two figures a successful forked job reports to its parent.

/// What a child that succeeded reports: two integers.
///
/// The fork gives them no meaning; the operation that chose them does (entries and bytes,
/// changed and skipped, a count and nothing). They are all that crosses from the child on
/// success: no length, string or path.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ChildReport {
    /// The first figure.
    pub first: u64,
    /// The second figure.
    pub second: u64,
}
