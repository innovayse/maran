//! Stopping every mail daemon and keeping it stopped.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::model::mail_unit::MailUnit;

/// Stops, disables and masks every mail daemon, in the reverse of start order: the
/// SMTP server that feeds the others goes first, the key-value server they use
/// last.
///
/// Masking is the second half and not decoration. A packaged unit that is merely
/// stopped is started again by the next package upgrade's post-install script on
/// the Debian family, which would open the mail ports with nobody having asked.
/// The installer masked these units; this puts the mask back.
///
/// Every daemon is attempted even when an earlier one refuses, so one stuck unit
/// does not leave the rest listening; the first refusal is returned.
///
/// # Errors
///
/// Returns [`MailError::ServiceRefused`] naming the first unit the service manager
/// refused to disable or mask, [`MailError::SpawnFailed`] when it cannot be
/// started, and [`MailError::Bound`] when it outruns its deadline.
pub(crate) fn stop_mail_daemons(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    layout: &MailLayout,
) -> Result<(), MailError> {
    let manager = distro.service_manager();
    let mut first_failure = None;
    for daemon in MailDaemon::START_ORDER.into_iter().rev() {
        let name = match daemon {
            MailDaemon::KeyValueServer => layout.key_value_server.service,
            MailDaemon::Rspamd => distro.rspamd_service(),
            MailDaemon::Dovecot => distro.dovecot_service(),
            MailDaemon::Postfix => distro.postfix_service(),
        };
        let unit = MailUnit::new(name);
        let results = [
            host.run(&manager, &unit.disable_now(), CallClass::Reload),
            host.run(&manager, &unit.mask(), CallClass::Reload),
        ];
        for result in results {
            let failure = match result {
                Ok(outcome) if outcome.status == 0 => continue,
                Ok(_) => MailError::ServiceRefused {
                    unit: name.to_owned(),
                },
                Err(error) => error,
            };
            first_failure.get_or_insert(failure);
        }
    }
    first_failure.map_or(Ok(()), Err)
}

#[cfg(test)]
#[path = "../tests/mail/stop_mail_daemons_tests.rs"]
mod tests;
