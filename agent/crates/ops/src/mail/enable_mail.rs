//! Turning mail on: render, check, start in dependency order, observe.

use std::path::Path;

use maran_agent_core::validation::web::domain::Domain;
use maran_distro::DistroAdapter;

use crate::mail::build_mail_stages::build_mail_stages;
use crate::mail::converge_mail_stage::converge_mail_stage;
use crate::mail::get_mail_status::get_mail_status;
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::model::mail_ports::MailPorts;
use crate::mail::model::mail_settings::MailSettings;
use crate::mail::model::mail_state::MailState;
use crate::mail::postfix_uid_from_passwd::postfix_uid_from_passwd;
use crate::mail::seed_mail_maps::seed_mail_maps;
use crate::mail::stop_mail_daemons::stop_mail_daemons;
use crate::mail::wait_for_mail_ports::wait_for_mail_ports;

/// Configures and starts this host's mail stack: the key-value server, the spam
/// filter, Dovecot and Postfix, in that order.
///
/// `version_id` is the os-release `VERSION_ID` the daemon read at start; it picks
/// the Dovecot configuration generation and the key-value server, which differ by
/// release.
///
/// # What it refuses before changing anything
///
/// It asks what certificate material is installed for the hostname and returns
/// [`MailError::CertificateMissing`] when there is none: the mail stack never
/// writes into the certificate store, and a stack that generated its own
/// placeholder would put a certificate every client warns about behind forced
/// TLS. It then requires every daemon's program to be on the host and returns
/// [`MailError::NotInstalled`] naming the first that is not — the installer
/// installs what the distribution carries and says so for what it cannot.
///
/// # Idempotent
///
/// Applying settings the host already has, with every unit active, writes nothing
/// and restarts nothing: [`converge_mail_stage`] judges the files and the daemon
/// together. Only a stage whose files differ, or whose unit is not running, is
/// written and started.
///
/// # What a failure leaves behind
///
/// The config-write protocol puts back the files of the stage that failed. When
/// NO daemon was active before the call, every daemon is then stopped and masked
/// again, so a failed first enable leaves the host as the installer left it —
/// nothing listening. When some daemon WAS active, the earlier stages are left as
/// they are, valid and running, because stopping a working mail server over a
/// refused change would turn a rejected edit into an outage.
///
/// # What no check can see
///
/// An edit made to a live file AFTER the call. The checks read the files this
/// call wrote; what observes a later edit is [`get_mail_status`], which asks
/// Postfix for its effective values and the kernel for its sockets.
///
/// # Cancellation class
///
/// Run to completion. A half-written configuration across four daemons is the one
/// state nothing recovers from by itself, so a dropped client must not stop it;
/// the remedy for a stream the panel lost is to ask again, which converges.
///
/// # Errors
///
/// Returns [`MailError::UnsupportedRelease`], [`MailError::HostnameNotUsable`],
/// [`MailError::CertificateMissing`] and [`MailError::NotInstalled`] with nothing
/// written. Returns
/// [`MailError::ConfigRejected`] when a daemon's own check refuses its files,
/// [`MailError::ServiceRefused`] when a unit will not come up, and
/// [`MailError::NotListening`] when the units are up and an expected port stays
/// unbound for the whole bounded wait. Returns [`MailError::Render`],
/// [`MailError::ConfigUnreadable`], [`MailError::ConfigWrite`],
/// [`MailError::SpawnFailed`], [`MailError::SocketsUnreadable`] and
/// [`MailError::Bound`] for the mechanical failures they name.
pub fn enable_mail(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    version_id: &str,
    settings: &MailSettings,
) -> Result<MailState, MailError> {
    let layout = MailLayout::resolve(distro, version_id)?;

    let hostname =
        Domain::parse(settings.hostname.as_str()).map_err(|_| MailError::HostnameNotUsable)?;
    let certificate = host.certificate_state(&hostname)?;
    if !certificate.installation.is_present() {
        return Err(MailError::CertificateMissing {
            domain: hostname.as_str().to_owned(),
            expected_path: certificate.certificate_path,
        });
    }

    let before = get_mail_status(host, distro, &layout)?;
    for state in &before.daemons {
        if !state.installed {
            return Err(MailError::NotInstalled {
                daemon: state.daemon,
            });
        }
    }
    let was_running = before.daemons.iter().any(|state| state.active);

    let passwd = host
        .read_config(Path::new(distro.passwd_database()))?
        .unwrap_or_default();
    let postfix_uid = postfix_uid_from_passwd(&passwd, distro.postfix_user())?;
    let stages = build_mail_stages(distro, &layout, settings, &certificate, postfix_uid)?;

    if let Err(failure) = bring_up(host, distro, &stages, settings) {
        if !was_running {
            // The failure is what the caller is owed. A rollback that also
            // failed is logged, because it is the state an operator has to be
            // told about and not one a caller can act on.
            if let Err(rollback) = stop_mail_daemons(host, distro, &layout) {
                tracing::error!(
                    original = %failure,
                    rollback = %rollback,
                    "the mail daemons could not be stopped after a failed enable"
                );
            }
        }
        return Err(failure);
    }
    get_mail_status(host, distro, &layout)
}

/// Seeds the tables, converges each stage in start order, and waits for the ports.
///
/// # Errors
///
/// As [`enable_mail`].
fn bring_up(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    stages: &[crate::mail::model::mail_stage::MailStage],
    settings: &MailSettings,
) -> Result<(), MailError> {
    seed_mail_maps(host, distro)?;
    for daemon in MailDaemon::START_ORDER {
        for stage in stages.iter().filter(|stage| stage.daemon == daemon) {
            converge_mail_stage(host, distro, stage)?;
        }
    }
    wait_for_mail_ports(host, &MailPorts::expected(settings.pop3_enabled))
}

#[cfg(test)]
#[path = "../tests/mail/enable_mail_tests.rs"]
mod tests;
