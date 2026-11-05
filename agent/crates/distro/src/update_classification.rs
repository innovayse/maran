//! Whether a pending package update is a security update.

/// What the host's package metadata says about one pending update
/// (issue #163).
///
/// `Unmeasured` is a verdict about the READING and not about the package: the
/// family could not tell, or a second reading that was needed failed. It is
/// never folded into `Ordinary`, because `Ordinary` is a measured answer — the
/// metadata was read and the package is not marked as a security fix — and a
/// package whose classification was not read must not be reported as one the
/// metadata cleared.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum UpdateClassification {
    /// The classification was not read: a reader that does not classify yet, or
    /// a second reading that failed.
    Unmeasured,
    /// The package metadata marks the update as a security fix.
    Security,
    /// The metadata was read and does not mark the update as a security fix.
    Ordinary,
}
