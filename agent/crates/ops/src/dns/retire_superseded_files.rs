//! Removing what a zone's new signing mode leaves behind.

use maran_agent_core::validation::dns::zone_file_name::ZoneFileName;
use maran_agent_core::validation::dns::zone_name::ZoneName;

use crate::dns::dns_error::DnsError;
use crate::dns::model::zone_directories::ZoneDirectories;
use crate::dns::model::zone_signing::ZoneSigning;
use crate::dns::remove_signed_copy::remove_signed_copy;
use crate::dns::remove_signing_keys::remove_signing_keys;
use crate::dns::zone_host::ZoneHost;

/// Removes the files of `zone` that `signing` makes obsolete, after the daemon
/// has been told to load the new ones.
///
/// - UNSIGNED: every key file and the signed directory's copy of the zone, which
///   the daemon no longer uses. This is the only path that takes a private key off
///   the host short of deleting the zone.
/// - SIGNED or WITHDRAWING: the plain directory's copy of the zone (the zone moved
///   into the signed directory) and any key file of a different tag.
///
/// It runs AFTER the load because the daemon is serving from the old files until
/// then; run before, a failed load would leave a zone with no file. A failure in
/// here leaves obsolete files and an error, and the same `PutZone` repeated
/// converges, because every removal is idempotent.
///
/// # Errors
///
/// Returns [`DnsError::ZoneDirectoryUnreadable`] when the key directory cannot be
/// listed and [`DnsError::ConfigWrite`] when a file cannot be removed.
pub(crate) fn retire_superseded_files(
    host: &dyn ZoneHost,
    directories: &ZoneDirectories,
    zone: &ZoneName,
    file: &ZoneFileName,
    signing: &ZoneSigning,
) -> Result<(), DnsError> {
    match signing.key() {
        None => {
            remove_signing_keys(host, directories.keys(), zone, None)?;
            remove_signed_copy(host, directories.signed_zones(), file)
        }
        Some(key) => {
            remove_signing_keys(host, directories.keys(), zone, Some(key.key_tag()))?;
            host.remove_file(&directories.zones().join(file.as_str()))
        }
    }
}

#[cfg(test)]
#[path = "../tests/dns/retire_superseded_files_tests.rs"]
mod tests;
