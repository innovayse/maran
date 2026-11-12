//! The check itself: read once, tell every layer that is behind.

use maran_agent_core::utils::host_addresses::HostAddresses;

use crate::address_watch::host_address_layer::HostAddressLayer;
use crate::address_watch::host_address_layer_outcome::HostAddressLayerOutcome;
use crate::address_watch::host_address_source::HostAddressSource;
use crate::address_watch::watch_tick::WatchTick;

/// The state of the periodic check: where addresses come from, the layers that
/// follow them, and what each layer was last brought to.
pub(crate) struct HostAddressWatch {
    /// The one place the addresses are read from.
    source: Box<dyn HostAddressSource>,
    /// The layers, each with the addresses it was last brought to (`None` until it
    /// has been, which is why the first tick after the start follows).
    layers: Vec<(Box<dyn HostAddressLayer>, Option<HostAddresses>)>,
}

impl HostAddressWatch {
    /// A watch over `layers` that reads from `source`.
    pub(crate) fn new(
        source: Box<dyn HostAddressSource>,
        layers: Vec<Box<dyn HostAddressLayer>>,
    ) -> Self {
        Self {
            source,
            layers: layers.into_iter().map(|layer| (layer, None)).collect(),
        }
    }

    /// Reads the host's addresses once and asks every layer that does not hold
    /// them to follow.
    ///
    /// **One reading, every layer.** The two layers of the lockdown are handed the
    /// same value, so they cannot follow different hosts. Readings are compared
    /// sorted and de-duplicated, so the kernel listing the same host in another
    /// order is not a change.
    ///
    /// **Unreadable is not empty.** A reading that fails is logged and nothing is
    /// written; the layers keep what they hold and the next tick tries again.
    ///
    /// **A failing layer is retried alone.** It is recorded as brought to an
    /// address set only when it really followed, so a layer that failed is asked again
    /// at the next tick and a layer that followed is not asked twice. This never
    /// panics and never ends the loop: every failure is a log line.
    ///
    /// **"Nothing to do" is not "done".** A layer that answers
    /// [`HostAddressLayerOutcome::Pending`] (no firewall loaded yet, Postfix stopped)
    /// is NOT recorded either: it is asked again at the next tick, which is cheap, so
    /// the moment it can follow it does, and a lockdown layer is never reported
    /// current on the strength of a tick that wrote nothing to it.
    pub(crate) fn tick(&mut self) -> WatchTick {
        let current = match self.source.read() {
            Ok(addresses) => addresses.normalized(),
            Err(error) => {
                tracing::error!(
                    %error,
                    "the host's addresses could not be read, so the port-25 lockdown keeps the \
                     addresses it has; trying again at the next interval"
                );
                return WatchTick::SourceUnreadable;
            }
        };

        let mut asked = 0;
        let mut failed = 0;
        let mut pending = 0;
        for (layer, brought_to) in &mut self.layers {
            if brought_to.as_ref() == Some(&current) {
                continue;
            }
            asked += 1;
            match layer.follow(&current) {
                Ok(HostAddressLayerOutcome::Followed) => *brought_to = Some(current.clone()),
                Ok(HostAddressLayerOutcome::Pending) => pending += 1,
                Err(error) => {
                    failed += 1;
                    tracing::error!(
                        layer = layer.name(),
                        %error,
                        "a layer of the port-25 lockdown could not follow the host's addresses; \
                         it keeps the addresses it has and is tried again at the next interval"
                    );
                }
            }
        }

        if asked == 0 {
            WatchTick::NothingChanged
        } else {
            WatchTick::Followed {
                asked,
                failed,
                pending,
            }
        }
    }
}

#[cfg(test)]
#[path = "../tests/address_watch/host_address_watch_tests.rs"]
mod tests;
