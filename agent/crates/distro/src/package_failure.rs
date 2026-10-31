//! What kind of failure a package install that exited non-zero was.

/// The verdict on one failed package-manager run: whether the fault lies with
/// a package source outside this host, and which way, or with the host itself.
///
/// The shape both families answer with, so it sits flat in this crate beside
/// [`crate::VsftpdTlsVersionKeys`] rather than in a family folder: what each
/// family's package manager PRINTS differs, and the reading of it is
/// [`crate::DistroAdapter::classify_package_failure`]; what the panel needs to
/// know does not.
///
/// The three cases need different things from an operator, which is why they
/// are three and not two: a mirror that answered with an error is a mirror to
/// change, a mirror nobody could reach is a network to look at or a moment to
/// wait, and everything else — a package the configured repositories do not
/// carry, a held package database lock, a maintainer script that failed — is
/// this host's own state.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PackageFailure {
    /// A package source answered and refused: an HTTP error status for the
    /// repository metadata or for a package archive.
    ///
    /// Chosen over [`Self::SourceUnreachable`] when one run shows both, because
    /// a source that answered with a refusal is the more specific observation.
    SourceRefused,
    /// No package source could be reached: a name that did not resolve, a
    /// connection that was refused or timed out, a network that was down.
    SourceUnreachable,
    /// Everything that is not a source's fault, including a run whose output
    /// names no fetch failure at all and one that exited with a status other
    /// than the package manager's documented failure status. The default
    /// reading on purpose: blaming a mirror is a claim, and an unproven one
    /// would send the operator away from the host that actually broke.
    Local,
}
