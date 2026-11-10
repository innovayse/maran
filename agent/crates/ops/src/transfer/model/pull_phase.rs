//! Which pass of a migration an rsync copy is.

/// The phase of one pull of an account's files (issue #197, phase 2).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PullPhase {
    /// The first copy of a live source: nothing is removed from the destination,
    /// so a file the source deletes meanwhile only disappears at the final pass.
    Initial,
    /// The delta at cut-over: the destination is made to equal the source, so
    /// files the source no longer has are removed from the home.
    Final,
}
