//! How a family packages Node.js.

/// Whether a family's repositories carry one package per Node.js major version or a single
/// `nodejs` package of whatever major the release ships.
///
/// The two need different questions asked of the package manager: a per-major family answers
/// "is `nodejs20` offered?", while a single-package family answers "which major is the
/// candidate of `nodejs`?". Distribution sources only: no third-party repository is added
/// (`docs/apps/README.md`), so a major the sources do not carry is not offered.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NodejsPackaging {
    /// One package per major (`nodejs20`), offered when the sources carry that name.
    PerMajorPackages,
    /// One `nodejs` package; its candidate version decides which major it is.
    SingleDistroPackage,
}
