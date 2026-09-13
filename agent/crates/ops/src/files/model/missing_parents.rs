//! What the descent does about a directory level that is not there.

/// The choice a walk into a customer's home makes when a level is missing.
///
/// An enum and not a `bool` because the two answers are opposite kinds of
/// operation — one changes the customer's filesystem, the other only reads it —
/// and a `create: true` at a call site says nothing about which is which. The
/// removal path must never create anything: an argument that could be
/// mistyped there would have "delete this file" silently building the
/// directories it did not find.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MissingParents {
    /// Create the level, as the account, and descend into it. What a write
    /// does: the ACME challenge directory does not exist until the first
    /// issuance asks for it.
    Create,
    /// Refuse. What a removal does, and what a read would do: a level that is
    /// not there means the entry is not there either.
    Require,
}
