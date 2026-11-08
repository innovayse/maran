//! Where the listener learns which panel certificate is pinned right now.

use std::fmt::Debug;

use super::pinned_panel_certificate::PinnedPanelCertificate;

/// The answer to "which panel is this node enrolled with, at this moment".
///
/// Two calls, because the callers are of two kinds. The TLS verifier is synchronous and runs
/// inside the handshake, so it can only read what is already in memory: [`PinSource::current`]
/// is that read and never touches the disk. Reading the disk is blocking work and belongs
/// off the async workers, so it is [`PinSource::refresh`], which the async callers (the
/// handshake and the per-request admission check) await BEFORE they ask `current`. A caller
/// that refreshes and then reads sees the pin as it was no earlier than the start of its own
/// refresh: removing the pin (`maran node leave`) stops admission at the next handshake and the
/// next request without a restart, and a session admitted under a pin that has since gone does
/// not keep working.
#[tonic::async_trait]
pub trait PinSource: Send + Sync + Debug {
    /// The pinned panel certificate as of the last [`PinSource::refresh`], or `None` when the
    /// node is not enrolled. Cheap and non-blocking: it reads memory only.
    fn current(&self) -> Option<PinnedPanelCertificate>;

    /// Brings what [`PinSource::current`] answers up to date with the pin's real state. Any
    /// disk read happens off the async workers. A pin that cannot be read is a pin removed:
    /// the next `current` answers `None`.
    async fn refresh(&self);
}
