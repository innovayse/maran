//! A pin that the daemon can replace while it runs.

use std::sync::{PoisonError, RwLock};

use super::pin_source::PinSource;
use super::pinned_panel_certificate::PinnedPanelCertificate;

/// A [`PinSource`] held in memory and replaceable: whoever watches the pin file on disk calls
/// [`PinSlot::replace`] when it changes, and the listener sees the new state on its next
/// handshake and its next request.
#[derive(Debug, Default)]
pub struct PinSlot {
    /// The pinned certificate, or `None` while the node is not enrolled.
    current: RwLock<Option<PinnedPanelCertificate>>,
}

impl PinSlot {
    /// A slot that pins nothing: no client is admitted until [`PinSlot::replace`] sets one.
    #[must_use]
    pub fn empty() -> Self {
        Self::default()
    }

    /// A slot that starts with `pin`.
    #[must_use]
    pub fn holding(pin: PinnedPanelCertificate) -> Self {
        Self {
            current: RwLock::new(Some(pin)),
        }
    }

    /// Replaces the pin; `None` removes it, after which no client is admitted.
    ///
    /// A lock poisoned by a panicking holder is taken anyway: the guarded value is an
    /// `Option` that a panic cannot leave half-written, and refusing to update a pin because of
    /// an unrelated panic would be refusing to revoke one.
    pub fn replace(&self, pin: Option<PinnedPanelCertificate>) {
        *self.current.write().unwrap_or_else(PoisonError::into_inner) = pin;
    }
}

#[tonic::async_trait]
impl PinSource for PinSlot {
    /// The pin as of this call.
    fn current(&self) -> Option<PinnedPanelCertificate> {
        self.current
            .read()
            .unwrap_or_else(PoisonError::into_inner)
            .clone()
    }

    /// Nothing to bring up to date: the slot is the pin's only copy, and whoever changes the
    /// pin changes the slot.
    async fn refresh(&self) {}
}
