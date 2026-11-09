//! What a removal does about a directory whose mode forbids emptying it.

/// Whether a tree removal widens a directory the account owns before emptying it.
///
/// A directory the account owns but has made read-only (`0o555`) cannot have
/// its entries unlinked, so removing the tree under it fails with a refused
/// unlink. For the customer's own delete that refusal is the answer: the mode
/// was the customer's choice and the delete reports it. For the copy staging
/// area it is not: a copy preserves the source's bits, so a copy that fails
/// after a read-only directory was completed would leave a tree nobody can
/// remove, and the staging area is the agent's own scratch space, not the
/// customer's data.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum DirectoryAccess {
    /// Directories are emptied with the mode they have; a refused unlink is an error.
    AsFound,
    /// A directory entered is first set to `0o700` on its descriptor, which the
    /// walk has already proved is owned by the account and is not a link.
    Reclaimed,
}
