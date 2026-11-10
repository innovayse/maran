//! Where one Node.js site's systemd unit lives, and what it is called.

use std::path::PathBuf;

use maran_agent_core::validation::web::domain::Domain;
use maran_distro::DistroAdapter;

/// The prefix of every unit this area writes for a Node.js site; the primary domain follows.
const UNIT_NAME_PREFIX: &str = "maran-node-";

/// The suffix of a service unit.
const UNIT_NAME_SUFFIX: &str = ".service";

/// The name of the unit that runs `domain`'s application.
///
/// Named after the primary domain, which is unique per site and whose alphabet (letters,
/// digits, `-` and `.`) is one a unit name allows, so the name needs no escaping and cannot
/// carry a path separator.
#[must_use]
pub(crate) fn node_unit_name(domain: &Domain) -> String {
    format!("{UNIT_NAME_PREFIX}{}{UNIT_NAME_SUFFIX}", domain.as_str())
}

/// The file the unit is written to, in the directory the distribution adapter names.
#[must_use]
pub(crate) fn node_unit_path(distro: &dyn DistroAdapter, domain: &Domain) -> PathBuf {
    PathBuf::from(distro.systemd_unit_directory()).join(node_unit_name(domain))
}
