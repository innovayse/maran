//! Removing a site's rotated logs beyond its retention.

use crate::sites::SitesOpError;
use crate::sites::model::prune_logs_input::PruneLogsInput;
use crate::sites::model::pruned_logs::PrunedLogs;
use crate::sites::rotated_log_name::rotated_log_name;
use crate::sites::site_log_host::SiteLogHost;

/// Removes the rotated archives of `input.domain` numbered above
/// `input.retain`, access and error alike, compressed or not.
///
/// The shared logrotate policy (`installer/logrotate/maran-sites`) keeps
/// fourteen and stays the hard bound; this only lets a site keep fewer. What it
/// removes is decided by `rotated_log_name` alone — the whole entry name must
/// be exactly one of this site's rotated logs — and the live file, generation
/// `0`, another site's files, an operator's own file in the directory and
/// anything that is not a regular file are left where they are and are not an
/// error.
///
/// Idempotent: a second call finds nothing numbered above the retention and
/// removes nothing. A cancellation class of **run to completion**: removing a
/// file has no unsafe midpoint and a repeat finishes what a stop left.
///
/// # Errors
///
/// Returns [`SitesOpError::LogUnreadable`] when the directory is not root's or
/// cannot be listed, or a matching regular file cannot be removed.
pub fn prune_site_logs(
    host: &dyn SiteLogHost,
    input: &PruneLogsInput,
) -> Result<PrunedLogs, SitesOpError> {
    let directory = host.open_directory(&input.account)?;
    let mut removed_files = 0_u32;

    for name in directory.list_names()? {
        let Some(generation) = rotated_log_name(&name, &input.domain) else {
            continue;
        };

        if generation > input.retain.value() && directory.remove_file(&name)? {
            removed_files = removed_files.saturating_add(1);
        }
    }

    Ok(PrunedLogs { removed_files })
}

#[cfg(test)]
#[path = "../tests/sites/prune_site_logs_tests.rs"]
mod tests;
