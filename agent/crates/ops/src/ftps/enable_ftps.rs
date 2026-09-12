//! Turning FTPS on: probe, render, validate, swap, restart, observe — and put
//! the previous configuration back if the daemon does not answer.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::adapter::DistroAdapter;
use maran_templates::vsftpd::vsftpd_daemon_config::VsftpdDaemonConfig;

use crate::ftps::ftps_error::FtpsError;
use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::get_ftps_status::get_ftps_status;
use crate::ftps::model::ftps_configuration::FtpsConfiguration;
use crate::ftps::model::ftps_state::FtpsState;
use crate::ftps::model::ftps_unit::FtpsUnit;
use crate::ftps::model::listen_mode::ListenMode;
use crate::ftps::probe_listen_mode::probe_listen_mode;
use crate::ftps::validate_candidate_config::validate_candidate_config;
use crate::safe_write::model::{Reload, Validator};
use crate::ssl::CertificateState;

/// The transfer log the rendered configuration points vsftpd at.
///
/// Under `/var/log/maran` with the panel's other logs, and rotated by
/// `installer/logrotate/maran-ftps`.
///
/// **That agreement is compared, not assumed.** This constant and the rotation
/// policy are two spellings of one file in two languages that cannot read each
/// other -- the policy is copied to `/etc/logrotate.d` at install time, long
/// before any agent renders a configuration -- so a drift would leave the daemon
/// writing a file nothing rotates, growing one line per login and per transfer
/// on the busiest host, with no unit failing and no gate moving until a
/// partition fills. `assert_the_installer_and_the_agent_spell_the_ftps_names_the_same`
/// in `docker/polygon/assert-installer-steps.sh` reads this literal and requires
/// the rotation stanza to name exactly it, and requires the same of every log
/// path written anywhere in that policy, in `installer/lib/89-ftps.sh` and in
/// this file. It is the same comparison the FTPS group, jail root and PAM
/// service name already had.
///
/// **Deliberately not in `AgentPaths`, and the reason is a real distinction
/// rather than a filing preference.** That type is the set of locations the
/// agent itself creates and writes, and `maran structure` enforces exactly that
/// reading: every absolute constant there outside `/etc` must appear in the
/// agent unit's `ReadWritePaths=`. This file is not one of them. The agent never
/// opens it — it renders the path into a configuration, and the FTPS daemon,
/// running under its own unit, is what creates and writes it. Putting it there
/// would have made the agent's own writable set claim a file it never touches,
/// which is the drift that gate exists to catch, pointed the wrong way.
///
/// It is not a platform fact either — both families get this same path — so it
/// is a constant here, beside the one function that renders it, rather than a
/// `DistroAdapter` method.
const FTPS_LOG_PATH: &str = "/var/log/maran/ftps.log";

