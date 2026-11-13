//! How a family packages Node.js.

/// Whether a family's repositories carry one package per Node.js major version, a single
/// `nodejs` package of whatever major the release ships, or one module stream per major.
///
/// The three need different questions asked of the package manager: a per-major family answers
/// "is `nodejs20` offered?", a single-package family answers "which major is the candidate of
/// `nodejs`?" and a module-stream family answers "which streams of the `nodejs` module are
/// listed?". Distribution sources only: no third-party repository is added
/// (`docs/apps/README.md`), so a major the sources do not carry is not offered.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NodejsPackaging {
    /// One package per major (`nodejs20`; the RHEL 10 generation's `nodejs` for its default major
    /// and `nodejs24` for the next, issue #542), offered when the sources carry that name. The
    /// majors are installed beside each other.
    PerMajorPackages,
    /// One `nodejs` package; its candidate version decides which major it is.
    SingleDistroPackage,
    /// One stream of the `nodejs` module per major (`nodejs:20`), installed and removed through
    /// the module commands of the package manager (issue #532). Only one stream is active at a
    /// time, so only one major is installed at a time.
    ModuleStreams,
}
