//! Making the refused-client table follow the host's addresses.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_distro::DistroAdapter;

use crate::mail::build_mail_stages::CONFIG_MODE;
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::mail_lifecycle_lock::hold_mail_lifecycle;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_unit::MailUnit;
use crate::mail::model::self_clients_outcome::SelfClientsOutcome;
use crate::mail::render_self_clients::render_self_clients;
use crate::safe_write::model::config_file::ConfigFile;
use crate::safe_write::model::{Reload, Validator};

/// Brings the table the port 25 service refuses to the host's present addresses, and
/// has a RUNNING Postfix read it.
///
/// The table is rendered by the same [`render_self_clients`] that `enable_mail`
/// renders it with, from the same [`HostAddresses`] reading the firewall's address
/// sets are filled from, so the two layers of the port-25 lockdown follow one
/// host. Only that one file is written, through the config-write protocol, and the
/// table is judged by Postfix's own check (`postfix check`) either way.
///
/// **A running Postfix** gets the table through the full protocol: rename, check,
/// reload, and the old table put back when either refuses.
///
/// **A stopped Postfix** (mail disabled, or down for maintenance) gets the table written
/// and checked and NOT reloaded, and the answer is [`SelfClientsOutcome::NotRunning`]:
/// whoever starts Postfix next, `enable_mail` or an operator's `systemctl start`, starts it
/// from the current addresses and not from the table a gained address was missing from.
/// The outcome says the layer was not brought to the addresses by a reload, so the caller
/// does not record it as followed and asks again at the next interval.
///
/// **Never a start.** The reload is the plain `reload`, which the service manager refuses
/// for a stopped unit, and not `reload-or-restart`, which would START it: an administrator
/// who disabled mail must not see port 25 listening because an address changed. The
/// running check and the reload are also one critical section with `enable_mail` and
/// `disable_mail` (the mail lifecycle lock), so a disable cannot land between them; a
/// Postfix that stops for any other reason in that window makes the reload fail, the table
/// is put back and the error is returned, and nothing is started.
///
/// Nothing is written, and Postfix is not reloaded, when the table already holds the
/// rendered bytes. A host that never enabled mail has no table and is left alone.
///
/// # Calling this
///
/// Synchronous; invoke it from `tokio::task::spawn_blocking`: it takes the mail
/// lifecycle lock.
///
/// # Errors
///
/// Returns [`MailError::Render`] when the template and its type drifted apart,
/// [`MailError::ConfigUnreadable`] when the table exists and cannot be read,
/// [`MailError::ConfigRejected`] when Postfix's check refuses the table (it is put
/// back), [`MailError::ServiceRefused`] when the reload is refused (it is put back), and
/// the protocol's other failures as [`MailError::ConfigWrite`],
/// [`MailError::SpawnFailed`] and [`MailError::Bound`].
pub fn refresh_self_clients(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    addresses: &HostAddresses,
) -> Result<SelfClientsOutcome, MailError> {
    let _lifecycle = hold_mail_lifecycle();

    let wanted = render_self_clients(addresses)?;
    let target = Path::new(AgentPaths::MAIL_SELF_CLIENTS_PATH);

    let Some(current) = host.read_config(target)? else {
        return Ok(SelfClientsOutcome::NotEnabled);
    };

    let manager = distro.service_manager();
    let unit = MailUnit::new(distro.postfix_service());
    let active = host.run(&manager, &unit.is_active(), CallClass::Query)?;
    let validator_program = distro.postfix_binary();
    let validator = Validator {
        program: &validator_program,
        arguments: &["check"],
        class: CallClass::Validate,
    };
    let file = ConfigFile {
        target,
        contents: &wanted,
        mode: CONFIG_MODE,
    };

    if active.status != 0 {
        if current != wanted {
            host.write_file_unreloaded(MailDaemon::Postfix, unit.name(), &file, &validator)?;
        }

        return Ok(SelfClientsOutcome::NotRunning);
    }
    if current == wanted {
        return Ok(SelfClientsOutcome::Unchanged);
    }

    host.write_config_set(
        MailDaemon::Postfix,
        unit.name(),
        &[file],
        &validator,
        &Reload {
            program: &manager,
            arguments: &unit.reload(),
        },
    )?;

    Ok(SelfClientsOutcome::Rewritten)
}

#[cfg(test)]
#[path = "../tests/mail/refresh_self_clients_tests.rs"]
mod tests;
