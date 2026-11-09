//! Observing what the host is doing about mail.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::daemon_state::DaemonState;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::model::mail_state::MailState;
use crate::mail::model::mail_unit::MailUnit;
use crate::mail::port_bindings::port_bindings;
use std::path::Path;

/// Reads the mail stack's state from the machine.
///
/// Every answer is asked of the host and none is remembered: the service manager
/// says whether a unit is active, the kernel's socket table says which ports are
/// bound and to what, and `postconf` reads back the hostname and the certificate
/// setting from the files Postfix itself reads. The panel's own record of what it
/// asked for is not consulted, because a status that agreed with it whatever the
/// host did would be a check that cannot fail (rules/testing.md).
///
/// "Installed" is the presence of the program that proves it (`postfix`,
/// `doveconf`, `rspamadm`) or, for the key-value server, of the configuration file
/// the package ships, so that a daemon the installer could not put on the host
/// reads as missing instead of as stopped.
///
/// # Errors
///
/// Returns [`MailError::SpawnFailed`] when the service manager cannot be started
/// at all, [`MailError::Bound`] when it outruns its deadline, and
/// [`MailError::SocketsUnreadable`] when the kernel's table cannot be read. A
/// `postconf` that does not answer is a `None` field and not an error: a stopped
/// or half-installed Postfix is a state worth reporting.
pub fn get_mail_status(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    layout: &MailLayout,
) -> Result<MailState, MailError> {
    let manager = distro.service_manager();
    let mut daemons = Vec::with_capacity(MailDaemon::START_ORDER.len());
    for daemon in MailDaemon::START_ORDER {
        let (unit, marker) = match daemon {
            MailDaemon::KeyValueServer => (
                layout.key_value_server.service,
                Path::new(layout.key_value_server.config_file).to_path_buf(),
            ),
            MailDaemon::Rspamd => (
                distro.rspamd_service(),
                distro.rspamadm_binary().as_path().to_path_buf(),
            ),
            MailDaemon::Dovecot => (
                distro.dovecot_service(),
                distro.doveconf_binary().as_path().to_path_buf(),
            ),
            MailDaemon::Postfix => (
                distro.postfix_service(),
                distro.postfix_binary().as_path().to_path_buf(),
            ),
        };
        let active = host
            .run(&manager, &MailUnit::new(unit).is_active(), CallClass::Query)?
            .status
            == 0;
        daemons.push(DaemonState {
            daemon,
            installed: host.path_exists(&marker),
            active,
        });
    }

    let postfix_installed = daemons
        .iter()
        .any(|state| state.daemon == MailDaemon::Postfix && state.installed);
    let effective_hostname = if postfix_installed {
        read_postfix_parameter(host, distro, "myhostname")?.filter(|value| !value.is_empty())
    } else {
        None
    };
    let tls_configured = if postfix_installed {
        read_postfix_parameter(host, distro, "smtpd_tls_chain_files")?
            .map(|value| !value.is_empty())
    } else {
        None
    };

    Ok(MailState {
        daemons,
        ports: port_bindings(&host.listening_sockets()?),
        effective_hostname,
        tls_configured,
    })
}

/// Reads one Postfix parameter's effective value (`postconf -h <name>`), or `None`
/// when `postconf` did not answer. A parameter that is set to nothing is
/// `Some("")`, which is a different fact from not having been asked.
///
/// # Errors
///
/// Returns [`MailError::Bound`] when `postconf` outruns its deadline; a program
/// that cannot be started is `None` here, because the caller already knows from
/// the installed marker whether it should be there.
fn read_postfix_parameter(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    name: &str,
) -> Result<Option<String>, MailError> {
    match host.run(&distro.postconf_binary(), &["-h", name], CallClass::Query) {
        Ok(outcome) if outcome.status == 0 => Ok(Some(outcome.stdout.trim().to_owned())),
        Ok(_) | Err(MailError::SpawnFailed { .. }) => Ok(None),
        Err(other) => Err(other),
    }
}

#[cfg(test)]
#[path = "../tests/mail/get_mail_status_tests.rs"]
mod tests;
