//! What the panel can do with one catalogue PHP extension on this host.

/// Whether an extension can be switched on and off on this host's family.
///
/// Derived from the adapter's two answers for the extension and never stored:
/// a package the family has no name for, and a family with no clean switch, are
/// facts of the distribution (`PhpExtensionFacts`), and the panel offers only
/// what the host can actually do.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ExtensionAvailability {
    /// The family has a package for it and a switch for both directions: it can
    /// be turned on (installing the package if needed) and turned off.
    Available,
    /// The family has a package but no clean switch. It can be turned ON, by
    /// installing the package, and not off: the agent refuses to edit the
    /// distribution's ini files by hand.
    DisableNotSupported,
    /// The family has no separate package for it (built into the base package,
    /// or not offered), so the agent cannot turn it on.
    NotPackaged,
}
