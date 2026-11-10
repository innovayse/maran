//! Taking a zone out of the signed-zone directory.

use std::path::Path;

use maran_agent_core::validation::dns::zone_file_name::ZoneFileName;

use crate::dns::dns_error::DnsError;
use crate::dns::zone_host::ZoneHost;

/// What follows a signed zone's file name in the files the daemon writes beside
/// it: the file itself, its journal and the journal's backup, the signed copy and
/// that copy's journal and backup.
///
/// A list of names to try, not a pattern to match against a listing: an unlink of
/// a name that is not there is success, so a build that writes fewer of them costs
/// nothing, and no listing of a directory the daemon can write decides what is
/// removed.
const SUFFIXES: [&str; 6] = ["", ".jnl", ".jbk", ".signed", ".signed.jnl", ".signed.jbk"];

/// Removes the zone file `file` from `signed_zones` together with the files the
/// daemon's inline signing keeps beside it.
///
/// Called when a zone leaves signing (it is UNSIGNED, or deleted), after the daemon
/// has stopped using the file. Each name is the validated file name with a fixed
/// suffix, removed by unlink, which never follows a symlink.
///
/// Idempotent: names that are not there are success.
///
/// # Errors
///
/// Returns [`DnsError::ConfigWrite`] when a file is there and cannot be removed.
pub(crate) fn remove_signed_copy(
    host: &dyn ZoneHost,
    signed_zones: &Path,
    file: &ZoneFileName,
) -> Result<(), DnsError> {
    for suffix in SUFFIXES {
        host.remove_file(&signed_zones.join(format!("{}{suffix}", file.as_str())))?;
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/dns/remove_signed_copy_tests.rs"]
mod tests;
