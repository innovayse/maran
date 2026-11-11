//! What the package manager says a removal would do, before it does it.

/// The packages a dry run of one removal would take off the host.
///
/// Two sets, because they mean different things to a caller that decides whether to go ahead: a
/// package in [`Self::removed`] is gone BECAUSE the requested one is (it is the requested
/// package, or something that needs it), while one in [`Self::unused`] goes only because nothing
/// needs it once the removal is done. A package manager that never removes the second kind
/// (`apt-get remove`, `zypper remove`) leaves it empty.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RemovalSimulation {
    /// The requested package and every installed package that depends on it, by package name
    /// (no architecture, no version).
    pub removed: Vec<String>,
    /// Packages that were installed only as dependencies and that nothing needs after the
    /// removal, by package name.
    pub unused: Vec<String>,
}
