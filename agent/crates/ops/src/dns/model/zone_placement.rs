//! Where a zone's files were found at start-up.

/// What the files on the host say about how a zone is to be served again.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ZonePlacement {
    /// The zone's file is in the plain directory and nothing says it was signed.
    Unsigned,
    /// The zone's file is in the signed directory and the key directory holds a
    /// complete key (`.key` and `.private`) for the zone.
    Signed,
    /// The zone's file is in the signed directory and no complete key is there.
    /// The zone is NOT served again: a daemon told to sign a zone it holds no key
    /// for generates a new one, whose DS record the registrar does not have.
    KeyMissing,
}
