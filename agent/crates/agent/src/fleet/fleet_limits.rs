//! The bounds on the fleet listener's handshakes.

use std::time::Duration;

/// How long a peer is given to finish its TLS handshake.
///
/// Ten seconds is generous for a handshake on a working network and short enough that a peer
/// that connects and says nothing holds a task, a descriptor and a permit for a moment and not
/// for as long as the kernel keeps an idle socket.
const STANDARD_HANDSHAKE_TIMEOUT: Duration = Duration::from_secs(10);

/// How many handshakes may be in flight at once.
///
/// One legitimate peer, the panel, opens a handful of long-lived connections. Sixty-four is far
/// above that and far below the descriptor limit, so a flood is refused at this number long
/// before it can starve the unix listener of descriptors.
const STANDARD_MAX_CONCURRENT_HANDSHAKES: usize = 64;

/// The two bounds that keep the fleet listener from being a way to exhaust the agent.
///
/// tonic's built-in TLS has neither: every accepted socket spawns a handshake task that can wait
/// for ever, with no count. Both are injectable so a test drives them with a few hundred
/// milliseconds and a cap of one instead of waiting out ten seconds and opening sixty-five
/// sockets.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct FleetLimits {
    /// The most time one handshake may take, counted from the accepted socket.
    handshake_timeout: Duration,
    /// The most handshakes allowed to run at the same moment. A connection arriving while the
    /// count is full is closed without a handshake.
    max_concurrent_handshakes: usize,
}

impl FleetLimits {
    /// The bounds the daemon runs with.
    #[must_use]
    pub fn standard() -> Self {
        Self::new(
            STANDARD_HANDSHAKE_TIMEOUT,
            STANDARD_MAX_CONCURRENT_HANDSHAKES,
        )
    }

    /// Bounds of the caller's choosing; tests use small ones.
    #[must_use]
    pub fn new(handshake_timeout: Duration, max_concurrent_handshakes: usize) -> Self {
        Self {
            handshake_timeout,
            max_concurrent_handshakes,
        }
    }

    /// The most time one handshake may take.
    #[must_use]
    pub fn handshake_timeout(&self) -> Duration {
        self.handshake_timeout
    }

    /// The most handshakes allowed at once.
    #[must_use]
    pub fn max_concurrent_handshakes(&self) -> usize {
        self.max_concurrent_handshakes
    }
}
