//! What a startup pass over one version's php-fpm pool tree decided.

/// The three outcomes of validating one installed PHP version's pool tree on
/// disk and reloading its service onto it.
///
/// Three outcomes and not a `Result`, for the reason the web tree's own
/// reconciliation gives: the two failures are different jobs for an operator
/// and are logged at different levels. A tree `php-fpm -t` rejects is a broken
/// pool file somebody must fix by hand; a reload that refuses on an accepted
/// tree is usually a php-fpm service that is not running, which is not a
/// defect in any file.
#[derive(Debug, PartialEq, Eq)]
pub enum PoolTreeDecision {
    /// The tree validated and the version's php-fpm was reloaded onto it.
    Applied,
    /// `php-fpm -t` rejected what is on disk. Nothing was reloaded.
    Refused,
    /// The tree validated but the reload did not take.
    NotReloaded,
}
