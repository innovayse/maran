//! Bytes the host's interfaces have carried since boot.

/// The character separating an interface's name from its counters.
const NAME_SEPARATOR: char = ':';

/// The loopback interface, which carries no traffic to or from anywhere.
const LOOPBACK: &str = "lo";

/// Field index, among an interface's counters, of the bytes it received.
const RECEIVED_FIELD: usize = 0;

/// Field index of the bytes it transmitted.
///
/// The receive half is eight fields wide — bytes, packets, errs, drop, fifo,
/// frame, compressed, multicast — so the transmit byte count is the ninth
/// number on the line.
const TRANSMITTED_FIELD: usize = 8;

/// How many numbers a line must hold before it is believed.
const MINIMUM_FIELDS: usize = TRANSMITTED_FIELD + 1;

/// What every interface of this host has carried since boot.
///
/// Counters and not rates (plan ruling R7): the agent is sampled at whatever
/// interval the panel manages, gaps included, and only the panel knows the real
/// time between two samples. A rate computed here from an assumed interval
/// would be wrong by exactly the amount the panel could have corrected for.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NetworkCounters {
    /// Bytes received since boot, across every interface but the loopback.
    pub received_bytes: u64,
    /// Bytes transmitted since boot, across every interface but the loopback.
    pub transmitted_bytes: u64,
}

impl NetworkCounters {
    /// Sums the kernel's per-interface byte counters, skipping the loopback.
    ///
    /// **The loopback is not network traffic.** Every request nginx forwards to
    /// php-fpm, every query the panel sends to its own database and every byte
    /// of a local backup crosses `lo`, and counting it would report a host with
    /// no visitors at all as busy — and would double-count every real request,
    /// which arrives on a real interface and is then proxied over the loopback.
    ///
    /// Interfaces that are not the loopback are all summed, virtual ones
    /// included. On a hosting server that is the honest answer; on a machine
    /// that also bridges containers, traffic bridged between them is counted
    /// once per bridge end. Naming that here rather than filtering on a guess
    /// about interface names, which would silently drop a second real NIC.
    ///
    /// The two header lines carry no `:` in a leading name position and fall
    /// away with every other unparsable line. A line whose counter has grown
    /// into the colon — `eth0:1234567890` with no space, which the kernel emits
    /// on a long-lived interface — is still read, because the name is split off
    /// at the colon before the numbers are taken.
    ///
    /// Returns `None` only when the text names no interface at all; an
    /// individual line that cannot be read is skipped, since one malformed line
    /// is not a reason to stop reporting the rest of the machine's traffic.
    #[must_use]
    pub fn parse(net_dev: &str) -> Option<Self> {
        let mut counters = Self {
            received_bytes: 0,
            transmitted_bytes: 0,
        };
        let mut seen = false;

        for line in net_dev.lines() {
            let Some((name, values)) = line.split_once(NAME_SEPARATOR) else {
                continue;
            };

            let name = name.trim();
            if name.is_empty() || name.contains(char::is_whitespace) {
                continue;
            }

            // Taken and parsed as a block rather than filtered: a token that is
            // not a number would otherwise be dropped and every field after it
            // would shift one place left, which reads a packet count as a byte
            // count instead of reporting that the line was not understood.
            let Some(fields) = values
                .split_whitespace()
                .take(MINIMUM_FIELDS)
                .map(|field| field.parse::<u64>().ok())
                .collect::<Option<Vec<u64>>>()
            else {
                continue;
            };
            if fields.len() < MINIMUM_FIELDS {
                continue;
            }

            seen = true;
            if name == LOOPBACK {
                continue;
            }

            counters.received_bytes = counters
                .received_bytes
                .saturating_add(*fields.get(RECEIVED_FIELD)?);
            counters.transmitted_bytes = counters
                .transmitted_bytes
                .saturating_add(*fields.get(TRANSMITTED_FIELD)?);
        }

        seen.then_some(counters)
    }
}

#[cfg(test)]
#[path = "../../tests/monitor/network_counters_tests.rs"]
mod tests;
