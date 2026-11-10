//! What the host's package sources offer.

/// What the host's package manager answered about a set of packages.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct OfferedPackages {
    /// The asked-for packages that have a candidate, in the order they were asked.
    pub names: Vec<String>,
    /// The package manager's raw answer, for a family whose candidate reading needs more than
    /// the name (the version of the one `nodejs` package).
    pub raw: String,
}
