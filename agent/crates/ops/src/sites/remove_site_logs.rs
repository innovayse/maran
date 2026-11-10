//! Removing a site's log files when the site is deleted (issue #477).

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use crate::sites::SitesOpError;
use crate::sites::is_site_log_name::is_site_log_name;
use crate::sites::site_log_host::SiteLogHost;

/// Removes `<domain>.access.log`, `<domain>.error.log` and every rotated
/// generation of both from the account's log directory, and returns how many
/// files it removed.
///
/// The logs are named by the account and the domain, and the panel recycles both:
/// the next holder of the same pair opens `<domain>.access.log` through the log
/// tail and the traffic summary whatever its age, and nginx appends to a file that
/// exists. Leaving them behind handed the previous tenant's visitor addresses,
/// request targets and referrers to the next one, and kept personal data past the
/// erasure a deletion promises. Called after the site's vhost is gone and nginx
/// has been reloaded, so nothing new is being written.
///
/// What is removed is decided by `is_site_log_name` alone, and only regular
/// files, by name, through the one held directory descriptor (the host's
/// discipline, as for `prune_site_logs`); another site's files, an operator's own
/// file, a directory and a symlink are left where they are. An account directory
/// that does not exist holds nothing to remove and is success.
///
/// Idempotent: a second call finds nothing and removes nothing. A cancellation
/// class of **run to completion**: removing a file has no unsafe midpoint and a
/// repeat finishes what a stop left.
///
/// # Errors
///
/// Returns [`SitesOpError::LogUnreadable`] when the directory is not root's or
/// cannot be listed, or a matching regular file cannot be removed.
pub fn remove_site_logs(
    host: &dyn SiteLogHost,
    account: &AccountName,
    domain: &Domain,
) -> Result<u32, SitesOpError> {
    let Some(directory) = host.open_directory_if_present(account)? else {
        return Ok(0);
    };
    let mut removed_files = 0_u32;

    for name in directory.list_names()? {
        if is_site_log_name(&name, domain) && directory.remove_file(&name)? {
            removed_files = removed_files.saturating_add(1);
        }
    }

    Ok(removed_files)
}

#[cfg(test)]
#[path = "../tests/sites/remove_site_logs_tests.rs"]
mod tests;
