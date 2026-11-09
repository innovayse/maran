//! The walks over a customer's directory tree that copy, change and remove.
//!
//! Crate-private steps and not operations: each is one way of visiting a tree
//! from a directory descriptor the caller already holds, and each is run by an
//! `*_in_home` function inside a forked child that has dropped to the account.
//! What they share, and why it is a folder: every descent is
//! `openat(O_NOFOLLOW | O_DIRECTORY)` from the descriptor above (never a path
//! rebuilt and reopened), every directory entered must be owned by the account
//! and on the filesystem the walk began on, no symlink is followed at any depth,
//! and depth and entry counts are bounded so a customer's own nesting cannot
//! become the agent's stack or time.

pub(crate) mod chmod_tree;
pub(crate) mod copy_tree;
pub(crate) mod directory_access;
pub(crate) mod open_tree_directory;
pub(crate) mod remove_tree;
pub(crate) mod scan_tree;
pub(crate) mod tree_limits;
pub(crate) mod tree_tally;
