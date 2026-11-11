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

/// What a unit's `Description=` line says before the major, as the site unit template writes it
/// (`templates/systemd/node_site_unit.service.j2`): `Maran Node.js <major> application for ...`.
/// A test renders the template and reads the major back, so the two cannot drift apart.
const DESCRIPTION_PREFIX: &str = "Description=Maran Node.js ";

/// Whether `file_name`, an entry of the unit directory, is the unit of some Node.js site.
#[must_use]
pub(crate) fn is_node_unit_file_name(file_name: &str) -> bool {
    file_name.starts_with(UNIT_NAME_PREFIX) && file_name.ends_with(UNIT_NAME_SUFFIX)
}

/// The Node.js major `unit_text` declares in its `Description=` line, or `None` for text that
/// states none (a unit edited by hand, a different template).
///
/// The agent reads this back to learn which sites still run on a runtime; `None` is not "no
/// major", it is "cannot tell", and a caller that decides about removal treats it as a user.
#[must_use]
pub(crate) fn declared_node_major(unit_text: &str) -> Option<&str> {
    let rest = unit_text
        .lines()
        .find_map(|line| line.strip_prefix(DESCRIPTION_PREFIX))?;
    let major = rest.split_whitespace().next()?;
    major
        .bytes()
        .all(|byte| byte.is_ascii_digit())
        .then_some(major)
}

#[cfg(test)]
#[path = "../tests/sites/node_unit_path_tests.rs"]
mod tests;
