//! Turning DNS on: render, swap, check, restart, observe — and put the previous
//! configuration back if the daemon does not answer.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::adapter::DistroAdapter;
use maran_templates::bind::named_conf::NamedConf;

use crate::dns::dns_error::DnsError;
use crate::dns::dns_host::DnsHost;
use crate::dns::get_dns_status::get_dns_status;
use crate::dns::model::dns_state::DnsState;
use crate::dns::model::dns_unit::DnsUnit;
use crate::dns::wait_for_control_channel::wait_for_control_channel;
use crate::safe_write::model::{Reload, Validator};

/// Configures and starts this host's authoritative DNS daemon.
///
/// Idempotent: applying a configuration the host already has, while the unit is
/// running and its daemon answering, writes nothing and restarts nothing (a unit
/// missing only from the boot sequence is enabled in place).
///
/// "Already has" is judged on the daemon as well as the file — the same bytes on
/// disk with the daemon stopped (a disable leaves the file) are NOT
/// converged, and the operation falls through to the write, whose restart is what
/// starts it.
///
/// # What checks what
///
/// The config-write protocol renames the new file over the live one and THEN runs
/// the daemon's own `named-checkconf` on that path, because the checker reads the
/// configuration by path (rules/rust.md "Config writes"). A refusal restores the
/// previous bytes before any restart, so the running daemon never saw the refused
/// text. Only after the checker accepts does the protocol restart the unit. The
/// restart returns when the process is forked, so the operation then asks the
/// daemon itself, over its control channel, whether it is alive; a daemon that
/// never answers restores the previous configuration, restarts onto it and
/// returns [`DnsError::NotListening`]. With no previous configuration the unit is
/// stopped and the refused file removed, so a later enable cannot mistake it for
/// a live one.
///
/// # Concurrency
///
/// There is no DNS-area lock, and the rule for why is the one `enable_ftps`
/// states: the read of the live file and the write are not one critical section,
/// two enables both writing is last-writer-wins for an administrator-only
/// setting, and the rollback is made conditional on the file still holding the
/// bytes this operation wrote, so one operation's failure never reverts a
/// neighbour's success. No new lock joins the set in rules/rust.md.
///
/// # What it does not do
///
/// It does not open port 53 in the firewall. The panel does that as its own
/// administrator action, as it does for FTPS; this operation brings the daemon
/// up and reports `port_53_answering` so the panel can say it is listening on
/// loopback while the world may still be blocked.
///
/// # Errors
///
/// Returns [`DnsError::ConfigRejected`] when the daemon's checker refuses the
/// rendered file (previous bytes restored, daemon never restarted),
/// [`DnsError::ServiceRefused`] when the unit will not start or will not be
/// enabled, and [`DnsError::NotListening`] when the unit starts and the daemon
/// never answers — in the first two start cases with the previous configuration
/// restored. Returns [`DnsError::Render`], [`DnsError::ConfigUnreadable`],
/// [`DnsError::ConfigWrite`] and [`DnsError::SpawnFailed`] for the mechanical
/// failures they name, and [`DnsError::Bound`] when a program outruns its
/// deadline.
pub fn enable_dns(host: &dyn DnsHost, distro: &dyn DistroAdapter) -> Result<DnsState, DnsError> {
    let rendered = render(distro)?;
    let target = Path::new(AgentPaths::DNS_CONFIG_PATH);
    let previous = host.read_config(target)?;

    if previous.as_deref() == Some(rendered.as_str()) {
        let state = get_dns_status(host, distro)?;
        if state.running && state.control_channel_answered {
            if state.enabled_at_boot {
                return Ok(state);
            }
            // Serving and answering, only missing from the boot sequence: say so
            // to the service manager and leave the daemon alone. Restarting a
            // healthy daemon to add a symlink would drop its live queries.
            enable_at_boot(host, distro)?;
            return get_dns_status(host, distro);
        }
    }

    let checker = distro.dns_checkconf_binary();
    let service_manager = distro.service_manager();
    let the_checker_reads_the_file = Validator {
        program: &checker,
        arguments: &[AgentPaths::DNS_CONFIG_PATH],
        class: CallClass::Validate,
    };
    let the_unit_restarts = Reload {
        program: &service_manager,
        arguments: &DnsUnit::RESTART,
    };

    if let Err(refused) = host.write_config(
        target,
        &rendered,
        &the_checker_reads_the_file,
        &the_unit_restarts,
    ) {
        if matches!(refused, DnsError::ServiceRefused { .. }) && previous.is_some() {
            restart_on_the_restored_file(host, distro, &refused);
        }
        return Err(refused);
    }

    if !wait_for_control_channel(host, distro)? {
        restore_previous(host, distro, target, &rendered, previous.as_deref());
        return Err(DnsError::NotListening);
    }

    enable_at_boot(host, distro)?;
    get_dns_status(host, distro)
}

