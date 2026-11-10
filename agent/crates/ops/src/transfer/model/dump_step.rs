//! The steps of a database transfer.

/// A step a database transfer has reached, in the order they run.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DumpStep {
    /// Dumping the database on the source into a file on this host.
    Dumping,
    /// Loading the dump through the restricted loader.
    Loading,
}
