//! An observed mail stack onto the wire status every lifecycle rpc answers with.

use maran_agent_core::validation::mail::mail_password_scheme::MailPasswordScheme as HostScheme;
use maran_ops::mail::{
    MailDaemon as HostDaemon, MailPorts, ObservedMail, PortBinding, ToolVersion,
};

use crate::proto::{
    MailDaemon, MailDaemonStatus, MailListener, MailObservation, MailPasswordScheme, MailQueueSize,
    MailStatus,
};

/// The address a listener reachable from other hosts is reported with.
const REACHABLE_ADDRESS: &str = "*";

/// The address a listener that only this host can reach is reported with.
const LOOPBACK_ADDRESS: &str = "127.0.0.1";

/// Builds the `MailStatus` that `GetMailStatus`, `EnableMail`, `DisableMail` and
/// `ReloadMailTls` all answer with, so the reading of an observed stack happens
/// once for all four.
///
/// `host_scheme` is the password scheme the distribution says its IMAP server
/// verifies; whether the running server agrees is the observed
/// `password_scheme_verified`, which is reported beside it and never replaced by
/// it.
///
/// What this status does not carry, and says so rather than guessing:
///
/// - **Listener addresses are two values, not the kernel's.** The kernel's table
///   is reduced to "reachable from other hosts" or "loopback only" before it gets
///   here, so a listener is `*` or `127.0.0.1`. A daemon's listeners are the mail
///   ports of its protocols that are bound, attributed by port and not by owning
///   process.
/// - **`enabled` is derived from activity**: every daemon of the stack active.
///   Whether the units are in the boot sequence is not read.
/// - **`tls_in_use` is the MTA's reading only**: its configuration names a
///   certificate chain. The IMAP server's certificate is not read back.
/// - **The virus scanner has no entry.** This build cannot enable it
///   (`validated_mail_settings` refuses it), so the stack it reports does not
///   contain it.
/// - **Spam filter and key-value server list no TCP listeners and say
///   `listeners_observed = NOT_OBSERVED`**: they talk over unix sockets, and the
///   kernel table read here is only for the seven mail ports.
#[must_use]
pub fn reported_mail_status(observed: &ObservedMail, host_scheme: HostScheme) -> MailStatus {
    let state = &observed.state;
    let details = &observed.details;
    let reading_of = |daemon: HostDaemon| state.daemons.iter().find(|each| each.daemon == daemon);

    let daemon_status = |wire: MailDaemon, daemon: HostDaemon, version: Option<&ToolVersion>| {
        let reading = reading_of(daemon);
        let (ports, listeners_observed) = match daemon {
            HostDaemon::Postfix => (MailPorts::POSTFIX.to_vec(), MailObservation::Yes),
            HostDaemon::Dovecot => (
                [MailPorts::DOVECOT, MailPorts::POP3].concat(),
                MailObservation::Yes,
            ),
            HostDaemon::Rspamd | HostDaemon::KeyValueServer => {
                (Vec::new(), MailObservation::NotObserved)
            }
        };
        MailDaemonStatus {
            daemon: wire as i32,
            installed: observation(reading.map(|each| each.installed)) as i32,
            version: version
                .map(|each| each.text().to_owned())
                .unwrap_or_default(),
            active: observation(reading.map(|each| each.active)) as i32,
            listeners: listeners_of(observed, &ports),
            listeners_observed: listeners_observed as i32,
        }
    };

    let every_daemon_active =
        !state.daemons.is_empty() && state.daemons.iter().all(|each| each.active);
    let dovecot_installed = reading_of(HostDaemon::Dovecot).is_some_and(|each| each.installed);
    let password_scheme = if dovecot_installed {
        wire_scheme(host_scheme)
    } else {
        MailPasswordScheme::Unspecified
    };

    MailStatus {
        daemons: vec![
            daemon_status(
                MailDaemon::Mta,
                HostDaemon::Postfix,
                details.postfix_version.as_ref(),
            ),
            daemon_status(
                MailDaemon::ImapServer,
                HostDaemon::Dovecot,
                details.dovecot_version.as_ref(),
            ),
            daemon_status(
                MailDaemon::SpamFilter,
                HostDaemon::Rspamd,
                details.rspamd_version.as_ref(),
            ),
            daemon_status(MailDaemon::RateLimitStore, HostDaemon::KeyValueServer, None),
        ],
        enabled: observation(Some(every_daemon_active)) as i32,
        effective_hostname: state.effective_hostname.clone().unwrap_or_default(),
        tls_in_use: observation(state.tls_configured) as i32,
        dovecot_major: details
            .dovecot_version
            .as_ref()
            .map_or(0, ToolVersion::major),
        dovecot_minor: details
            .dovecot_version
            .as_ref()
            .map_or(0, ToolVersion::minor),
        password_scheme: password_scheme as i32,
        password_scheme_verified: observation(details.password_scheme_listed) as i32,
        queue: details
            .queue_messages
            .map(|messages| MailQueueSize { messages }),
        sieve_available: observation(details.sieve_available) as i32,
    }
}

/// The bound listeners among `ports`, in port order. A port nothing listens on has
/// none.
fn listeners_of(observed: &ObservedMail, ports: &[u16]) -> Vec<MailListener> {
    ports
        .iter()
        .filter_map(|&port| {
            let binding = observed
                .state
                .ports
                .iter()
                .find(|state| state.port == port)?
                .binding;
            let address = match binding {
                PortBinding::NotBound => return None,
                PortBinding::LoopbackOnly => LOOPBACK_ADDRESS,
                PortBinding::Public => REACHABLE_ADDRESS,
            };
            Some(MailListener {
                address: address.to_owned(),
                port: u32::from(port),
            })
        })
        .collect()
}

/// A reading that may not have been made onto the three-valued wire enum: `None`
/// is "not observed", which certifies nothing either way.
fn observation(reading: Option<bool>) -> MailObservation {
    match reading {
        Some(true) => MailObservation::Yes,
        Some(false) => MailObservation::No,
        None => MailObservation::NotObserved,
    }
}

/// The host's scheme onto the wire enum.
fn wire_scheme(scheme: HostScheme) -> MailPasswordScheme {
    match scheme {
        HostScheme::Argon2id => MailPasswordScheme::Argon2id,
        HostScheme::BlowfishCrypt => MailPasswordScheme::BlfCrypt,
    }
}

#[cfg(test)]
#[path = "../../tests/services/mail/reported_mail_status_tests.rs"]
mod tests;