/// Puts the unit in the boot sequence, so a reboot does not silently end DNS.
///
/// # Errors
///
/// Returns [`DnsError::ServiceRefused`] when the service manager refuses,
/// [`DnsError::SpawnFailed`] when it cannot be started and [`DnsError::Bound`]
/// when it outruns its deadline.
fn enable_at_boot(host: &dyn DnsHost, distro: &dyn DistroAdapter) -> Result<(), DnsError> {
    let enabled = host.run(
        &distro.service_manager(),
        &DnsUnit::ENABLE,
        CallClass::Reload,
    )?;
    if enabled.status != 0 {
        return Err(DnsError::ServiceRefused {
            unit: AgentPaths::DNS_UNIT.to_owned(),
        });
    }
    Ok(())
}

/// Renders the daemon configuration.
///
/// Every input is an agent constant or a distro fact; none comes from the
/// request, so there is nothing to validate here (rules/rust.md "Validation
/// first" applies to caller-supplied values).
///
/// # Errors
///
/// Returns [`DnsError::Render`] when the template and its render type have
/// drifted apart.
fn render(distro: &dyn DistroAdapter) -> Result<String, DnsError> {
    NamedConf {
        control_key_path: AgentPaths::DNS_CONTROL_KEY_PATH.to_owned(),
        control_key_name: AgentPaths::DNS_CONTROL_KEY_NAME.to_owned(),
        control_port: AgentPaths::DNS_CONTROL_PORT,
        state_directory: distro.dns_state_directory().to_owned(),
    }
    .render_config()
    .map_err(|_| DnsError::Render)
}

/// Restarts the unit onto the configuration the config-write protocol restored,
/// after the restart onto the refused one failed.
///
/// Nothing is written: the bytes on disk are the ones the protocol captured
/// inside its lock, which are newer and more trustworthy than anything this
/// operation read before it. A failed restart is logged and nothing more — the
/// caller is already returning the error that brought it here.
fn restart_on_the_restored_file(
    host: &dyn DnsHost,
    distro: &dyn DistroAdapter,
    original: &DnsError,
) {
    if let Err(failed) = host.run(
        &distro.service_manager(),
        &DnsUnit::RESTART,
        CallClass::Reload,
    ) {
        tracing::error!(
            original = %original,
            rollback = %failed,
            "the dns daemon could not be restarted on the restored configuration"
        );
    }
}

/// Puts the host back the way it was found after a daemon that started and never
/// answered.
///
/// With a previous configuration it goes back through the same protocol the
/// forward write used, so the file is restored byte for byte AND the daemon is
/// restarted onto it. With none the unit is stopped and the refused file removed.
///
/// The restore is conditional on the file still holding exactly what this
/// operation wrote: a fact re-read after a wait may have expired
/// (rules/rust.md "What this agent serialises"), and writing `previous` back
/// unconditionally would revert another operation's newer write on the strength
/// of a value read before anything was serialised. A mismatch skips the rollback
/// and logs it.
///
/// Never returns an error: the caller owes its own error to the caller's caller,
/// and a rollback that also failed is logged because it is the state an operator
/// has to be told about.
fn restore_previous(
    host: &dyn DnsHost,
    distro: &dyn DistroAdapter,
    target: &Path,
    written: &str,
    previous: Option<&str>,
) {
    match host.read_config(target) {
        Ok(live) if live.as_deref() == Some(written) => {}
        Ok(_) => {
            tracing::warn!(
                "the dns configuration changed under a failed enable; the rollback is skipped \
                 rather than reverting the newer write"
            );
            return;
        }
        Err(unreadable) => {
            tracing::error!(
                rollback = %unreadable,
                "the dns configuration could not be read back, so the failed enable is not \
                 rolled back"
            );
            return;
        }
    }

    let checker = distro.dns_checkconf_binary();
    let service_manager = distro.service_manager();
    let outcome = match previous {
        Some(bytes) => host.write_config(
            target,
            bytes,
            &Validator {
                program: &checker,
                arguments: &[AgentPaths::DNS_CONFIG_PATH],
                class: CallClass::Validate,
            },
            &Reload {
                program: &service_manager,
                arguments: &DnsUnit::RESTART,
            },
        ),
        None => host
            .run(&service_manager, &DnsUnit::STOP, CallClass::Reload)
            .and_then(|_| host.remove_file(target)),
    };

    if let Err(failed) = outcome {
        tracing::error!(
            rollback = %failed,
            "the dns configuration could not be rolled back after a failed enable"
        );
    }
}

#[cfg(test)]
#[path = "../tests/dns/enable_dns_tests.rs"]
mod tests;
