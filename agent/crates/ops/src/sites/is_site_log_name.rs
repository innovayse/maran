//! Which directory entries are one site's log files, live or rotated.

use maran_agent_core::validation::web::domain::Domain;

use crate::sites::rotated_log_name::rotated_log_name;

/// Whether `entry` is exactly one of `domain`'s log files: the live
/// `<domain>.access.log` and `<domain>.error.log`, or one of their rotated
/// generations (`rotated_log_name`).
///
/// The whole entry name is compared, so another site's files are never matched:
/// the site `a.com.access` writes `a.com.access.access.log`, which is neither
/// `a.com.access.log` nor `a.com.error.log`, and the site `a.com.access.log`
/// writes `a.com.access.log.access.log`, which `rotated_log_name` refuses at its
/// first non-digit. There is no pattern or wildcard here to match more than the
/// names say.
#[must_use]
pub(crate) fn is_site_log_name(entry: &str, domain: &Domain) -> bool {
    let live = ["access", "error"]
        .iter()
        .any(|kind| entry == format!("{}.{kind}.log", domain.as_str()));

    live || rotated_log_name(entry, domain).is_some()
}

#[cfg(test)]
#[path = "../tests/sites/is_site_log_name_tests.rs"]
mod tests;
