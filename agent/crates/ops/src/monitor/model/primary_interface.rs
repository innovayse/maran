//! Which network interface this host's default route points out through —
//! spec §228's "primary interface", defined here because nothing else in the
//! tree defines it.

/// The interface carrying this host's IPv4 default route, or the typed fact
/// that there is none.
///
/// # Definition — stated here because §228 does not give one
///
/// "Primary interface" is not self-evident on a host with several NICs, so
/// this type states the definition it uses rather than leaving a reader to
/// guess one: the interface named by the **lowest-metric IPv4 default
/// route** — the row in `/proc/net/route`
/// (`crate::monitor::process_monitor_host::ProcessMonitorHost::read_ipv4_routes`)
/// whose `Destination` field is `00000000`, i.e. matches every address,
/// chosen by the same tie-break the kernel applies when several default
/// routes exist: lowest `Metric` wins. This mirrors what `ip route show
/// default` reports as the route a packet with no more specific match
/// leaves through — the intuitive "which NIC does this box talk to the
/// internet on" — without spawning `ip`: `/proc/net/route` is the kernel's
/// own interface, textually stable across both supported families, and
/// reading it needs no allow-listed binary (rules/security.md item 3).
/// `/proc/net/route`, like `/proc/net/dev` in `process_monitor_host.rs`, is
/// identical on both supported families and is therefore not a
/// `DistroAdapter` fact (rules/architecture.md "no platform fact appears as
/// a literal outside the `distro` crate" governs facts that DIFFER between
/// families; this one does not).
///
/// # What this definition does NOT cover, stated plainly
///
/// - **IPv6-only default routes are invisible to it.** A host reaching the
///   internet only over an IPv6 default route (`/proc/net/ipv6_route`, a
///   different and harder-to-parse format) reports [`Self::NotAvailable`]
///   here even though it plainly has outbound connectivity through one
///   interface. Out of scope for this slice; a future pass would need to
///   parse and merge both tables.
/// - **Equal-metric ties** are broken by the order `/proc/net/route` lists
///   them in — the kernel's own routing-table insertion order — which is not
///   a property this type or its caller controls or can promise is stable
///   across a reboot.
///
/// # Why this is an unstable second factor
///
/// This value can change on a machine nobody touched: a DHCP lease renewal
/// that hands out a different metric, a second NIC or a VPN/tunnel interface
/// coming up with a lower metric than the existing default, a cable moved
/// from one switch port to another, or simply the interface-naming order the
/// kernel assigns on a reboot after a driver or kernel update. None of those
/// events replace the machine, but every one of them can replace the answer
/// this type gives. A licence fingerprint built from this value alone will
/// occasionally lock out a customer who changed nothing about the machine
/// itself — the eventual fingerprint-comparison consumer of this value
/// SHOULD treat a primary-interface mismatch as materially weaker evidence
/// than a `machine-id` mismatch. Whether it is worth including as a second
/// factor at all, given how easily it moves, is a judgement call for that
/// consumer's design, not settled by this type.
///
/// # What may be recorded
///
/// Same rule as [`super::machine_identity::MachineIdentity`]: [`Self::Present`]'s
/// interface name identifies this machine's network configuration and MUST
/// NOT be logged or journalled verbatim (rules/security.md item 8).
#[derive(Debug, Clone, PartialEq, Eq)]
#[non_exhaustive]
pub enum PrimaryInterface {
    /// The name of the interface carrying the lowest-metric IPv4 default
    /// route, e.g. `"eth0"`.
    Present(String),
    /// No IPv4 default route exists in the kernel's routing table.
    NotAvailable,
}

/// The `Destination` field an IPv4 default route carries in `/proc/net/route`
/// — the whole address space, expressed as the kernel writes it: hex,
/// little-endian, zero.
const DEFAULT_DESTINATION: &str = "00000000";

/// Column index (0-based, after whitespace-splitting) of `/proc/net/route`'s
/// `Metric` field, per `route.c` in the Linux kernel's own `/proc/net/route`
/// formatter: `Iface Destination Gateway Flags RefCnt Use Metric Mask ...`.
const METRIC_COLUMN: usize = 6;

impl PrimaryInterface {
    /// Parses `/proc/net/route`'s text and picks the primary interface per
    /// the definition on this type.
    ///
    /// Pure function over text, like
    /// [`super::sftp_jail_status::SftpJailStatus::evaluate`]: the whole
    /// question here is answered by reading columns, which is what lets a
    /// test decide the input directly instead of reading the build machine's
    /// own routing table.
    #[must_use]
    pub fn from_ipv4_routes(routes: &str) -> Self {
        let mut best: Option<(u32, String)> = None;

        for line in routes.lines().skip(1) {
            let fields: Vec<&str> = line.split_whitespace().collect();
            let (Some(iface), Some(destination), Some(metric_field)) =
                (fields.first(), fields.get(1), fields.get(METRIC_COLUMN))
            else {
                continue;
            };

            if !destination.eq_ignore_ascii_case(DEFAULT_DESTINATION) {
                continue;
            }

            let metric = metric_field.parse::<u32>().unwrap_or(u32::MAX);
            let replace = match &best {
                Some((best_metric, _)) => metric < *best_metric,
                None => true,
            };
            if replace {
                best = Some((metric, (*iface).to_owned()));
            }
        }

        match best {
            Some((_, iface)) => Self::Present(iface),
            None => Self::NotAvailable,
        }
    }
}

#[cfg(test)]
#[path = "../../tests/monitor/primary_interface_tests.rs"]
mod tests;
