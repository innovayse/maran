//! What kind of host a source is.

/// The kind of host a registered source turned out to be.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SourceKind {
    /// A Linux host with no panel recognised: the generic adapter reads it.
    GenericLinux,
    /// A host that runs a Maran panel.
    Maran,
}
