//! Taking a zone's signing keys off the host.

use std::path::Path;

use maran_agent_core::validation::dns::dnssec_key_file_name::DnssecKeyFileName;
use maran_agent_core::validation::dns::zone_name::ZoneName;

use crate::dns::dns_error::DnsError;
use crate::dns::zone_host::ZoneHost;

/// Removes every key file of `zone` from `keys` — `.key`, `.private` and the
/// daemon's `.state` — except those of the key tagged `keep`.
///
/// `keep` is `None` for an UNSIGNED zone (and for a deleted one), where no key may
/// stay: a private key left on the host for a zone that no longer uses it is a
/// secret kept for nothing. It is `Some` when a zone is signed, so a key the panel
/// replaced does not stay beside the new one and have the daemon sign with both.
///
/// The directory is listed and each entry is parsed back through
/// [`DnssecKeyFileName::parse`], which accepts only the exact spelling the writer
/// produces: a stray file is left alone, and a file of another zone is never
/// removed because its name is a prefix of this one's. Removal is an unlink by
/// name, which removes a symlink and never follows it.
///
/// Idempotent: a directory with nothing to remove, or that is not there, is
/// success.
///
/// # Errors
///
/// Returns [`DnsError::ZoneDirectoryUnreadable`] when the directory exists and
/// cannot be listed and [`DnsError::ConfigWrite`] when a file cannot be removed.
pub(crate) fn remove_signing_keys(
    host: &dyn ZoneHost,
    keys: &Path,
    zone: &ZoneName,
    keep: Option<u16>,
) -> Result<(), DnsError> {
    for entry in host.zone_file_names(keys)? {
        let Ok(parsed) = DnssecKeyFileName::parse(&entry) else {
            continue;
        };
        if parsed.zone() != zone || Some(parsed.key_tag()) == keep {
            continue;
        }
        host.remove_file(&keys.join(&entry))?;
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/dns/remove_signing_keys_tests.rs"]
mod tests;