/// Configures and starts this host's FTPS daemon.
///
/// Idempotent: applying a configuration the host already has writes nothing and
/// restarts nothing. That is not a nicety — a nightly reconcile that re-applied
/// the same file would bounce every live FTP session on the server once a day.
///
/// # It refuses rather than creating certificate material
///
/// The first thing it does is ask what is installed for the hostname, and it
/// returns [`FtpsError::CertificateMissing`] — naming the path — when there is
/// nothing. FTPS never writes into the certificate store: that store is
/// `ops::ssl`'s, and a daemon that quietly generated its own self-signed
/// placeholder would put a certificate every client warns about behind this
/// feature's promise of forced TLS, with nothing on any screen saying so. The
/// operator creates the site and lets the SSL module place material there.
///
/// # The two validation layers, and what each can and cannot see
///
/// **Before the swap**, [`validate_candidate_config`] runs the daemon against the
/// rendered text on a loopback port. That is the layer that catches a
/// configuration vsftpd will not parse, and it catches it while the live file is
/// still untouched. It cannot see anything about the real port, the real unit or
/// a real handshake.
///
/// **After the swap**, the config-write protocol restarts the unit and asks
/// whether it is active, and then this function asks the daemon itself for a
/// `220` greeting on the control port. That is the layer that catches what no
/// parse can: a certificate whose key does not match parses perfectly and fails
/// at the first handshake; a port something else already holds leaves a unit
/// systemd is happy with. Either answer being no restores the previous
/// configuration, restarts the daemon on it, and returns the original error.
///
/// **What neither layer can see**, said plainly so nobody concludes that
/// "validated twice" means "cannot be silently unmade": an edit made to the live
/// file AFTER the swap. The first layer validated text this function had just
/// rendered and never meets a later edit at all; the second asks liveness
/// questions that a config with its forced-TLS keys switched off answers
/// perfectly — a healthy daemon doing the wrong thing. What observes that is
/// [`get_ftps_status`], which reads the LAST occurrence of every key it looks at.
///
/// # Concurrency: no sixth lock, and what stands in for one
///
/// This operation reads the live configuration, decides, and only then enters
/// the config-write protocol, where `config_tree_lock` is taken. So the read
/// and the write are NOT one critical section, which rules/rust.md otherwise
/// requires ("Read-modify-write is one critical section, not just the write").
/// That is stated rather than hidden, because two things follow from it and
/// only one of them was ever dangerous.
///
/// What is NOT dangerous: two enables both writing. The protocol serialises the
/// writes themselves, each captures the previous bytes inside its own lock, and
/// the loser's configuration is simply superseded — last writer wins, which for
/// an admin-only server-level setting is the ordinary meaning of two people
/// pressing the button.
///
/// What WAS dangerous, and is what this function now guards: the ROLLBACK.
/// `previous` is read before any lock is held, so an enable whose post-swap
/// check fails would have written a pre-lock value back over a neighbour's
/// successful, newer configuration — turning one operation's failure into
/// another operation's silent reversal, with the panel holding the state the
/// first one returned. Two changes close it without a lock:
///
/// - when the protocol itself refuses, nothing is written back at all. The
///   protocol has already restored the bytes IT captured under its own lock,
///   and this function only restarts the daemon onto them.
/// - when the daemon comes up and does not answer, the live file is re-read and
///   the restore happens only if it still holds exactly what this operation
///   wrote. A mismatch means our write has already been superseded and there is
///   nothing of ours to take back, so the rollback is skipped and logged.
///
/// **Why not a lock.** `config_tree_lock` cannot be taken here: the protocol
/// takes it again below, it waits, and a mutex taken twice on one thread
/// deadlocks. A new FTPS-area lock would be the SIXTH in this workspace, and
/// rules/rust.md names a lock added by someone who could not see the other five
/// as how a deadlock gets built — the five are enumerated in
/// `crate::accounts::account_lock`. Neither of the two per-account locks
/// applies: this is a server-level setting with no account to scope it to. The
/// re-read above is a compare-and-swap that costs one `read_config` and adds no
/// edge to the wait-for graph, and it is the same discipline this agent already
/// applies to an account's uid: a fact re-read after a wait is a fact that may
/// have expired, and the operation refuses on a mismatch.
///
/// `disable_ftps` and `reload_ftps_tls` need none of this: they read and write
/// no configuration at all, only run an idempotent service-manager verb.
///
/// # Errors
///
/// Returns [`FtpsError::CertificateMissing`] when nothing is installed for the
/// hostname, with nothing written. Returns [`FtpsError::ConfigRejected`] when the
/// daemon refuses the candidate, again with nothing written. Returns
/// [`FtpsError::ServiceRefused`] when the unit will not come up on the new
/// configuration and [`FtpsError::NotListening`] when it comes up and the control
/// port stays silent — both with the previous configuration restored and the
/// daemon restarted on it. Returns [`FtpsError::Render`],
/// [`FtpsError::ConfigUnreadable`], [`FtpsError::ConfigWrite`] and
/// [`FtpsError::SpawnFailed`] for the mechanical failures they name.
pub fn enable_ftps(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
    configuration: &FtpsConfiguration,
) -> Result<FtpsState, FtpsError> {
    let certificate = host.certificate_state(&configuration.hostname)?;
    if !certificate.present {
        return Err(FtpsError::CertificateMissing {
            domain: configuration.hostname.as_str().to_owned(),
            expected_path: certificate.certificate_path,
        });
    }

    // The mode is an INPUT to the render, so the probe runs before it. It is a
    // question about the kernel and not about the request, which is why nothing
    // in `FtpsConfiguration` can override it.
    let listen_mode = probe_listen_mode(host);
    let rendered = render(distro, configuration, listen_mode, &certificate)?;

    let target = Path::new(AgentPaths::VSFTPD_CONFIG_PATH);
    let previous = host.read_config(target)?;
    if previous.as_deref() == Some(rendered.as_str()) {
        // Converged. Nothing is written and nothing is restarted — the state is
        // still OBSERVED below, so a caller that arrives here is told what the
        // daemon is doing rather than what this function decided not to do.
        return get_ftps_status(host, distro, Some(&configuration.hostname));
    }

    validate_candidate_config(host, distro, &rendered)?;

    // The two argv the config-write protocol runs after the swap. They occupy
    // its validator and reload slots, and what they mean here is worth naming:
    // this file has no validating tool at all — vsftpd has no `-t` — so the only
    // thing that can judge it in place is the daemon reading it, which is the
    // restart. The unit is `Type=simple`, so that restart returns as soon as the
    // process has been forked and says nothing about whether it is still alive;
    // `is-active` is therefore a second, genuinely different question, not a
    // restatement of the first. Both are steps the protocol restores the previous
    // content on, which is exactly the behaviour wanted for each.
    let daemon_reads_the_file = Validator {
        program: distro.service_manager(),
        arguments: &FtpsUnit::RESTART,
    };
    let the_unit_is_still_up = Reload {
        program: distro.service_manager(),
        arguments: &FtpsUnit::IS_ACTIVE,
    };

    if let Err(refused) = host.write_config(
        target,
        &rendered,
        &daemon_reads_the_file,
        &the_unit_is_still_up,
    ) {
        // The protocol has already put back the bytes IT captured, inside its
        // own lock, and this function does not write anything over them: its
        // own `previous` was read before the lock was ever held and may be a
        // superseded configuration by now. What is left to do is restart the
        // daemon onto the file the protocol restored, because it restarted onto
        // the content that was refused and not onto the content it put back.
        restart_on_the_restored_file(host, distro, &refused);
        return Err(refused);
    }

    let state = get_ftps_status(host, distro, Some(&configuration.hostname))?;
    if !state.control_port_answered {
        restore_previous(host, distro, target, &rendered, previous.as_deref());
        return Err(FtpsError::NotListening);
    }

    Ok(state)
}

