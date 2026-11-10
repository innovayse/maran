//! Which phase of DNSSEC signing a zone is in.

/// The three phases of a zone's signing, as the panel states them and as the
/// daemon's configuration for the zone shows them (issue #452).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SigningMode {
    /// The daemon does not sign the zone.
    Unsigned,
    /// The daemon signs the zone under the panel's single-key policy.
    Signed,
    /// The daemon follows its built-in `insecure` policy with the same key, while
    /// the DS record is removed at the registrar.
    Withdrawing,
}
