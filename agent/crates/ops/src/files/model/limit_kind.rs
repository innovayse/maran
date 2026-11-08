//! Which stated allowance a bulk file operation ran into.

/// The countable allowance a request stated and the operation reached.
///
/// A closed enum and not a string, so the panel's wording for "too many files"
/// and "too much data" is chosen from a variant and never from agent text
/// (rules/rust.md "Errors"). Only the allowances a request can state appear
/// here; the agent's own hard ceilings are refusals of a different kind.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LimitKind {
    /// The most entries (files and directories together) the request allowed.
    Entries,
    /// The most bytes of file content the request allowed.
    Bytes,
}
