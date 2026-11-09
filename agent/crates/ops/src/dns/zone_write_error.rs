//! Mapping the config-write protocol's failure onto this area's list for a zone.

use crate::dns::dns_error::DnsError;
use crate::safe_write::SafeWriteError;

/// Maps a failure of the config-write protocol, run with the zone checker as the
/// validator and an `rndc` command as the reload, onto [`DnsError`].
///
/// Not [`DnsError::from_config_write`]: there the reload slot holds a unit
/// restart and a refusal means "the service manager refused"; here it holds the
/// daemon's control channel, and a refusal means the daemon would not load or
/// add the zone, which is a different answer for an operator.
#[must_use]
pub(crate) fn zone_write_error(error: SafeWriteError) -> DnsError {
    match error {
        SafeWriteError::ValidationFailed { stderr } => DnsError::ZoneRejected { output: stderr },
        SafeWriteError::ReloadFailed { stderr } => DnsError::ControlRefused { output: stderr },
        SafeWriteError::SpawnFailed { .. } => DnsError::program_unavailable(),
        SafeWriteError::Bound(bound) => DnsError::Bound(bound),
        other => DnsError::ConfigWrite {
            reason: other.to_string(),
        },
    }
}

#[cfg(test)]
#[path = "../tests/dns/zone_write_error_tests.rs"]
mod tests;
