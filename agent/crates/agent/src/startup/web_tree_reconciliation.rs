//! The outcome of the startup validation of the web configuration tree.

/// What `reconcile_web_tree` found and did, so a test can observe the
/// decision rather than the log line it produced.
///
/// Three outcomes and not a `Result`, because the two failures are different
/// jobs for an operator and are logged at different levels: a tree the
/// validator rejects is a broken file somebody must fix, and a reload that
/// refuses on an accepted tree is usually a web server that is not running
/// yet.
#[derive(Debug, PartialEq, Eq)]
pub(crate) enum WebTreeReconciliation {
    /// The tree on disk validated and the running server was reloaded onto it.
    Applied,
    /// The validator rejected what is on disk. Nothing was reloaded.
    Refused,
    /// The tree validated but the reload did not take.
    NotReloaded,
}