/// Renders the daemon configuration for `configuration` in `listen_mode`.
///
/// The three TLS option NAMES come from the adapter and are copied straight into
/// the render type: this is the one place that reads them, so `ops` branches on
/// no family and the templates crate never learns which one it is rendering for
/// (rules/rust.md "Distro adapter").
///
/// # Errors
///
/// Returns [`FtpsError::Render`] when the template and its render type have
/// drifted apart.
fn render(
    distro: &dyn DistroAdapter,
    configuration: &FtpsConfiguration,
    listen_mode: ListenMode,
    certificate: &CertificateState,
) -> Result<String, FtpsError> {
    let tls_keys = distro.vsftpd_tls_version_keys();

    VsftpdDaemonConfig {
        certificate_path: certificate.certificate_path.clone(),
        private_key_path: certificate.private_key_path.clone(),
        passive_port_min: configuration.passive_port_min.value(),
        passive_port_max: configuration.passive_port_max.value(),
        passive_address: configuration
            .passive_address
            .as_ref()
            .map(|address| address.as_str().to_owned()),
        max_clients: configuration.max_clients,
        log_path: FTPS_LOG_PATH.to_owned(),
        ipv4_only: listen_mode == ListenMode::Ipv4Only,
        tls_v1_key: tls_keys.tls_v1,
        tls_v1_1_key: tls_keys.tls_v1_1,
        tls_v1_2_key: tls_keys.tls_v1_2,
    }
    .render_config()
    .map_err(|_| FtpsError::Render)
}

