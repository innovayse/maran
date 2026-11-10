//! Which zones have a complete signing key on the host.

use std::collections::BTreeSet;
use std::path::Path;

use maran_agent_core::validation::dns::dnssec_key_file_name::DnssecKeyFileName;

use crate::dns::dns_error::DnsError;
use crate::dns::zone_host::ZoneHost;

/// The suffix of the private key file.
const PRIVATE_SUFFIX: &str = ".private";

/// The suffix of the public key file.
const PUBLIC_SUFFIX: &str = ".key";

/// The names (apex text) of the zones for which `keys` holds a complete key: the
/// same `K<zone>.+013+<tag>` with BOTH a `.private` and a `.key` file.
///
/// Read from the directory listing and parsed through [`DnssecKeyFileName::parse`],
/// which accepts only the spelling the writer produces; a stray file counts for
/// nothing. A directory that is not there has no keys. Only names are read: no key
/// file is opened.
///
/// # Errors
///
/// Returns [`DnsError::ZoneDirectoryUnreadable`] when the directory exists and
/// cannot be listed.
pub(crate) fn keyed_zones(host: &dyn ZoneHost, keys: &Path) -> Result<BTreeSet<String>, DnsError> {
    let mut private = BTreeSet::new();
    let mut public = BTreeSet::new();

    for entry in host.zone_file_names(keys)? {
        let Ok(parsed) = DnssecKeyFileName::parse(&entry) else {
            continue;
        };
        let identity = (parsed.zone().as_str().to_owned(), parsed.key_tag());
        if entry.ends_with(PRIVATE_SUFFIX) {
            private.insert(identity);
        } else if entry.ends_with(PUBLIC_SUFFIX) {
            public.insert(identity);
        }
    }

    Ok(private
        .intersection(&public)
        .map(|(zone, _)| zone.clone())
        .collect())
}

#[cfg(test)]
#[path = "../tests/dns/keyed_zones_tests.rs"]
mod tests;
