//! What a layer answers when it is asked to follow the host's addresses.

/// How a layer's attempt to follow ended, when it did not fail.
///
/// The watch records a layer as brought to a reading ONLY for [`Self::Followed`]. A layer that
/// had nothing to follow yet answers [`Self::Pending`] and is asked again at the next
/// interval; recording it would claim a lockdown layer is current when nothing was written to
/// it, and nothing would ask again until the host's addresses changed once more.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum HostAddressLayerOutcome {
    /// The layer holds the addresses it was asked to follow, written (or already so) and in
    /// effect.
    Followed,
    /// The layer is not in a state to follow (no firewall loaded yet, mail stopped, or the host
    /// moved again while it was being written); whatever could be brought current without
    /// effect was, and the layer is asked again at the next interval.
    Pending,
}
