//! Seeding the local-submission maps an agent upgrade added.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::DistroAdapter;

use crate::mail::local_submission_tables::hold_tables;
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::seed_local_submission_maps::seed_local_submission_maps;

/// Creates the local-submission maps that an agent upgrade added and an already
/// mail-enabled host does not have yet, empty (issue #531).
///
/// The fan-out map arrived with #531: a host enabled by an earlier release has the
/// other four maps and the rendered setting that names the fifth, and the rule defers
/// every message while a map it reads is missing. Seeding it empty at the agent's
/// start closes that window without waiting for the panel's next push. A host that
/// never enabled mail (no accounts map) is left alone, and a map that exists is
/// never touched, for the reason [`seed_mail_maps`] gives.
///
/// Answers whether mail was found enabled.
///
/// # Errors
///
/// The errors of [`seed_mail_maps`].
pub fn seed_missing_local_submission_maps(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
) -> Result<bool, MailError> {
    let _tables = hold_tables();
    if host
        .read_config(Path::new(AgentPaths::MAIL_LOCAL_SUBMIT_ACCOUNTS_PATH))?
        .is_none()
    {
        return Ok(false);
    }
    seed_local_submission_maps(host, distro)?;
    Ok(true)
}

#[cfg(test)]
#[path = "../tests/mail/seed_missing_local_submission_maps_tests.rs"]
mod tests;