/// Restarts the daemon onto the configuration the config-write protocol
/// restored, after that protocol refused a write.
///
/// The protocol's own rollback puts the previous BYTES back, but its last act
/// was a restart onto the content that was refused — so the daemon is running
/// (or stopped) on a file that is no longer there. Nothing is written here, and
/// deliberately: the bytes on disk are the ones the protocol captured inside its
/// lock, which are newer and more trustworthy than anything this operation read
/// before it.
///
/// A failed restart is logged and nothing more. The caller is already returning
/// the error that brought it here, and replacing that error with the rollback's
/// would hide the reason the operation failed.
fn restart_on_the_restored_file(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
    original: &FtpsError,
) {
    if let Err(failed) = host.run(distro.service_manager(), &FtpsUnit::RESTART) {
        tracing::error!(
            original = %original,
            rollback = %failed,
            "the ftps daemon could not be restarted on the restored configuration"
        );
    }
}

/// Puts the host back the way it was found after `original` made the new
/// configuration untenable.
///
/// With a previous configuration, it goes back through the same protocol the
/// forward write used — so the file is restored byte-for-byte AND the daemon is
/// restarted onto it, which is the half `write_config`'s own rollback cannot do.
///
/// With none — an enable that had nothing to go back to — the daemon is stopped
/// and the refused file is left on disk. Removing it would buy nothing: the file
/// is inert, vsftpd reads it only when the unit starts, and the next enable
/// replaces it whole. Leaving it lets an operator look at what was refused.
///
/// Never returns an error. `original` is what the caller is owed and what the
/// caller gets; a rollback that also failed is logged, because it is the state an
/// operator has to be told about and not one a caller can act on.
fn restore_previous(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
    target: &Path,
    written: &str,
    previous: Option<&str>,
) {
    // A fact re-read after a wait is a fact that may have expired
    // (rules/rust.md, "What this agent serialises"). `previous` was captured
    // before the config-write protocol took its lock, so between then and now
    // another EnableFtps or a DisableFtps may have committed a configuration of
    // its own. Writing `previous` back unconditionally would then revert THAT
    // operation — a rollback of somebody else's successful write, on the
    // strength of a value read before anything was serialised.
    //
    // So the restore is conditional on the file still holding exactly what this
    // operation wrote. If it does not, this operation's write has already been
    // superseded and there is nothing of ours left to take back; the caller
    // still gets `NotListening`, which is the truth about the daemon it
    // observed. This is a compare-and-swap done with a read rather than a sixth
    // lock, and it is the shape the rules ask for: there is no FTPS-area lock,
    // `config_tree_lock` cannot be held around a call that takes it again, and
    // adding a lock nobody could see the other five from is the thing
    // rules/rust.md names as how a deadlock gets built.
    match host.read_config(target) {
        Ok(live) if live.as_deref() == Some(written) => {}
        Ok(_) => {
            tracing::warn!(
                "the ftps configuration changed under a failed enable; the \
                 rollback is skipped rather than reverting the newer write"
            );
            return;
        }
        Err(unreadable) => {
            tracing::error!(
                rollback = %unreadable,
                "the ftps configuration could not be read back, so the failed \
                 enable is not rolled back"
            );
            return;
        }
    }

    let original = FtpsError::NotListening;
    let outcome = match previous {
        Some(bytes) => host.write_config(
            Path::new(AgentPaths::VSFTPD_CONFIG_PATH),
            bytes,
            &Validator {
                program: distro.service_manager(),
                arguments: &FtpsUnit::RESTART,
            },
            &Reload {
                program: distro.service_manager(),
                arguments: &FtpsUnit::IS_ACTIVE,
            },
        ),
        None => host
            .run(distro.service_manager(), &FtpsUnit::STOP)
            .map(|_| ()),
    };

    if let Err(failed) = outcome {
        tracing::error!(
            original = %original,
            rollback = %failed,
            "the ftps configuration could not be rolled back after a failed enable"
        );
    }
}

#[cfg(test)]
#[path = "../tests/ftps/enable_ftps_tests.rs"]
mod tests;
