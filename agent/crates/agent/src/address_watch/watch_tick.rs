//! What one check of the host's addresses found and did.

/// The result of one [`HostAddressWatch::tick`](crate::address_watch::host_address_watch::HostAddressWatch::tick).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum WatchTick {
    /// The host's addresses could not be read; no layer was touched.
    SourceUnreadable,
    /// Every layer already follows the addresses just read; nothing was written.
    NothingChanged,
    /// At least one layer did not hold the addresses just read and was asked to
    /// follow them.
    Followed {
        /// How many layers were asked to follow.
        asked: usize,
        /// How many of them failed and will be asked again next interval.
        failed: usize,
        /// How many had nothing they could follow yet (no firewall loaded, Postfix
        /// stopped) and will be asked again next interval.
        pending: usize,
    },
}
