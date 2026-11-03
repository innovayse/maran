//! The run that tells whether a package catalogue exists at all.

use crate::pending_updates_failure::PendingUpdatesFailure;

/// A fixed program run, and the reading of it, that tells whether the host's
/// package catalogue has been built (issue #324).
///
/// Asked of the adapter only by a family whose pending-updates run cannot tell a
/// catalogue that was never built from an up-to-date host; the other family has
/// no probe and answers `None`. The arguments are a `'static` constant array no
/// caller value reaches, and the program is
/// [`crate::PackageFacts::package_query_binary`].
#[derive(Debug, Clone, Copy)]
pub struct CatalogueProbe {
    /// The fixed arguments of the probe run; no operand a caller could choose.
    pub arguments: &'static [&'static str],
    /// Reads the probe's exit status and `stdout`.
    ///
    /// Returns `Ok(())` only when the catalogue exists; every other answer is a
    /// [`PendingUpdatesFailure`], never an empty verdict.
    pub read: fn(status: i32, stdout: &str) -> Result<(), PendingUpdatesFailure>,
}
