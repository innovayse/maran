//! What the start-up pass found and did about the zone files.

/// The outcome of [`crate::dns::reconcile_zone_files`].
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ZoneReconciliation {
    /// The daemon did not answer its control channel, so nothing was compared. The
    /// ordinary state of a host that has not enabled DNS: not a fault.
    DaemonNotAnswering,
    /// Every zone file was compared with the daemon.
    Compared {
        /// Zone files found.
        files: usize,
        /// Zones the daemon did not know and now serves.
        added: usize,
        /// Zones the daemon did not know and refused to add.
        failed: usize,
    },
}
