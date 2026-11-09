//! Making running mail daemons pick up certificate material replaced underneath them.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::mail::get_mail_status::get_mail_status;
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::model::mail_ports::MailPorts;
use crate::mail::model::mail_state::MailState;
use crate::mail::model::mail_unit::MailUnit;
use crate::mail::wait_for_mail_ports::wait_for_mail_ports;

/// Reloads Postfix and Dovecot, the two daemons that hold the certificate, then
/// checks that their ports are still bound.
///
/// # A stopped daemon is a no-op, not an error
///
/// A daemon that is off reads the new material at its next start by construction,
/// and "reload TLS" must never be the thing that turns a deliberately disabled
/// stack on: an administrator who disabled mail and then renewed a site's
/// certificate would otherwise find the mail ports open again with no screen
/// claiming to have opened them. Only a unit that is active is reloaded.
///
/// The spam filter and the key-value server hold no certificate and are left
/// alone.
///
/// `reload-or-restart` rather than a bare reload: both daemons re-read their
/// certificate on a reload, and the one verb also covers a unit that died between
/// the check and the call.
///
/// # Errors
///
/// Returns [`MailError::ServiceRefused`] when the service manager refuses, or the
/// unit is not active afterwards, and [`MailError::NotListening`] when a daemon is
/// active and its ports stay unbound for the whole bounded wait — the shape a
/// certificate whose key does not match produces. Returns
/// [`MailError::SpawnFailed`], [`MailError::Bound`] and
/// [`MailError::SocketsUnreadable`] for the mechanical failures they name.
pub fn reload_mail_tls(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    version_id: &str,
) -> Result<MailState, MailError> {
    let layout = MailLayout::resolve(distro, version_id)?;
    let manager = distro.service_manager();
    let before = get_mail_status(host, distro, &layout)?;
    let mut reloaded_ports: Vec<u16> = Vec::new();

    for (daemon, name, ports) in [
        (
            MailDaemon::Dovecot,
            distro.dovecot_service(),
            MailPorts::DOVECOT.as_slice(),
        ),
        (
            MailDaemon::Postfix,
            distro.postfix_service(),
            MailPorts::POSTFIX.as_slice(),
        ),
    ] {
        let running = before
            .daemons
            .iter()
            .any(|state| state.daemon == daemon && state.active);
        if !running {
            continue;
        }
        let unit = MailUnit::new(name);
        let outcome = host.run(&manager, &unit.reload_or_restart(), CallClass::Reload)?;
        if outcome.status != 0 {
            return Err(MailError::ServiceRefused {
                unit: name.to_owned(),
            });
        }
        reloaded_ports.extend_from_slice(ports);
    }

    wait_for_mail_ports(host, &reloaded_ports)?;
    get_mail_status(host, distro, &layout)
}

#[cfg(test)]
#[path = "../tests/mail/reload_mail_tls_tests.rs"]
mod tests;
